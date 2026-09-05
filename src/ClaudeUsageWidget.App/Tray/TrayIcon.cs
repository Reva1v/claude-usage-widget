using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClaudeUsageWidget.App.Windows;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// Everything the tray menu needs to reflect current app state, gathered by
/// App.xaml.cs and handed to <see cref="TrayIcon.SyncMenuState"/> — on
/// startup and again every time the menu is about to open (<see
/// cref="TrayIcon.MenuOpening"/>), so checkmarks stay correct even when the
/// underlying setting changed from somewhere other than this menu (e.g. the
/// widget's own lock toggle or its eye button).
/// </summary>
public sealed record TrayMenuState(
    string TrayMetricKey,
    IReadOnlyList<string> AvailableModelBuckets,
    string? SelectedModelBucket,
    bool ShowOnDesktop,
    bool PositionLocked,
    bool TaskbarBandEnabled,
    string BandPosition,
    string? ResolvedModelLabel,
    IReadOnlyList<AccountProfile> Accounts,
    string? TrayAccountId,
    PanelView? PanelView,
    BandView BandView,
    ThemeChoice Theme);

/// <summary>
/// The application's icon in the system tray and its context menu.
/// </summary>
///
/// WPF has no tray of its own — we wrap WinForms <see cref="NotifyIcon"/>,
/// the way menu-bar-app ports usually do on Windows.
public sealed class TrayIcon : IDisposable
{
    private const string RepoUrl = "https://github.com/Reva1v/claude-usage-widget";
    private const string IssuesUrl = RepoUrl + "/issues";

    private readonly NotifyIcon _notifyIcon;
    private Icon? _currentIcon;
    private bool _disposed;

    private ToolStripMenuItem _trayShowsSessionItem = null!;
    private ToolStripMenuItem _trayShowsWeekItem = null!;
    private ToolStripMenuItem _trayShowsModelItem = null!;
    private ToolStripMenuItem _modelLimitMenu = null!;
    private ToolStripMenuItem _showOnDesktopItem = null!;
    private ToolStripMenuItem _taskbarBandItem = null!;
    private ToolStripMenuItem _bandPositionLeftItem = null!;
    private ToolStripMenuItem _bandPositionTrayItem = null!;
    private ToolStripMenuItem _lockPositionItem = null!;
    private ToolStripMenuItem _launchAtLoginItem = null!;
    private ToolStripMenuItem _accountsMenu = null!;
    private ToolStripMenuItem _layoutMenu = null!;
    private ToolStripMenuItem _bandViewMetricsItem = null!;
    private ToolStripMenuItem _bandViewAccountsItem = null!;
    private ToolStripMenuItem _themeSystemItem = null!;
    private ToolStripMenuItem _themeDarkItem = null!;
    private ToolStripMenuItem _themeLightItem = null!;

    /// <summary>The "Refresh now" menu item.</summary>
    public event Action? RefreshRequested;

    /// <summary>The "Sign in to Claude.ai…" menu item.</summary>
    public event Action? SignInRequested;

    /// <summary>The "Quit Claude Usage Widget" menu item.</summary>
    public event Action? QuitRequested;

    /// <summary>"Tray shows" — the new TrayMetricKey value ("five_hour"/"seven_day"/"model").</summary>
    public event Action<string>? TrayMetricSelected;

    /// <summary>"Model limit" — the selected bucket key, or null for "Auto".</summary>
    public event Action<string?>? ModelBucketSelected;

    /// <summary>"Show on desktop" — the new desired state.</summary>
    public event Action<bool>? ShowOnDesktopToggled;

    /// <summary>"Taskbar band" — the new desired state.</summary>
    public event Action<bool>? TaskbarBandToggled;

    /// <summary>"Band position" — the new value ("tray"/"left").</summary>
    public event Action<string>? BandPositionSelected;

    /// <summary>"Lock position" — the new desired state.</summary>
    public event Action<bool>? LockPositionToggled;

    /// <summary>Accounts — the account selected for the tray icon.</summary>
    public event Action<string>? AccountSelected;

    /// <summary>Accounts — "Add account…".</summary>
    public event Action? AccountAddRequested;

