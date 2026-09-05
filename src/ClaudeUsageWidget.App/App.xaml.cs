using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClaudeUsageWidget.App.Tray;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.App.Web;
using ClaudeUsageWidget.App.Windows;
using ClaudeUsageWidget.Core;
using Microsoft.Win32;

namespace ClaudeUsageWidget.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
// The fully qualified name is required: UseWindowsForms makes
// System.Windows.Forms.Application visible in this file, and Application
// becomes an ambiguous reference.
public partial class App : System.Windows.Application
{
    private TrayIcon? _trayIcon;
    private DesktopWidgetWindow? _widgetWindow;
    private TaskbarBandWindow? _bandWindow;
    private SettingsStore? _settings;
    private StatusStore? _statusStore;
    private DispatcherTimer? _refreshTimer;

    /// Edit mode is a session state, not a setting: a widget that starts up in
    /// edit mode because it was closed in edit mode would be a trap.
    private bool _editingLayout;

    /// One AccountRuntime per configured account, in the settings' order.
    /// Recreated entirely when an account is added/removed, rather than
    /// mutated in place — this way the row order on the panel and the order
    /// in the settings cannot drift apart.
    private IReadOnlyList<AccountRuntime> _accounts = [];

    // UsageStore.hasCredentials is a synchronous lambda, while
    // HasSessionCookieAsync goes into WebView2 and cannot be synchronous.
    // Blocking the UI thread via .GetAwaiter().GetResult() (as in the
    // brief's draft) would mean hanging the entire Dispatcher — including
    // rendering and click handling — for however long
    // CoreWebView2Environment is not yet ready on the first call. Instead,
    // RefreshAllAsync updates this field asynchronously right before
    // calling LoadAsync(), and the lambda just reads the already-ready
    // value.
    ///
    /// Keyed by account id: one shared flag for four accounts would mean
    /// that a signed-in account reads as signed-out the moment a
    /// neighboring account loses its cookie.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _hasSessionCookie = new();

    // Separate from _hasSessionCookie: becomes true when the cookie check
    // itself (HasSessionCookieAsync) threw an exception (e.g. the WebView2
    // Runtime is not installed, or the profile is unreachable) — that is,
    // when we could not even determine whether the cookie exists or not,
    // rather than when we honestly determined that it does not. Read from
    // UpdateTrayTooltip so such a failure does not get silently lost in
    // FireAndForget/Debug output — see the comment in RefreshAllAsync.
    /// Also keyed by account: WebView2 can break on one profile and keep
    /// working on the rest.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _webViewFailing = new();

    /// Named single-instance mutex — kept as a field so the GC does not
    /// release it for the entire lifetime of the process.
    private System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The XAML binds the palette with DynamicResource; the keys must exist
        // before the first view loads (the preview included).
        Theme.PublishResources();

        LayoutPreview.RunIfRequested();
        Tray.MenuPreview.RunIfRequested();

        // One instance per session. Live debugging (2026-08-06) caught TWO
        // instances running at once (zombies from earlier `dotnet run`
        // launches): their bands and widgets fought over
        // position/visibility, which looked from the outside like chaotic
        // flickering. A widget with a tray icon never needs a second
        // instance.
        _singleInstanceMutex = new System.Threading.Mutex(
            initiallyOwned: true, "ClaudeUsageWidget.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        // For a tray widget, an unhandled exception on the UI thread (which
        // WPF terminates the process after, by default) is worse than one
        // wrong or stale number on the panel — the tray icon and the
        // already-rendered state must survive the failure of a particular
        // handler rather than take the whole process down with it. No
        // dialogs, deliberately minimal: this is a background widget, not a
        // window with someone around to click "OK".
        DispatcherUnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled UI-thread exception: {args.Exception}");
            // Debug output reaches nobody on a tray widget; the log is the
            // only place a swallowed exception can be seen afterwards.
            WidgetLog.Write("-", "unhandled", $"dispatcher {args.Exception.GetType().Name}: {args.Exception.Message}");
            args.Handled = true;
        };