    /// <summary>Accounts — "Rename…" for this id.</summary>
    public event Action<string>? AccountRenameRequested;

    /// <summary>Accounts — "Sign out and remove" for this id.</summary>
    public event Action<string>? AccountRemoveRequested;

    /// Raised by the Layout submenu's edit item. The App owns the flag; the
    /// tray only asks for it to be flipped.
    public event Action? EditLayoutToggled;

    /// Layout — one of the two named views was picked.
    public event Action<PanelView>? PanelViewSelected;

    /// "Band shows" — what the taskbar band draws.
    public event Action<BandView>? BandViewSelected;

    /// Theme — System / Dark / Light.
    public event Action<ThemeChoice>? ThemeSelected;

    /// What the tick beside `Edit layout…` should show. Set before the menu is
    /// opened, like the rest of the Layout submenu's state.
    public bool EditingLayout { get; set; }

    /// <summary>
    /// The menu is about to open — the moment to pull in fresh state
    /// (SettingsStore, the widget's PositionLocked, available model buckets) via
    /// <see cref="SyncMenuState"/>, without waiting for the store's next Changed.
    /// </summary>
    public event Action? MenuOpening;

    /// <summary>
    /// The open menu — later tasks (org picker) append their own items to it,
    /// without recreating TrayIcon wholesale.
    /// </summary>
    public ContextMenuStrip Menu { get; }

    public TrayIcon()
    {
        Menu = BuildMenu();
        Menu.Opening += (_, _) => MenuOpening?.Invoke();

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = Menu,
            Visible = true,
        };

        SetIcon(TrayIconRenderer.Render(null, SystemTheme.TaskbarKind));
        SetTooltip("Claude Usage Widget");
    }

    public void SetIcon(Icon icon)
    {
        var previous = _currentIcon;
        _notifyIcon.Icon = icon;
        _currentIcon = icon;

        if (previous is null) return;

        // NotifyIcon.Icon = ... does not take ownership of the handle; an icon
        // created from a HICON via Icon.FromHandle requires a manual
        // DestroyIcon — otherwise every replacement (the live digit,
        // TrayIconRenderer) leaks into the process's GDI quota until it's exhausted.
        var handle = previous.Handle;
        previous.Dispose();
        NativeMethods.DestroyIcon(handle);
    }

    public void SetTooltip(string text)
    {
        // NOTIFYICONDATA.szTip holds 128 characters including the trailing NUL;
        // WinForms throws ArgumentOutOfRangeException at 128+, hence 127.
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>Syncs the dynamic part of the menu (checkboxes, "Model
    /// limit", "Tray shows") with the current settings/widget state. Call it
    /// on startup and on every <see cref="MenuOpening"/> — the source of truth
    /// lives in App.xaml.cs (SettingsStore/DesktopWidgetWindow), not here.</summary>
    public void SyncMenuState(TrayMenuState state)
    {
        // MetricIndex, not a pointwise comparison with "five_hour": the same
        // function App.xaml.cs.RefreshTrayIcon uses for the digit itself —
        // an unrecognized/corrupt TrayMetricKey (e.g. from a manually edited
        // settings.json) must read in the menu as SESSION by exactly the same
        // rule that makes the icon draw SESSION in that case, rather than
        // leaving all three checkboxes blank.
        SyncAccountsMenu(state.Accounts, state.TrayAccountId);
        SyncLayoutMenu(state.ShowOnDesktop, state.PanelView);
        _bandViewMetricsItem.Checked = state.BandView == BandView.Metrics;
        _bandViewAccountsItem.Checked = state.BandView == BandView.Accounts;
        _themeSystemItem.Checked = state.Theme == ThemeChoice.System;
        _themeDarkItem.Checked = state.Theme == ThemeChoice.Dark;
        _themeLightItem.Checked = state.Theme == ThemeChoice.Light;

        var metricIndex = MetricIndex(state.TrayMetricKey);
        _trayShowsSessionItem.Checked = metricIndex == 0;
        _trayShowsWeekItem.Checked = metricIndex == 1;
        _trayShowsModelItem.Checked = metricIndex == 2;

        // "MODEL (Fable)" once we know which bucket actually resolved (same
        // resolution DialModel.All uses for the third dial's own title) —
        // plain "MODEL" before any snapshot has loaded. ModelBuckets.Label
        // comes back SHOUTY ("FABLE"); title-case it for the menu specifically
        // — the widget's own dial keeps the shouty version, this is purely a
        // menu-text nicety.
        _trayShowsModelItem.Text = string.IsNullOrEmpty(state.ResolvedModelLabel)
            ? "MODEL"
            : $"MODEL ({TitleCase(state.ResolvedModelLabel)})";

        // ModelBucketPicker.swift:145-163 — visible only when there's something
        // to choose from; with 0/1 buckets the choice is meaningless (Resolve
        // will take the only available one anyway).
        _modelLimitMenu.Visible = state.AvailableModelBuckets.Count > 1;
        _modelLimitMenu.DropDownItems.Clear();

        var autoItem = new ToolStripMenuItem("Auto", null, (_, _) => ModelBucketSelected?.Invoke(null))
        {
            Checked = string.IsNullOrEmpty(state.SelectedModelBucket),
        };
        _modelLimitMenu.DropDownItems.Add(autoItem);
        _modelLimitMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var key in state.AvailableModelBuckets)
        {
            var capturedKey = key; // don't rely on loop-variable capture semantics — a local copy just in case
            _modelLimitMenu.DropDownItems.Add(new ToolStripMenuItem(
                ModelBuckets.Label(capturedKey), null, (_, _) => ModelBucketSelected?.Invoke(capturedKey))
            {
                Checked = state.SelectedModelBucket == capturedKey,
            });
        }

        _showOnDesktopItem.Checked = state.ShowOnDesktop;
        _taskbarBandItem.Checked = state.TaskbarBandEnabled;
        _bandPositionLeftItem.Checked = state.BandPosition == "left";
        _bandPositionTrayItem.Checked = state.BandPosition != "left"; // "tray" and any unrecognized value default here, same fallback shape as MetricIndex
        _lockPositionItem.Checked = state.PositionLocked;

        // Launch at login doesn't come through TrayMenuState: it's not a
        // JSON setting but the registry itself — the source of truth is already
        // at hand.
        _launchAtLoginItem.Checked = Autostart.IsEnabled();
    }

    /// "FABLE" -> "Fable" — ModelBuckets.Label is deliberately SHOUTY for the
    /// widget's own dial (Theme.LabelWeight etc. render everything caps
    /// anyway); the tray menu is normal UI chrome where a shouty parenthetical
    /// would look out of place next to "Auto"/"Left corner"/etc.
    private static string TitleCase(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>SESSION/WEEK/MODEL → index into DialModel.All, which always
    /// returns exactly these three dials in this order. Public and shared
    /// with App.xaml.cs.RefreshTrayIcon: both sides must agree on what an
    /// unrecognized/corrupt TrayMetricKey means (SESSION), otherwise the menu
    /// checkboxes and the icon's digit could disagree.</summary>
    public static int MetricIndex(string trayMetricKey) => trayMetricKey switch
    {
        "seven_day" => 1,
        "model" => 2,
        _ => 0, // "five_hour" and any unrecognized value — session by default
    };

    /// Rebuilt from scratch on every menu opening rather than mutated:
    /// accounts are added and removed from this same submenu, and rebuilding
    /// is what keeps the checkmark, the list, and the settings from drifting
    /// apart.
    private void SyncAccountsMenu(IReadOnlyList<AccountProfile> accounts, string? trayAccountId)
    {
        foreach (var item in _accountsMenu.DropDownItems.OfType<ToolStripMenuItem>())
            item.DropDown.Closing -= CancelCloseOnItemClick;
        _accountsMenu.DropDownItems.Clear();

        // The accounts submenu is meaningless while there's only one account and
        // a second can't be added; but "add" is always needed, so we hide only
        // the list itself, not the whole item.
        foreach (var account in accounts)
        {
            var item = new ToolStripMenuItem(
                account.DisplayName, null, (_, _) => AccountSelected?.Invoke(account.Id))
            {
                Checked = account.Id == trayAccountId,
            };
            var id = account.Id;
            item.DropDownItems.Add("Rename…", null, (_, _) => AccountRenameRequested?.Invoke(id));
            item.DropDownItems.Add("Sign out and remove", null, (_, _) => AccountRemoveRequested?.Invoke(id));
            item.DropDown.Closing += CancelCloseOnItemClick;
            _accountsMenu.DropDownItems.Add(item);
        }

        if (accounts.Count > 0) _accountsMenu.DropDownItems.Add(new ToolStripSeparator());

        // Disabled rather than hidden: a missing item reads as a bug, one grayed
        // out with a tooltip reads as a limit.
        var atLimit = accounts.Count >= AccountLimits.Max;
        _accountsMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Add account…", null, (_, _) => AccountAddRequested?.Invoke())
        {
            Enabled = !atLimit,
            ToolTipText = atLimit ? $"The widget shows at most {AccountLimits.Max} accounts." : null,
        });
    }

    /// The two named views, then the editor. Every flow, the name placement
    /// and the status live on the panel's own toolbar — one editing surface
    /// rather than two that have to keep agreeing with each other; the views
    /// here are presets that write the same settings the toolbar edits, and a
    /// hand-arranged panel ticks neither.
    private void SyncLayoutMenu(bool widgetVisible, PanelView? current)
    {
        _layoutMenu.DropDownItems.Clear();
        _layoutMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Classic", null, (_, _) => PanelViewSelected?.Invoke(PanelView.Classic))
        {
            Checked = current == PanelView.Classic,
            ToolTipText = "A 2x2 grid of dials with the service status as a dial — the panel as it was before accounts.",
        });
        _layoutMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Account rows", null, (_, _) => PanelViewSelected?.Invoke(PanelView.Accounts))
        {
            Checked = current == PanelView.Accounts,
            ToolTipText = "One row per account with its name, and the service status as a line under the rows.",
        });
        _layoutMenu.DropDownItems.Add(new ToolStripSeparator());
        _layoutMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Edit layout…", null, (_, _) => EditLayoutToggled?.Invoke())
        {
            Checked = EditingLayout,
            // Disabled while the widget is hidden: a ticked item and an
            // invisible mode is the trap this closes.
            Enabled = widgetVisible,
            ToolTipText = widgetVisible
                ? "Toolbar on the panel: flows, name, status, lock, hide. Drag a cell onto another to swap them."
                : "Show the widget on the desktop first.",
        });
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Order and separators — like the macOS menu (ClaudeUsageWidgetApp.swift:33-58):
        // GitHub/issues first, then refresh/sign-in, then toggles, then quit.
        // "Check for Updates…" isn't ported (no Sparkle equivalent in this
        // task); "Tray shows" is an item with no Swift counterpart, specific to
        // the Windows tray: there the menu bar always draws all three numbers at
        // once, here the icon only fits one.
        menu.Items.Add($"Claude Usage Widget v{CoreInfo.Version} — GitHub", null, (_, _) => OpenUrl(RepoUrl));
        menu.Items.Add("Report an Issue", null, (_, _) => OpenUrl(IssuesUrl));
        menu.Items.Add(new ToolStripSeparator());
        // Refresh now — an exception to the "menu doesn't close on click" rule
        // below: it's an action, not a toggle, no reason to keep the menu open
        // after it. The explicit Close() arrives with reason CloseCalled, which
        // CancelCloseOnItemClick lets through.
        menu.Items.Add("Refresh now", null, (_, _) =>
        {
            RefreshRequested?.Invoke();
            menu.Close();
        });
        menu.Items.Add("Sign in to Claude.ai…", null, (_, _) => SignInRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());

        // Shorter labels ("5H"/"7D" instead of "SESSION"/"WEEK") — the same
        // TrayMetricKey keys under the hood ("five_hour"/"seven_day"/"model"),
        // only what's visible in the menu changes. "MODEL" here is a placeholder
        // at startup (the snapshot hasn't loaded yet); SyncMenuState appends the
        // resolved bucket in parentheses as soon as it's known ("MODEL (Fable)").
        var trayShowsMenu = new ToolStripMenuItem("Tray shows");
        _trayShowsSessionItem = new ToolStripMenuItem("5H", null, (_, _) => TrayMetricSelected?.Invoke("five_hour"));
        _trayShowsWeekItem = new ToolStripMenuItem("7D", null, (_, _) => TrayMetricSelected?.Invoke("seven_day"));
        _trayShowsModelItem = new ToolStripMenuItem("MODEL", null, (_, _) => TrayMetricSelected?.Invoke("model"));
        trayShowsMenu.DropDownItems.Add(_trayShowsSessionItem);
        trayShowsMenu.DropDownItems.Add(_trayShowsWeekItem);
        trayShowsMenu.DropDownItems.Add(_trayShowsModelItem);
        menu.Items.Add(trayShowsMenu);

        // DropDownItems are refilled in SyncMenuState — at startup there are no
        // available buckets yet (the snapshot hasn't loaded), so this starts as
        // an empty submenu, hidden until the first SyncMenuState.
        _modelLimitMenu = new ToolStripMenuItem("Model limit") { Visible = false };
        menu.Items.Add(_modelLimitMenu);

        // Which account the tray icon describes. The alternative would be four
        // icons, but Windows hides them in the overflow at its own discretion.
        _accountsMenu = new ToolStripMenuItem("Accounts");
        menu.Items.Add(_accountsMenu);

        // One item: the panel's toolbar is the editing surface, and this is the
        // way into it.
        _layoutMenu = new ToolStripMenuItem("Layout");
        menu.Items.Add(_layoutMenu);

        // A look setting, like Layout, so it sits beside it. System follows
        // Windows' "Default app mode" live.
        var themeMenu = new ToolStripMenuItem("Theme");
        _themeSystemItem = new ToolStripMenuItem("System", null, (_, _) => ThemeSelected?.Invoke(ThemeChoice.System))
        {
            ToolTipText = "Follow Windows' app theme.",
        };
        _themeDarkItem = new ToolStripMenuItem("Dark", null, (_, _) => ThemeSelected?.Invoke(ThemeChoice.Dark));
        _themeLightItem = new ToolStripMenuItem("Light", null, (_, _) => ThemeSelected?.Invoke(ThemeChoice.Light));
        themeMenu.DropDownItems.Add(_themeSystemItem);
        themeMenu.DropDownItems.Add(_themeDarkItem);
        themeMenu.DropDownItems.Add(_themeLightItem);
        menu.Items.Add(themeMenu);

        _showOnDesktopItem = new ToolStripMenuItem("Show on desktop");
        _showOnDesktopItem.Click += (_, _) => ShowOnDesktopToggled?.Invoke(!_showOnDesktopItem.Checked);
        menu.Items.Add(_showOnDesktopItem);

        _taskbarBandItem = new ToolStripMenuItem("Taskbar band");
        _taskbarBandItem.Click += (_, _) => TaskbarBandToggled?.Invoke(!_taskbarBandItem.Checked);
        menu.Items.Add(_taskbarBandItem);

        // "Near tray"/"Left corner" — sets BandPosition independently of
        // whether the band itself is currently enabled (the same principle as
        // "Tray shows": the preference is kept even while there's nothing to
        // look at).
        var bandPositionMenu = new ToolStripMenuItem("Band position");
        _bandPositionTrayItem = new ToolStripMenuItem("Near tray", null, (_, _) => BandPositionSelected?.Invoke("tray"));
        _bandPositionLeftItem = new ToolStripMenuItem("Left corner", null, (_, _) => BandPositionSelected?.Invoke("left"));
        bandPositionMenu.DropDownItems.Add(_bandPositionTrayItem);
        bandPositionMenu.DropDownItems.Add(_bandPositionLeftItem);
        menu.Items.Add(bandPositionMenu);

        // What the band draws, kept like the position: a preference that
        // survives the band being switched off.
        var bandViewMenu = new ToolStripMenuItem("Band shows");
        _bandViewMetricsItem = new ToolStripMenuItem("Three metrics", null, (_, _) => BandViewSelected?.Invoke(BandView.Metrics))
        {
            ToolTipText = "5H, 7D and the model for the tray account.",
        };
        _bandViewAccountsItem = new ToolStripMenuItem("All accounts", null, (_, _) => BandViewSelected?.Invoke(BandView.Accounts))
        {
            ToolTipText = "Every account's name over its 5H figure and reset time.",
        };
        bandViewMenu.DropDownItems.Add(_bandViewMetricsItem);
        bandViewMenu.DropDownItems.Add(_bandViewAccountsItem);
        menu.Items.Add(bandViewMenu);

        _lockPositionItem = new ToolStripMenuItem("Lock position");
        _lockPositionItem.Click += (_, _) => LockPositionToggled?.Invoke(!_lockPositionItem.Checked);
        menu.Items.Add(_lockPositionItem);

        // Launch at login is self-contained (a port of LaunchAtLoginToggle,
        // ClaudeUsageWidgetApp.swift:168-188): unlike the other checkboxes, it
        // has no corresponding field in WidgetSettingsData — the source of truth
        // is the registry itself (Autostart), so there's no need for a round
        // trip through App/SettingsStore, and rolling back on failure is easier
        // to do in place.
        _launchAtLoginItem = new ToolStripMenuItem("Launch at login");
        _launchAtLoginItem.Click += OnLaunchAtLoginClicked;
        menu.Items.Add(_launchAtLoginItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Claude Usage Widget", null, (_, _) => QuitRequested?.Invoke());

        // The menu doesn't close after clicking an item — only explicitly (a
        // click outside the menu, Esc, loss of focus): almost all items here are
        // toggles, and the user clicks several during a single opening, while
        // WinForms collapses the menu on the very first one by default. The
        // subscription is needed on both the root and every submenu: clicking a
        // nested item closes the whole chain of DropDowns, and each link asks
        // about closing separately. One shared loop at the end, rather than a
        // line per submenu — a new submenu picks it up automatically, with no
        // risk of forgetting the subscription.
        menu.Closing += CancelCloseOnItemClick;
        foreach (var submenu in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (submenu.HasDropDownItems || submenu == _modelLimitMenu)
                submenu.DropDown.Closing += CancelCloseOnItemClick;
        }

        return menu;
    }

    /// ItemClicked is the only close reason we suppress; it doesn't delay
    /// Quit (the app tears down the whole menu via Dispose), and the keyboard
    /// and a click outside the menu keep closing it as usual.
    private static void CancelCloseOnItemClick(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
    }

    private void OnLaunchAtLoginClicked(object? sender, EventArgs e)
    {
        var desired = !_launchAtLoginItem.Checked;
        try
        {
            Autostart.SetEnabled(desired);
            _launchAtLoginItem.Checked = desired;
        }
        catch (Exception ex)
        {
            // A port of LaunchAtLoginToggle's catch semantics (swift:174-185): on
            // failure the checkbox doesn't just roll back to the old value, it
            // rereads the actual registry state — SetEnabled could have failed
            // midway (e.g. SetValue after a successful OpenSubKey), and the old
            // "was" doesn't necessarily match what's there now.
            _launchAtLoginItem.Checked = Autostart.IsEnabled();
            Debug.WriteLine($"Failed to change launch-at-login: {ex}");
        }
    }

    private static void OpenUrl(string url)
    {
        // "-" for the account: the tray's links (repository, issues) are about
        // the widget itself, not about whichever account the tray shows.
        WidgetLog.Write("-", "browser-open", $"site=tray url={url}");

        // UseShellExecute: true — without it .NET tries to launch the URL as an
        // executable directly and fails with Win32Exception.
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Menu.Dispose();

        if (_currentIcon is null) return;

        var handle = _currentIcon.Handle;
        _currentIcon.Dispose();
        NativeMethods.DestroyIcon(handle);
        _currentIcon = null;
    }
}

internal static class NativeMethods
{
    // DllImport, not LibraryImport: the latter requires AllowUnsafeBlocks,
    // not worth it for a one-off P/Invoke in a project where unsafe isn't
    // needed anywhere else.
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint handle);
}