        // With no window and no MainWindow, the app stays alive as long as
        // the tray object is alive and ShutdownMode remains
        // OnExplicitShutdown (see App.xaml) — otherwise WPF would close the
        // process right after OnStartup, without waiting for Quit.
        _trayIcon = new TrayIcon();
        _trayIcon.QuitRequested += Shutdown;
        // Tray account only: manually refreshing all four is exactly what
        // this endpoint punishes with a rate limit.
        _trayIcon.RefreshRequested += () => FireAndForget(async () =>
        {
            if (TrayAccount() is { } account) await RefreshAccountAsync(account);
            await _statusStore!.LoadAsync();
        });
        _trayIcon.SignInRequested += () => FireAndForget(async () =>
        {
            WidgetLog.Write(TrayAccount()?.Profile.DisplayName ?? "-", "sign-in-requested", "source=tray");
            if (TrayAccount() is { } account) await account.Session.OpenLoginWindowAsync();
        });
        _trayIcon.TrayMetricSelected += OnTrayMetricSelected;
        _trayIcon.ModelBucketSelected += OnModelBucketSelected;
        _trayIcon.ShowOnDesktopToggled += OnShowOnDesktopToggled;
        _trayIcon.TaskbarBandToggled += OnTaskbarBandToggled;
        _trayIcon.BandPositionSelected += OnBandPositionSelected;
        _trayIcon.LockPositionToggled += OnLockPositionToggled;
        _trayIcon.MenuOpening += RefreshTrayMenuState;
        _trayIcon.AccountSelected += OnAccountSelected;

        _trayIcon.EditLayoutToggled += () => SetEditingLayout(!_editingLayout);
        _trayIcon.PanelViewSelected += OnPanelViewSelected;
        _trayIcon.BandViewSelected += OnBandViewSelected;
        _trayIcon.ThemeSelected += OnThemeSelected;
        _trayIcon.AccountAddRequested += OnAccountAddRequested;
        _trayIcon.AccountRenameRequested += OnAccountRenameRequested;
        _trayIcon.AccountRemoveRequested += id => FireAndForget(() => OnAccountRemoveRequestedAsync(id));

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageWidget", "settings.json");
        _settings = new SettingsStore(settingsPath);

        BuildAccounts();

        // The palette before any window exists, and live tracking of Windows'
        // switch for as long as the app runs.
        ApplyTheme();
        SystemTheme.Changed += OnSystemThemeChanged;
        SystemTheme.Start(Dispatcher);

        _statusStore = new StatusStore(new StatusApi().FetchAsync);
        _statusStore.Changed += OnStoresChanged;

        _widgetWindow = new DesktopWidgetWindow(_settings);
        _widgetWindow.HideRequested += OnWidgetHideRequested;
        // The same handler the tray's Layout menu uses: two save paths for one
        // record is how they drift.
        _widgetWindow.LayoutEdited += OnLayoutSelected;
        // The panel's Done button and the tray's Edit layout are one toggle,
        // through the one method that keeps the flag, the tick and the mode
        // agreeing.
        _widgetWindow.EditDoneRequested += () => SetEditingLayout(false);
        // The hover header's pencil: the third way into the same toggle.
        _widgetWindow.EditToggleRequested += () => SetEditingLayout(!_editingLayout);
        _widgetWindow.StatusModeSelected += OnStatusModeSelected;
        _widgetWindow.ModelDialSelected += OnModelDialSelected;
        _widgetWindow.PlanLineSelected += OnPlanLineSelected;
        _widgetWindow.SignInRequested += () => FireAndForget(async () =>
        {
            // The user reported the panel's Sign in button "does nothing"
            // (2026-09-04) with no login-window line in the log: this line says
            // whether the click even arrives.
            WidgetLog.Write(TrayAccount()?.Profile.DisplayName ?? "-", "sign-in-requested", "source=panel");
            if (TrayAccount() is { } account) await account.Session.OpenLoginWindowAsync();
        });

        // The server updates its numbers slowly — matches
        // UsageStore.RefreshIntervalSeconds/StatusStore.RefreshIntervalSeconds;
        // both stores silently coalesce more frequent requests anyway, but
        // there is no point ticking faster than there is new data.
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(UsageStore.RefreshIntervalSeconds) };
        _refreshTimer.Tick += (_, _) => FireAndForget(RefreshAllAsync);
        _refreshTimer.Start();

        // Port of didWakeNotification: the laptop slept longer than the
        // timer interval — do not wait for the next tick, refresh right
        // after waking up.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();

        // Port of the stored widgetVisible state: if the user hid the
        // widget with the eye icon in a previous run, it must not come back
        // on its own — otherwise "Show on desktop" in the menu and what is
        // actually on screen would drift apart immediately after startup.
        if (_settings.Load().WidgetVisible) _widgetWindow.Show();

        // Same principle as WidgetVisible on the line above: the taskbar
        // band's state survives an application restart.
        if (_settings.Load().TaskbarBandEnabled) SetTaskbarBandVisible(true);

        FireAndForget(StartupAsync);
    }

    /// Port of applicationDidFinishLaunching: if there is no session
    /// cookie, the login window opens right away, without waiting for a
    /// tray click.
    ///
    /// Both steps here are wrapped in try — a WebView2 failure (e.g. the
    /// Runtime is not installed) at startup must not prevent
    /// RefreshAllAsync() below from running: it will retry on its own and
    /// correctly report the same error (see its comment), and StatusStore
    /// must load regardless of what happened to the web session.
    private async Task StartupAsync()
    {
        // The login window is opened only for the tray account, even if
        // several accounts have no cookie: four login windows at once on
        // startup is not help, it is an ambush. The rest will show empty
        // dials and sign in via a click from the Accounts submenu.
        var account = TrayAccount();
        if (account is null) return;

        var hasSession = false;
        try
        {
            hasSession = await account.Session.HasSessionCookieAsync();
        }
        catch
        {
            // Do nothing: hasSession stays false, and the code below will
            // still try to open LoginWindow — that is fine even if the
            // WebView2 environment is broken. EnsureEnvironmentAsync inside
            // OpenLoginWindowAsync will either recreate the environment
            // (see its faulted-state reset) or just cheaply fail again —
            // and that failure is caught separately below.
            // RefreshAllAsync() after this block will hit the same error
            // either way and honestly report it, regardless of whether the
            // login window opened or not.
        }

        if (!hasSession)
        {
            try
            {
                await account.Session.OpenLoginWindowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open the sign-in window: {ex}");
            }
        }

        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        // The cookie check is in its own try/catch, separate from the
        // Task.WhenAll below: if it throws (a WebView2 failure, not just
        // "no cookie"), that must not prevent _statusStore.LoadAsync() from
        // running — it reads the public status.claude.com endpoint, which
        // does not depend on WebView2, and its fate has nothing to do with
        // the web session's state.
        var accounts = _accounts;

        // Accounts are spread out across the five-minute cycle rather than
        // firing all at once: PollSchedule.OffsetFor. We only wait for the
        // service status — it is shared, and there is no reason to delay
        // it by 225 seconds for the sake of the last account.
        for (var i = 0; i < accounts.Count; i++)
        {
            var account = accounts[i];
            var offset = PollSchedule.OffsetFor(i, accounts.Count);
            if (offset == TimeSpan.Zero)
            {
                await RefreshAccountAsync(account);
                continue;
            }

            // The continuation must return to the UI thread: both WebView2
            // and the render triggered by Changed live on the Dispatcher.
            _ = Task.Delay(offset).ContinueWith(
                _ =>
                {
                    // The account may have been removed while this waited: its
                    // profile is already cleared, and a poll now would only log a
                    // sign-in failure for a row that no longer exists.
                    if (!_accounts.Contains(account)) return;
                    FireAndForget(() => RefreshAccountAsync(account));
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        await _statusStore!.LoadAsync();
    }

    private async Task RefreshAccountAsync(AccountRuntime account)
    {
        var id = account.Profile.Id;
        try
        {
            _hasSessionCookie[id] = await account.Session.HasSessionCookieAsync();
            _webViewFailing[id] = false;
        }
        catch (Exception ex)
        {
            // Deliberately NOT false: setting _hasSessionCookie to false
            // here would make UsageStore.LoadAsync below short-circuit to
            // Failed(NoCredentials) before even calling _fetch — which is a
            // wrong, misleading diagnosis ("not signed in", when in fact
            // WebView2 is not working, and the Sign in button below would
            // fail exactly the same way). Leaving it true lets LoadAsync
            // reach _fetch() = session.FetchUsageAsync, which will itself
            // run into the same error inside its own
            // HasSessionCookieAsync — and this time it will be caught by
            // UsageStore.PerformLoadAsync (the outer catch(Exception)),
            // turning it into an honest Failed(Network(ex.Message)),
            // visible in the status line on the panel. UpdateTrayTooltip
            // below additionally highlights the same thing in the tray
            // tooltip until the failure clears.
            _hasSessionCookie[id] = true;
            _webViewFailing[id] = true;
            System.Diagnostics.Debug.WriteLine($"WebView2 session check failed for '{id}': {ex}");
        }

        await account.Store.LoadAsync();
        ExportUsage(account);
    }

    /// A side channel for another tool, off by default.
    ///
    /// Two ways to switch it on, and their order matters: an account's
    /// ExportPath is a targeted override and always wins; ExportDirectory in
    /// the settings turns the export on for ALL accounts at once, each into
    /// its own file. The reader is another tool, and it wants every account, not
    /// just the one that once had a path written in by hand.
    private void ExportUsage(AccountRuntime account)
    {
        var data = _settings!.Load();

        // ExportPath comes from the freshly loaded settings, not from
        // account.Profile: it is set by hand-editing settings.json, and such
        // an edit must take effect on the next refresh, not after a restart.
        var path = data.Accounts.FirstOrDefault(a => a.Id == account.Profile.Id)?.ExportPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (string.IsNullOrWhiteSpace(data.ExportDirectory)) return;

            // The file name comes from DisplayName, with the id only as a
            // fallback: the reader wants `personal.widget.json`, not a GUID.
            // The profile here is current — ReloadAccountProfiles pulls a
            // rename into the live AccountRuntime.
            path = Path.Combine(
                data.ExportDirectory,
                UsageExport.FileNameFor(account.Profile.DisplayName, account.Profile.Id) + ".widget.json");
        }

        // Only on a fresh successful response: overwriting the file with
        // the last good snapshot would mean bumping its "age" without
        // learning anything new.
        if (account.Store.CurrentState is not UsageState.Ok(var snapshot, _)) return;
        // ModelBucket is the same choice the third dial shows; otherwise the
        // file and the panel would call two different limits by one name.
        if (UsageExport.Payload(snapshot, DateTimeOffset.Now, account.Profile.DisplayName, data.ModelBucket)
            is not { } payload) return;

        UsageExportWriter.Write(path, payload);
    }

    private void OnSignedIn(string accountId)
    {
        // The login may have gone through a different account — the
        // organization id saved from the previous session must not survive
        // a new sign-in. Port of fetchUsage's onSignedIn equivalent in
        // applicationDidFinishLaunching.
        var account = _accounts.FirstOrDefault(a => a.Profile.Id == accountId);
        if (account is null) return;

        account.Session.ClearCachedOrganization();
        // This account only: signing into one is not a reason to hit
        // claude.ai for the other three.
        FireAndForget(() => RefreshAccountAsync(account));
    }

    private void OnWidgetHideRequested()
    {
        // DesktopWidgetWindow itself has already hidden itself and saved
        // WidgetVisible=false in OnEyeClicked before raising this event —
        // there is nothing to redo here except pull the cleared "Show on
        // desktop" checkbox into the tray menu: the eye icon on the panel
        // changes the same state as the menu item, and they must show the
        // same thing even when this menu is not currently open.
        SetEditingLayout(false);
    }

    /// The flag, the tray's tick and the panel's mode, together. Four callers
    /// need this — the tray item, the toolbar's Done and Hide buttons, and
    /// switching the widget off from the tray — and a mode left on behind a
    /// hidden widget is a ticked item nobody can act on.
    private void SetEditingLayout(bool editing)
    {
        _editingLayout = editing;
        _trayIcon!.EditingLayout = editing;
        if (_widgetWindow is not null) _widgetWindow.EditMode = editing;
        RefreshTrayMenuState();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        // SystemEvents invokes handlers on a separate system thread — both
        // the Dispatcher-sensitive render inside OnStoresChanged and the
        // WebView2 calls themselves in ClaudeWebSession expect the UI
        // thread, so we marshal the whole handler, not just its tail.
        Dispatcher.Invoke(() => FireAndForget(RefreshAllAsync));
    }

    private void OnStoresChanged()
    {
        // Changed from either store may arrive off the UI thread (see
        // OnPowerModeChanged) — WPF forbids touching the visual tree from
        // any thread other than the one that owns it.
        Dispatcher.Invoke(() =>
        {
            RenderWidget();
            UpdateTrayTooltip();
            RefreshTrayIcon();
            RefreshTrayMenuState();
            RenderTaskbarBand();
        });
    }

    /// <summary>Recomputes the tray's live number from fresh data — the
    /// same DialModel.All/TrayText.Metrics pair the tooltip uses, but takes
    /// from it only the metric chosen in "Tray shows" (TrayMetricKey).</summary>
    private void RefreshTrayIcon()
    {
        var account = TrayAccount();
        if (account is null) return;

        var data = _settings!.Load();
        var models = DialModel.All(account.Snapshot, data.ModelBucket, DateTimeOffset.Now);
        var metrics = TrayText.Metrics(models);

        _trayIcon!.SetIcon(TrayIconRenderer.Render(metrics[TrayIcon.MetricIndex(data.TrayMetricKey)].Value, SystemTheme.TaskbarKind));
    }

    /// <summary>Pulls the current state into the tray menu (checkboxes,
    /// "Model limit") — on startup and on every TrayIcon.MenuOpening/store
    /// Changed, since part of this state (PositionLocked, available model
    /// buckets) can change other than through the menu item itself.</summary>
    private void RefreshTrayMenuState()
    {
        var data = _settings!.Load();
        // The list of model buckets comes from the tray account: the
        // "Model limit" choice is one per widget, and offering something
        // the displayed account does not have would be a lie.
        var snapshot = TrayAccount()?.Snapshot;
        var availableBuckets = snapshot is null ? Array.Empty<string>() : ModelBuckets.Available(snapshot);

        // The same bucket resolution DialModel.All uses for the third
        // dial's title — "MODEL (X)" in the tray menu must name exactly the
        // model the panel/band is currently actually showing, not just
        // "what the user picked" (which can be Auto/null).
        var resolvedModelLabel = snapshot is not null && ModelBuckets.Resolve(data.ModelBucket, snapshot) is { } resolvedKey
            ? ModelBuckets.Label(resolvedKey)
            : null;
        var resolved = LayoutResolution.Resolve(data, _accounts.Count);

        _trayIcon!.SyncMenuState(new TrayMenuState(
            data.TrayMetricKey,
            availableBuckets,
            data.ModelBucket,
            data.WidgetVisible,
            _widgetWindow!.PositionLocked,
            data.TaskbarBandEnabled,
            data.BandPosition,
            resolvedModelLabel,
            data.Accounts,
            data.TrayAccountId,
            PanelViews.Current(resolved.Layout, resolved.Status),
            BandViews.Resolve(data.BandView, data.Accounts.Count),
            data.Theme ?? ThemeChoice.System));
    }

    private void OnTrayMetricSelected(string key)
    {
        var data = _settings!.Load();
        _settings.Save(data with { TrayMetricKey = key });
        RefreshTrayIcon();
        RefreshTrayMenuState();
    }

    private void OnModelBucketSelected(string? key)
    {
        var data = _settings!.Load();
        _settings.Save(data with { ModelBucket = key });

        // Unlike TrayMetricKey, ModelBucket is visible both on the widget
        // panel (the third dial) and in the tooltip, not only in the tray
        // icon.
        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();
    }

    private void OnShowOnDesktopToggled(bool visible)
    {
        var data = _settings!.Load();
        _settings.Save(data with { WidgetVisible = visible });

        // The same toggle as the eye icon on the panel — just entered from
        // the opposite side, so plain Show/Hide, without
        // PersistWidgetVisible below: the setting is already saved on the
        // line above.
        if (visible) _widgetWindow!.Show(); else _widgetWindow!.Hide();

        if (!visible) SetEditingLayout(false); else RefreshTrayMenuState();
    }

    private void OnLockPositionToggled(bool locked)
    {
        // The PositionLocked setter itself updates _root.PositionLocked
        // and saves the setting (see DesktopWidgetWindow.PositionLocked) —
        // there is nothing to duplicate here.
        _widgetWindow!.PositionLocked = locked;
        RefreshTrayMenuState();
    }

    private void OnTaskbarBandToggled(bool enabled)
    {
        var data = _settings!.Load();
        _settings.Save(data with { TaskbarBandEnabled = enabled });
        SetTaskbarBandVisible(enabled);
        RefreshTrayMenuState();
    }

    private void OnBandPositionSelected(string position)
    {
        var data = _settings!.Load();
        _settings.Save(data with { BandPosition = position });
        _bandWindow?.SetPosition(position);
        RefreshTrayMenuState();
    }

    private void OnBandViewSelected(BandView view)
    {
        _settings!.Save(_settings.Load() with { BandView = view });
        RenderTaskbarBand();
        RefreshTrayMenuState();
    }

    /// The one place the theme setting meets the system: everything that
    /// draws reads Theme.Current, so this is a resolve and an Apply.
    private void ApplyTheme()
    {
        var choice = _settings!.Load().Theme;
        Theme.Apply(ThemeChoices.Resolve(choice, SystemTheme.AppsKind));
    }

    private void OnThemeSelected(ThemeChoice choice)
    {
        _settings!.Save(_settings.Load() with { Theme = choice });
        ApplyTheme();
        RefreshTrayMenuState();
    }

    /// Already on the dispatcher — SystemTheme hops before raising. The
    /// taskbar-mode consumers (icon digits, band text) redraw here too; the
    /// theme itself only changes when the setting is System.
    private void OnSystemThemeChanged()
    {
        ApplyTheme();
        RefreshTrayIcon();
        RenderTaskbarBand();
    }

    /// A named view is a preset over the same two settings the toolbar edits;
    /// the model-dial and plan-line switches ride along unchanged.
    private void OnPanelViewSelected(PanelView view)
    {
        _settings!.Save(LayoutResolution.Apply(_settings.Load(), view));
        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    /// <summary>Turns the band on/off. The window is created lazily on the
    /// first enable and survives subsequent disables (Detach only hides it
    /// and clears the owner) — there is no need to recreate
    /// TaskbarBandWindow on every checkbox toggle, Dock/Detach are already
    /// idempotent.</summary>
    private void SetTaskbarBandVisible(bool visible)
    {
        if (visible)
        {
            if (_bandWindow is null)
            {
                _bandWindow = new TaskbarBandWindow();
                _bandWindow.Lost += OnTaskbarBandLost;
            }
            _bandWindow.SetPosition(_settings!.Load().BandPosition);
            // Render BEFORE Dock: it immediately calls Reposition(), which
            // needs the current content width, not the width from the
            // previous showing (or zero on the very first one).
            RenderTaskbarBand();
            _bandWindow.Dock();
        }
        else
        {
            _bandWindow?.Detach();
        }
    }

    /// <summary>TaskbarBandWindow.Lost: the band's native HWND disappeared
    /// not through our own Detach()/Close() — an owned window does not
    /// cascade-die along with the taskbar (unlike the earlier WS_CHILD
    /// variant), so this became a defensive backstop for an unforeseen
    /// case rather than the main recovery path after explorer.exe restarts
    /// (that now fixes itself on the nearest tick — see
    /// TaskbarBandWindow.RepositionCore). WPF does not let you show a
    /// Window again once its native handle has disappeared this way — we
    /// drop the old instance (its HWND is already invalid, there is
    /// nothing to close) and, if the band is still supposed to be enabled,
    /// create a new one, just like on the normal first enable.</summary>
    private void OnTaskbarBandLost()
    {
        // Close the WPF shell of the dead instance: its native HWND is
        // already invalid, but the Window object itself, without Close(),
        // stays alive in the application's window list — live debugging
        // (2026-08-06) found several such orphaned hidden windows in a
        // single run.
        var dead = _bandWindow;
        _bandWindow = null;
        try { dead?.Close(); }
        catch (InvalidOperationException) { /* already closed by WPF itself — and that's fine */ }

        if (_settings!.Load().TaskbarBandEnabled) SetTaskbarBandVisible(true);
    }

    /// <summary>Fresh data for the band — one BandText entry per account, from
    /// the same AccountRow list the panel draws. Harmless to call when the band
    /// is switched off or not created yet.</summary>
    private void RenderTaskbarBand()
    {
        if (_bandWindow is null) return;

        var data = _settings!.Load();
        var rows = AccountRow.ForAll(
            data.Accounts, SnapshotsById(), data.ModelBucket, DateTimeOffset.Now);

        // Three metrics of the tray account, or one group per account — the
        // same rows either way, so the band and the panel never disagree.
        var trayId = TrayAccount()?.Profile.Id;
        var trayRow = rows.FirstOrDefault(row => row.AccountId == trayId) ?? rows.FirstOrDefault();
        var entries = BandViews.Resolve(data.BandView, data.Accounts.Count) == BandView.Metrics && trayRow is not null
            ? BandText.MetricEntries(trayRow)
            : BandText.Entries(rows);
        _bandWindow.Render(entries, SystemTheme.TaskbarKind);
    }

    private void RenderWidget()
    {
        var account = TrayAccount();
        if (account is null) return;

        var data = _settings!.Load();
        var rows = AccountRow.ForAll(
            data.Accounts, SnapshotsById(), data.ModelBucket, DateTimeOffset.Now);

        _widgetWindow!.Render(
            rows,
            account.Store.CurrentState,
            _statusStore!.Status,
            account.Store.RetryPausedUntil);
    }

    private void UpdateTrayTooltip()
    {
        var account = TrayAccount();
        if (account is null) return;

        if (_webViewFailing.TryGetValue(account.Profile.Id, out var failing) && failing)
        {
            // The simplest honest outward signal for a failure that would
            // otherwise drown in FireAndForget/Debug output — not a modal
            // MessageBox (that would block the Dispatcher right during
            // startup, while the cookie check has not finished yet) and
            // not a one-off message (that would then get washed away by
            // the very next Changed from the store, e.g. when
            // RefreshWidget is called by the timer) — but a tray tooltip,
            // which sticks around for exactly as long as the problem
            // persists. The widget panel, meanwhile, will show
            // Failed(Network(...)) through UsageStore's normal path anyway,
            // as soon as FetchUsageAsync inside LoadAsync runs into the
            // same error (see the comment in RefreshAllAsync).
            _trayIcon!.SetTooltip("Claude Usage Widget — WebView2 error, see widget for details");
            return;
        }

        var now = DateTimeOffset.Now;
        var state = account.Store.CurrentState;
        var data = _settings!.Load();
        var models = DialModel.All(account.Snapshot, data.ModelBucket, now);
        var metrics = TrayText.Metrics(models);
        var statusLine = StatusLine.Text(state, now, account.Store.RetryPausedUntil);

        // "5H 42% · 7D 18% · FAB 8%" — the same " · " separator as in
        // the status line under the dials on the panel; the status (if any)
        // goes on a separate line rather than the same " · ", so it does
        // not blend with the numbers at a quick glance at the tooltip.
        var metricsText = metrics.Count == 0
            ? "Claude Usage Widget"
            : "Claude Usage Widget — " + string.Join(" · ", metrics.Select(m => $"{m.Label} {m.Value}"));

        _trayIcon!.SetTooltip(statusLine is null ? metricsText : $"{metricsText}\n{statusLine}");
    }

    /// The pause after a 429 is per-account: a shared one would freeze
    /// polling for the other three because of one that hit the limit.
    private UsageRetryState? LoadRetryState(string accountId)
    {
        var account = _settings!.Load().Account(accountId);
        return account?.RetryPausedUntil is { } until
            ? new UsageRetryState(until, account.ConsecutiveRateLimits)
            : null;
    }

    private void SaveRetryState(string accountId, UsageRetryState? state)
    {
        _settings!.Save(_settings.Load().WithAccount(accountId, a => a with
        {
            RetryPausedUntil = state?.Until,
            ConsecutiveRateLimits = state?.ConsecutiveRateLimits ?? 0,
        }));
    }

    /// Builds the account list from the settings. On the very first run
    /// the file does not exist at all, the migration does not fire, and
    /// the list is empty — in that case we create one account so the
    /// widget has something to show and somewhere to sign in.
    private void BuildAccounts()
    {
        var data = _settings!.Load();
        if (data.Accounts.Count == 0)
        {
            data = data with
            {
                Accounts = [new AccountProfile(SettingsMigration.LegacyAccountId, "Claude", null, null, 0)],
                TrayAccountId = SettingsMigration.LegacyAccountId,
            };
            _settings.Save(data);
        }

        _accounts = data.Accounts.Select(CreateAccountRuntime).ToList();
    }

    private AccountRuntime CreateAccountRuntime(AccountProfile profile)
    {
        // The migrated (i.e. first) account stays on the IMPLICIT WebView2
        // profile — that is where the sign-in made before multi-account
        // support lives, and that profile is not addressed by name. The
        // other accounts get a named profile keyed by their own id.
        // Details and the reasoning are in the comment on
        // ClaudeWebSession._profileName.
        var profileName = profile.Id == SettingsMigration.LegacyAccountId ? null : profile.Id;
        var session = new ClaudeWebSession(profileName, profile.Id, profile.DisplayName, _settings!);
        session.SignedIn += () => OnSignedIn(profile.Id);

        var store = new UsageStore(
            fetch: session.FetchUsageAsync,
            hasCredentials: () => _hasSessionCookie.TryGetValue(profile.Id, out var has) && has,
            now: () => DateTimeOffset.Now,
            loadRetryState: () => LoadRetryState(profile.Id),
            saveRetryState: state => SaveRetryState(profile.Id, state));
        store.Changed += OnStoresChanged;

        return new AccountRuntime(profile, session, store);
    }

    /// The account the tray icon, its tooltip and "Refresh now" are bound
    /// to. Null only when there are no accounts at all.
    private AccountRuntime? TrayAccount()
    {
        var id = _settings!.Load().TrayAccountId;
        return _accounts.FirstOrDefault(a => a.Profile.Id == id) ?? _accounts.FirstOrDefault();
    }

    private IReadOnlyDictionary<string, UsageSnapshot?> SnapshotsById() =>
        _accounts.ToDictionary(a => a.Profile.Id, a => a.Snapshot);

    private void OnLayoutSelected(WidgetLayout layout)
    {
        var data = _settings!.Load();
        // Both settings are written back on every save, so a file that predated
        // either stops being ambiguous after the first edit.
        var current = LayoutResolution.Resolve(data, _accounts.Count);
        _settings.Save(data with
        {
            Layout = WidgetLayout.Sanitize(layout, current.Status, current.ModelDial),
            StatusMode = current.Status,
            ModelDial = current.ModelDial,
        });

        // Changing the layout changes both the panel's size and its grid —
        // the window is rebuilt entirely, not merely repainted.
        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    /// The status is one decision. Saving the mode and re-sanitizing the layout
    /// with it is what adds or removes the cell — there is no second switch that
    /// could disagree.
    private void OnStatusModeSelected(StatusMode mode)
    {
        var data = _settings!.Load();
        // Resolved, not `data.Layout`: a file that never saved a layout is
        // drawing the view for its account count, and that is the layout the
        // switch must apply to — not the default the file would fall back to.
        var current = LayoutResolution.Resolve(data, _accounts.Count);
        _settings.Save(data with
        {
            StatusMode = mode,
            ModelDial = current.ModelDial,
            Layout = WidgetLayout.Sanitize(current.Layout, mode, current.ModelDial),
        });

        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    /// The model dial is the same one decision in one place: the setting is
    /// saved and the layout re-sanitized around it, so the cell cannot disagree
    /// with the switch. Only the CELL goes — the tray tooltip and the taskbar
    /// band read DialModel.All and keep reporting the percentage.
    private void OnModelDialSelected(ModelDial dial)
    {
        var data = _settings!.Load();
        var current = LayoutResolution.Resolve(data, _accounts.Count);
        _settings.Save(data with
        {
            ModelDial = dial,
            StatusMode = current.Status,
            Layout = WidgetLayout.Sanitize(current.Layout, current.Status, dial),
        });

        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    /// The plan line, on the same one-place path as the two above minus the
    /// Sanitize: the plan is a line under the account name, never a cell, so
    /// nothing in `Order` follows it and there is no second switch to keep in
    /// agreement. RebuildLayout is still needed — the panel is SIZED for the
    /// line, so switching it changes the geometry and not only the paint.
    private void OnPlanLineSelected(PlanLine line)
    {
        _settings!.Save(_settings.Load() with { PlanLine = line });

        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    private void OnAccountSelected(string accountId)
    {
        _settings!.Save(_settings.Load() with { TrayAccountId = accountId });
        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();
    }

    private void OnAccountAddRequested()
    {
        var data = _settings!.Load();
        if (data.Accounts.Count >= AccountLimits.Max) return;

        // Guid — within ProfileName's alphabet and guaranteed not to
        // collide with the legacy id "default", which must stay on the
        // implicit profile.
        var profile = new AccountProfile(Guid.NewGuid().ToString(), "New account", null, null, 0);
        _settings.Save(data with { Accounts = [.. data.Accounts, profile] });

        var runtime = CreateAccountRuntime(profile);
        _accounts = [.. _accounts, runtime];
        // A file that never chose a view follows the account count, so the
        // second account can turn the classic square into rows.
        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
        FireAndForget(() => runtime.Session.OpenLoginWindowAsync());
    }

    private void OnAccountRenameRequested(string accountId)
    {
        var data = _settings!.Load();
        var account = data.Account(accountId);
        if (account is null) return;

        var name = RenameWindow.Ask(account.DisplayName);
        if (string.IsNullOrWhiteSpace(name)) return;

        _settings.Save(data.WithAccount(accountId, a => a with { DisplayName = name.Trim() }));
        ReloadAccountProfiles();
        RenderWidget();
        RefreshTrayMenuState();
    }

    private async Task OnAccountRemoveRequestedAsync(string accountId)
    {
        var runtime = _accounts.FirstOrDefault(a => a.Profile.Id == accountId);
        if (runtime is null) return;

        // Clean up BEFORE removing from settings: once removed, there is
        // nothing left to know which profile to wipe, and the next account
        // with the same id would inherit someone else's session.
        try
        {
            await runtime.Session.ClearBrowsingDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clearing browsing data for '{accountId}' failed: {ex}");
        }

        var data = _settings!.Load();
        _settings.Save(data with
        {
            Accounts = data.Accounts.Where(a => a.Id != accountId).ToList(),
            TrayAccountId = data.TrayAccountId == accountId ? null : data.TrayAccountId,
        });
        _accounts = _accounts.Where(a => a.Profile.Id != accountId).ToList();

        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();
    }

    /// Pulls changed profile fields (currently only DisplayName) into
    /// already live AccountRuntime instances, without recreating sessions
    /// or losing loaded snapshots.
    private void ReloadAccountProfiles()
    {
        var byId = _settings!.Load().Accounts.ToDictionary(a => a.Id);
        _accounts = _accounts
            .Where(a => byId.ContainsKey(a.Profile.Id))
            .Select(a => a with { Profile = byId[a.Profile.Id] })
            .ToList();
    }

    /// A "fire and forget" wrapper for click/timer handlers: without it,
    /// an unhandled exception inside an async task that nobody awaits is
    /// silently lost (at best, it ends up in
    /// TaskScheduler.UnobservedTaskException at the next garbage
    /// collection). Here it at least reaches Debug output.
    private static async void FireAndForget(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled error in fire-and-forget task: {ex}");
            WidgetLog.Write("-", "unhandled", $"fire-and-forget {ex.GetType().Name}: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A static event — not unsubscribing means keeping App alive in
        // SystemEvents' subscribers longer than needed.
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemTheme.Stop();
        _refreshTimer?.Stop();

        // NotifyIcon visually outlives the process until the next mouse
        // movement unless hidden explicitly — Dispose removes the icon
        // immediately.
        _trayIcon?.Dispose();
        _trayIcon = null;

        _widgetWindow?.Close();
        _widgetWindow = null;

        // Detach before Close: our window may still be a child of
        // Shell_TrayWnd (another process) — first detach it carefully,
        // then close it as a regular WPF window.
        _bandWindow?.Detach();
        _bandWindow?.Close();
        _bandWindow = null;

        base.OnExit(e);
    }
}

