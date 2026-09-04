using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using ClaudeUsageWidget.App.Windows;
using ClaudeUsageWidget.Core;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageWidget.App.Web;

/// <summary>
/// Reads Claude subscription usage through the authenticated claude.ai web
/// session. Port of <c>Sources/ClaudeUsageWidget/ClaudeWebSession.swift</c>:
/// the same page-fetch trick (an API endpoint is loaded as a browser
/// navigation rather than issued as a raw HTTP request, so Cloudflare sees a
/// normal page load using the same cookie jar the login used), the same
/// organization selection/caching and the same 401-clears-cached-org
/// behaviour.
///
/// User-Agent: the Swift build overrides WKWebView's UA to impersonate
/// Safari (see the comment at the top of ClaudeWebSession.swift) because
/// WKWebView's own UA string otherwise announces itself as an embedded
/// WebKit view, and Cloudflare's Turnstile fingerprints more than the UA
/// header alone. On Windows this port deliberately does the opposite and
/// never touches WebView2's UA: WebView2 *is* the installed Edge/Chromium
/// engine, so its native fingerprint already reads as a normal, logged-in
/// Chromium browser. Stamping a fake UA on top of a genuine Chromium engine
/// would only create the very mismatch the impersonation on macOS exists to
/// avoid.
/// </summary>
public sealed class ClaudeWebSession
{
    // The WebView2 PROFILE name, not a folder: accounts are separated by
    // profiles inside one shared user data folder (WebViewEnvironment),
    // otherwise every account would spin up its own set of browser processes.
    //
    // NULL is a special and necessary case: the "implicit default profile".
    // WebView2 stores named profiles under `EBWebView\WV2Profile_<name>`, while
    // a profile created WITHOUT options goes to `EBWebView\Default`, and there
    // is no `ProfileName` that reaches the latter by name (measured 2026-08-26
    // on a live WebView2 Runtime).
    // Everything that was signed in before multi-account support lives exactly
    // there — so the migrated account stays on the implicit profile and
    // survives the upgrade, while every subsequent one gets a named profile.
    private readonly string? _profileName;
    private readonly string _accountId;

    // What this account is called in the log. The DisplayName rather than the
    // id, because the reader of widget.log is a person comparing it against
    // the names on the panel — a GUID there tells them nothing. A rename does
    // not reach this copy until the next launch; the log is diagnostic, and
    // the account it names is still the right one.
    private readonly string _accountLabel;

    private readonly SettingsStore _settings;

    // The single on-demand WebView2 environment for the whole process:
    // LoginWindow obtains the same one through EnsureEnvironmentAsync, so the
    // cookie obtained at sign-in lands in the very profile that
    // HasSessionCookieAsync/FetchUsageAsync later read.
    private Task<CoreWebView2Environment>? _environmentTask;

    // The hidden CoreWebView2 for fetching JSON pages — one for the whole
    // session; a separate webview per request is not created, instead
    // accesses to this one are serialized through _fetchGate below.
    private Task<CoreWebView2>? _fetchWebViewTask;
    private Window? _hiddenHost;

    // CRITICAL to hold a strong reference to the controller, not only to its
    // CoreWebView2: the .NET wrapper CoreWebView2Controller closes the native
    // controller on garbage collection (finalizer → Close()), after which any
    // call on the cached CoreWebView2 fails forever with "CoreWebView2
    // members cannot be accessed after the WebView2 control is disposed".
    // That is exactly how it manifested: data kept flowing until a Gen2
    // collection happened, and after that every refresh hit the same error
    // until a restart.
    private CoreWebView2Controller? _fetchController;

    // One shared webview cannot serve two navigations at once — the second
    // Navigate() would overwrite the page before the first one's handler
    // manages to read the HttpStatusCode/body. UsageStore already disallows
    // concurrent FetchUsageAsync calls (it coalesces LoadAsync), but this
    // semaphore is an independent guarantee at the level of the webview
    // itself, not a side effect of someone else's logic.
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private LoginWindow? _loginWindow;

    // Opening the login window is itself asynchronous (the WebView2
    // environment is needed), which means there is an await-gap between
    // checking `_loginWindow == null` and assigning it. Without a separate
    // field, two concurrent calls (auto-open at start-up racing a manual
    // "Sign in" click from the tray) would both manage to see null and each
    // create its own window. `_openLoginWindowTask` is set synchronously,
    // before the first await — the second call sees the already-running task
    // and simply waits for the same window.
    private Task<LoginWindow>? _openLoginWindowTask;

    /// The cookie appeared and the login window closed — port uses site's onSignedIn.
    public event Action? SignedIn;

    public ClaudeWebSession(string? profileName, string accountId, string accountLabel, SettingsStore settings)
    {
        _profileName = profileName;
        _accountId = accountId;
        _accountLabel = accountLabel;
        _settings = settings;
    }

    /// Each account has its own organization: before multi-account support
    /// this field lived at the top level of settings simply because there was
    /// only one account.
    private AccountProfile? LoadProfile() => _settings.Load().Account(_accountId);

    private string? LoadOrganizationId() => LoadProfile()?.OrganizationId;

    private void SaveOrganizationId(string? organizationId)
    {
        _settings.Save(_settings.Load().WithAccount(_accountId, a => a with { OrganizationId = organizationId }));
    }

    /// The three subscription fields, written together in ONE save so the
    /// sentinel flips atomically: everywhere else, a null capability list means
    /// "never asked", and a half-written profile would make that a lie.
    ///
    /// Raw, never a label. The mapping lives in <see cref="SubscriptionTier"/>,
    /// and a wrong guess there must be fixable by shipping a new build rather
    /// than by asking claude.ai for a body that is already on disk.
    private void SaveSubscriptionFields(OrganizationFields fields)
    {
        _settings.Save(_settings.Load().WithAccount(_accountId, a => a with
        {
            Capabilities = fields.Capabilities,
            RateLimitTier = fields.RateLimitTier,
            RavenType = fields.RavenType,
        }));
    }

    /// One organizations fetch per RUN for the backfill below, whatever it
    /// found. Without it, an account whose organization has vanished from the
    /// body would re-fetch on every poll forever: the fields would stay null,
    /// and null is the condition being tested.
    private bool _subscriptionFieldsFetched;

    /// Clears cookies and cache for THIS account. Needed for "Remove" in the
    /// tray: removing an account from settings without cleaning its profile
    /// would leave it signed in — and the next account with the same id would
    /// inherit someone else's session.
    public async Task ClearBrowsingDataAsync()
    {
        var webView = await EnsureFetchWebViewAsync().ConfigureAwait(true);
        await webView.Profile.ClearBrowsingDataAsync().ConfigureAwait(true);
    }

    /// Port of <c>hasSessionCookie()</c> — sessionKey on the claude.ai domain
    /// with a non-empty value.
    public Task<bool> HasSessionCookieAsync() =>
        RunWithFetchWebViewAsync(async webView =>
        {
            var cookies = await webView.CookieManager.GetCookiesAsync("https://claude.ai").ConfigureAwait(true);
            return SessionCookie.IsPresent(cookies);
        });

    /// Port of <c>fetchUsage()</c>.
    public async Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        if (!await HasSessionCookieAsync().ConfigureAwait(true))
            throw new UsageException(UsageError.NoCredentials);

        var organizationId = LoadOrganizationId();
        if (string.IsNullOrEmpty(organizationId))
        {
            var body = await FetchPageAsync("https://claude.ai/api/organizations", ct).ConfigureAwait(true);
            organizationId = OrganizationPicker.Pick(body);
            if (string.IsNullOrEmpty(organizationId))
                throw new UsageException(UsageError.MalformedResponse);

            SaveOrganizationId(organizationId);
            RecordSubscription(body, organizationId);
        }
        else if (LoadProfile()?.Capabilities is null && !_subscriptionFieldsFetched)
        {
            // The organization was picked before this release, so the plan
            // fields were never stored — one extra fetch of the ORGANIZATIONS
            // endpoint fills them in. Deliberately not the usage endpoint: that
            // is the one the 429 budget is spent on, and this list is cheap and
            // changes about never. Bounded to once per run by the flag, and to
            // once ever by the fields it writes.
            _subscriptionFieldsFetched = true;
            try
            {
                var body = await FetchPageAsync("https://claude.ai/api/organizations", ct)
                    .ConfigureAwait(true);
                RecordSubscription(body, organizationId);
            }
            catch (UsageException ex)
            {
                // The plan line is decoration; the dials are the product. This
                // fetch is the ONLY reason an account that polled fine before
                // the upgrade now touches a second endpoint, and letting it
                // through would mean a blip — or worse, a 429, which UsageStore
                // answers by pausing the whole account — failing a poll for the
                // sake of a label. Logged and dropped; the flag above already
                // means it is not retried before the next launch.
                WidgetLog.Write(_accountLabel, "organization", $"backfill-failed {ex.Error.Kind}");
            }
        }

        try
        {
            var usageBody = await FetchPageAsync(
                $"https://claude.ai/api/organizations/{organizationId}/usage", ct).ConfigureAwait(true);
            return UsageDecoder.Snapshot(usageBody);
        }
        catch (UsageException ex) when (ex.Error.Kind == UsageErrorKind.Unauthorized)
        {
            // The saved organization could have died along with the session
            // (the account was removed from it, access revoked) — don't carry
            // a dead id into the next login attempt.
            ClearCachedOrganization();
            throw;
        }
    }

    /// Stores the picked organization's subscription fields and logs the same
    /// line the shape probe already wrote, now with the label those fields
    /// produce — so `widget.log` shows what the panel decided and off what.
    ///
    /// One key, `organization`, for the pick and the backfill alike: it is what
    /// anyone greps.
    private void RecordSubscription(string body, string organizationId)
    {
        var fields = OrganizationPicker.Fields(body, organizationId);
        var label = SubscriptionTier.Label(fields.Capabilities, fields.RateLimitTier, fields.RavenType);

        WidgetLog.Write(_accountLabel, "organization",
            $"{OrganizationPicker.Describe(body, organizationId)} tier={label}");
        SaveSubscriptionFields(fields);
    }

    public void ClearCachedOrganization()
    {
        // The subscription fields stay. They describe the SUBSCRIPTION, not the
        // organization id that died with the session, and the pick that follows
        // overwrites them from a fresh body anyway — clearing here would only
        // blank the plan line for as long as the account is signed out.
        SaveOrganizationId(null);
    }

    /// A repeat call while the login window is still open (or is still only
    /// opening) brings up that same window instead of creating a second one —
    /// a comparison against <c>_loginWindow</c>, not a counter, since the
    /// window itself resets the field to null when it closes.
    public Task<LoginWindow> OpenLoginWindowAsync()
    {
        if (_loginWindow is { } existing)
        {
            // Activate() cannot steal the foreground from another process
            // (Windows' foreground lock): an open window behind everything
            // else is what "the button does nothing" can also look like.
            WidgetLog.Write(_accountLabel, "login-window", "reuse existing");
            existing.Show();
            existing.Activate();
            return Task.FromResult(existing);
        }
        // Only a task that is STILL RUNNING is shared. This used to be
        // `_openLoginWindowTask ??= CreateLoginWindowAsync()`, which kept a
        // COMPLETED task forever: once the WebView2 environment is up, the
        // await inside CreateLoginWindowAsync continues synchronously, the
        // method runs through its finally (which clears the field) before
        // `??=` stores the finished task over that null — and every later
        // call handed back a window that had long been closed. Measured
        // 2026-09-04: eight panel clicks, eight `still opening
        // status=RanToCompletion` lines, no window. The first call at start-up
        // worked only because the environment was not ready yet, so the await
        // yielded and the finally ran after the assignment.
        if (_openLoginWindowTask is { IsCompleted: false } pending)
        {
            WidgetLog.Write(_accountLabel, "login-window", "still opening");
            return pending;
        }

        // A concurrent call on the same UI thread during the await inside
        // (auto-open at start-up racing a tray click) sees the running task
        // above and shares it instead of creating a second window.
        _openLoginWindowTask = CreateLoginWindowAsync();
        return _openLoginWindowTask;
    }

    private async Task<LoginWindow> CreateLoginWindowAsync()
    {
        try
        {
            var environment = await EnsureEnvironmentAsync().ConfigureAwait(true);
            // The same profile that the fetch reads — see the invariant in CreateFetchWebViewAsync.
            var window = new LoginWindow(environment, _profileName, _accountLabel);
            window.SignedIn += OnLoginWindowSignedIn;
            window.Closed += OnLoginWindowClosed;
            _loginWindow = window;
            window.Show();
            return window;
        }
        catch (Exception ex)
        {
            WidgetLog.Write(_accountLabel, "login-window", $"create-failed {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            // Release the "lock" regardless of outcome: on success the next
            // call goes down the `_loginWindow is { } existing` branch above;
            // on failure (e.g. the WebView2 environment failed to create) —
            // let the next call try again instead of getting stuck on a
            // once-failed task forever.
            _openLoginWindowTask = null;
        }
    }

    private void OnLoginWindowSignedIn() => SignedIn?.Invoke();

    private void OnLoginWindowClosed(object? sender, EventArgs e)
    {
        var window = (LoginWindow)sender!;
        window.SignedIn -= OnLoginWindowSignedIn;
        window.Closed -= OnLoginWindowClosed;
        if (ReferenceEquals(_loginWindow, window)) _loginWindow = null;
    }

    // ------------------------------------------------------------------
    // Page-fetch: port of WebPageJSONFetcher.
    // ------------------------------------------------------------------

    /// One retry on a transient navigation failure — and a log line for EVERY
    /// failure, the successful retry included.
    ///
    /// The panel only ever shows the last error, so a `Claude.ai navigation
    /// failed: Unknown.` that came and went used to leave no trace at all.
    /// The log is the only place that shows whether it was a single blip or
    /// the network being down.
    private async Task<string> FetchPageAsync(string url, CancellationToken ct)
    {
        var path = new Uri(url).AbsolutePath;

        // The gate is held across BOTH attempts: the shared fetch webview
        // serves one navigation at a time, and releasing it for the
        // three-second pause would let another request in halfway through
        // our retry.
        await _fetchGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var body = await RunWithFetchWebViewAsync(
                        webView => NavigateAndReadAsync(webView, url, ct)).ConfigureAwait(true);
                    if (attempt > 1)
                        WidgetLog.Write(_accountLabel, "retry-ok", $"path={path} attempt={attempt}");
                    return body;
                }
                catch (NavigationFailedException ex)
                {
                    // http=0 means "it never got as far as a status": the
                    // navigation failed at the transport. Any other value here
                    // is a code the branch above did not classify — log it, do
                    // not guess.
                    WidgetLog.Write(_accountLabel, "nav-failed",
                        $"path={path} status={ex.Status} http={ex.HttpStatusCode} attempt={attempt}");

                    // The second attempt is the last. Non-transient statuses
                    // (DNS, certificate) fail identically three seconds later
                    // and only double the noise.
                    if (attempt >= 2 || !NavigationRetryPolicy.IsTransient(ex.Status.ToString())) throw;

                    await Task.Delay(NavigationRetryPolicy.RetryDelay, ct).ConfigureAwait(true);
                }
            }
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    /// All accesses to the shared fetch webview go through this wrapper: if
    /// the engine died (the browser process exited/crashed — for example
    /// after sleep — or the controller turned out to be closed), the cache is
    /// reset and the attempt is retried once on a freshly created webview
    /// instead of returning the same error until the application restarts.
    private async Task<T> RunWithFetchWebViewAsync<T>(Func<CoreWebView2, Task<T>> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            var webView = await EnsureFetchWebViewAsync().ConfigureAwait(true);
            try
            {
                return await action(webView).ConfigureAwait(true);
            }
            catch (Exception ex) when (attempt == 0 && IsWebViewDead(ex))
            {
                ResetFetchWebView();
            }
        }
    }

    /// Forms of WebView2 death after which the cached CoreWebView2 is
    /// useless: ObjectDisposedException — the wrapper is closed ("...cannot
    /// be accessed after the WebView2 control is disposed"); COMException
    /// 0x8007139F (ERROR_INVALID_STATE) and 0x80010108 (RPC_E_DISCONNECTED)
    /// — the browser process exited, the native object fell off.
    private static bool IsWebViewDead(Exception ex) => ex switch
    {
        ObjectDisposedException => true,
        System.Runtime.InteropServices.COMException com =>
            (uint)com.HResult is 0x8007139F or 0x80010108,
        _ => false,
    };

    private void ResetFetchWebView()
    {
        // Rare and always a symptom: the engine died, or a fetch hit a dead
        // one. Cheap to log, and it is the line that explains a burst of
        // failures that otherwise look unrelated.
        WidgetLog.Write(_accountLabel, "webview-reset", "reason=fetch-webview-dead");

        _fetchWebViewTask = null;
        try
        {
            _fetchController?.Close();
        }
        catch
        {
            // The controller is already dead — Close() on top of a dead
            // browser process may throw, and recreation must not be lost
            // because of that.
        }
        _fetchController = null;
        _hiddenHost?.Close();
        _hiddenHost = null;
    }

    // Not static: it logs under the account's name.
    private async Task<string> NavigateAndReadAsync(CoreWebView2 webView, string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 30 s — the same timeout as in the original
        // (WebPageJSONFetcher.fetch, URLRequest(timeoutInterval: 30)).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        using var timeoutRegistration = timeoutCts.Token.Register(() =>
        {
            if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
            else tcs.TrySetException(new UsageException(UsageError.Network("Claude.ai request timed out.")));
        });

        async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            try
            {
                // Status codes BEFORE IsSuccess. A 4xx on a top-level navigation
                // lands in Chromium's error page, and WebView2 then reports
                // IsSuccess=false with WebErrorStatus.Unknown — while
                // HttpStatusCode still carries the code. Measured 2026-09-04:
                // one account's session had expired, the server answered
                // 403 application/json on every poll, and the IsSuccess check
                // sitting first turned "sign in again" into "navigation
                // failed: Unknown" for days. HttpStatusCode is 0 only when no
                // HTTP exchange happened at all — that, and only that, is a
                // transport failure.
                if (args.HttpStatusCode != 0 && args.HttpStatusCode is < 200 or >= 300)
                {
                    // Once for ANY non-2xx, before the codes are sorted out
                    // below: 401/403/429 each take their own path, and without
                    // this line the log would not show that an answer arrived.
                    WidgetLog.Write(_accountLabel, "nav-http",
                        $"path={new Uri(url).AbsolutePath} http={args.HttpStatusCode} success={args.IsSuccess} status={args.WebErrorStatus}");
                }

                // Status semantics — port of webView(_:didFinish:) in
                // ClaudeWebSession.swift:126-139.
                if (args.HttpStatusCode is 401 or 403)
                {
                    tcs.TrySetException(new UsageException(UsageError.Unauthorized));
                    return;
                }
                if (args.HttpStatusCode == 429)
                {
                    tcs.TrySetException(new UsageException(UsageError.RateLimited(null)));
                    return;
                }
                if (args.HttpStatusCode is < 200 or >= 300 && args.HttpStatusCode != 0)
                {
                    tcs.TrySetException(
                        new UsageException(UsageError.Network($"Claude.ai returned HTTP {args.HttpStatusCode}.")));
                    return;
                }

                if (!args.IsSuccess)
                {
                    // Its own type rather than a bare UsageException:
                    // FetchPageAsync has to tell "the navigation never
                    // happened" (worth retrying) from "the server answered
                    // badly" (not), and a message string is no way to decide
                    // that. What the user sees is unchanged.
                    tcs.TrySetException(new NavigationFailedException(args.WebErrorStatus, args.HttpStatusCode));
                    return;
                }

                var raw = await webView.ExecuteScriptAsync(
                    "document.body.innerText || document.body.textContent || ''").ConfigureAwait(true);
                // ExecuteScriptAsync returns a JSON representation of the
                // script's value (a string arrives as an escaped JSON string),
                // not raw text — unwrap it with the same JSON decoder.
                var text = JsonSerializer.Deserialize<string>(raw);
                if (text is null) tcs.TrySetException(new UsageException(UsageError.MalformedResponse));
                else tcs.TrySetResult(text);
            }
            catch (Exception ex)
            {
                // A webview death is rethrown as-is — RunWithFetchWebViewAsync
                // uses it to recreate the engine and retry the request; if it
                // were wrapped in UsageException.Network it would look like an
                // ordinary network error and would surface to the user's
                // display ("cannot be accessed after the WebView2...") until
                // a restart.
                tcs.TrySetException(IsWebViewDead(ex) ? ex : new UsageException(UsageError.Network(ex.Message)));
            }
        }

        webView.NavigationCompleted += OnNavigationCompleted;
        try
        {
            webView.Navigate(url);
            return await tcs.Task.ConfigureAwait(true);
        }
        finally
        {
            webView.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    // ------------------------------------------------------------------
    // Lazy, shared environment/webview.
    // ------------------------------------------------------------------

    private Task<CoreWebView2Environment> EnsureEnvironmentAsync()
    {
        // The same trick as `_openLoginWindowTask` in CreateLoginWindowAsync
        // (see the finally-comment above): without it `??=` would remember a
        // FAULTED (or CANCELED) Task forever — if the first attempt failed
        // (for example, the WebView2 Runtime is not installed yet), the user
        // could install it right while the application is running, but the UI
        // would keep showing the same error until a restart, because
        // CreateEnvironmentAsync() would never be called again. Reset the
        // cache before `??=`, so the next call recreates the environment from
        // scratch.
        //
        // Concurrency: no lock is needed, because all calls to this method
        // come from the UI thread. EnsureEnvironmentAsync is only called from
        // CreateFetchWebViewAsync (through EnsureFetchWebViewAsync) and
        // CreateLoginWindowAsync — both of which, in turn, are called
        // exclusively from App.xaml.cs (RefreshAllAsync/StartupAsync and
        // OpenLoginWindowAsync through SignInRequested/OnSignedIn), where
        // every await everywhere uses ConfigureAwait(true) and returns to the
        // Dispatcher; the one source of calls from a non-UI thread,
        // SystemEvents.PowerModeChanged, is itself explicitly marshalled
        // through Dispatcher.Invoke before it reaches RefreshAllAsync.
        if (_environmentTask is { IsFaulted: true } or { IsCanceled: true })
            _environmentTask = null;

        return _environmentTask ??= CreateEnvironmentAsync();
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
        // The environment is now one per process: it separates accounts by
        // ProfileName on the controller, rather than a separate folder for
        // each.
        WebViewEnvironment.SharedAsync();

    private Task<CoreWebView2> EnsureFetchWebViewAsync()
    {
        // The same FAULTED/CANCELED cache reset as in EnsureEnvironmentAsync
        // below: a failure of the webview's creation itself (as opposed to
        // its later death) must not stick around until the application
        // restarts.
        if (_fetchWebViewTask is { IsFaulted: true } or { IsCanceled: true })
            _fetchWebViewTask = null;

        return _fetchWebViewTask ??= CreateFetchWebViewAsync();
    }

    private async Task<CoreWebView2> CreateFetchWebViewAsync()
    {
        var environment = await EnsureEnvironmentAsync().ConfigureAwait(true);

        // This used to be a bare HwndSource(Width=0,Height=0), betting that
        // CreateWindowEx without WS_VISIBLE in its style would stay invisible
        // on its own. In practice the user still saw a black
        // "ClaudeWebSessionFetchHost" window on the desktop — apparently
        // CoreWebView2Controller, on first attaching to the parent, sets
        // WS_VISIBLE on that HWND itself as a side effect (the WebView2
        // runner is undocumentedly designed for "normal" embedding into a
        // visible window, not into a pure message-only host). Relying on the
        // parent handed to the controller staying invisible by itself turned
        // out not to be enough — a guarantee is needed that outlives what the
        // controller itself does.
        //
        // So instead of HwndSource — a regular WPF Window, but with its HWND
        // forcibly created through EnsureHandle() (the same trick as in
        // Windows/TaskbarBandWindow.TryAttach — that one also needs a real
        // HWND before the window can appear on screen at all): the entire WPF
        // path that sets WS_VISIBLE lives inside Show()/the Visibility
        // setter, and since Show() is never called here, it simply has
        // nowhere to fire. On top of that — four independent layers of
        // insurance in case WebView2 still tries to give the parent back its
        // visibility: an off-screen position, zero size,
        // WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE (won't show up in alt-tab/the
        // taskbar even if it formally becomes visible) and, separately,
        // controller.IsVisible=false on WebView2's own side.
        _hiddenHost = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = false,
            Visibility = Visibility.Hidden,
            Width = 0,
            Height = 0,
            Left = -32000,
            Top = -32000,
        };

        var hwnd = new WindowInteropHelper(_hiddenHost).EnsureHandle();

        var exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle);
        exStyle |= Win32.WsExNoActivate | Win32.WsExToolWindow;
        Win32.SetWindowLongPtr(hwnd, Win32.GwlExStyle, (nint)exStyle);

        // The same profile the login window uses, through the one helper that
        // keeps the two identical — see the invariant on WebViewEnvironment.
        var controller = await WebViewEnvironment
            .CreateControllerAsync(environment, hwnd, _profileName, _accountId)
            .ConfigureAwait(true);

        controller.IsVisible = false;
        // Into a field, not a local variable — see the doc-comment on
        // _fetchController: without a strong reference, GC finalizes the
        // controller's wrapper and thereby closes CoreWebView2 under us.
        _fetchController = controller;

        // Proactive reset on the browser process's death (runtime crash,
        // termination after sleep): the next fetch will immediately start by
        // creating a new engine, rather than with a guaranteed-to-fail
        // attempt on a dead one. The event arrives on the UI thread (the one
        // the webview was created on) — there is no race with
        // ResetFetchWebView from RunWithFetchWebViewAsync.
        controller.CoreWebView2.ProcessFailed += (_, args) =>
        {
            // Comparing against _fetchController filters out a late event
            // from an engine that has ALREADY been replaced (a retry managed
            // to recreate it) — otherwise it would tear down the fresh
            // controller and the hidden host under it.
            if (!ReferenceEquals(_fetchController, controller)) return;

            // Logged for EVERY kind, including the ones that do not trigger a
            // reset below: which kinds actually occur here is the open
            // question this log is meant to answer.
            WidgetLog.Write(_accountLabel, "webview-process-failed", $"kind={args.ProcessFailedKind}");

            if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                ResetFetchWebView();
            }
        };

        // Diagnostics for a navigation that fails as WebErrorStatus.Unknown with
        // http=0 (seen twice in a row on one account, 2026-09-04): the docs say
        // only "an unknown error", so the log records what a failed page load
        // can hide. A JSON body served as an attachment turns the navigation
        // into a DOWNLOAD, which never completes as a page; a redirect or a
        // non-JSON answer shows up as a resource line. Nothing here fires on a
        // healthy 200 application/json poll, so the file stays small.
        var core = controller.CoreWebView2;
        core.DownloadStarting += (_, args) =>
        {
            var op = args.DownloadOperation;
            WidgetLog.Write(_accountLabel, "nav-download",
                $"uri={op.Uri} mime={op.MimeType} disposition={op.ContentDisposition}");
            // A fetch that became a file is a failed fetch: cancel it rather
            // than drop a stray .json into Downloads on every poll.
            args.Cancel = true;
            args.Handled = true;
        };
        core.WebResourceResponseReceived += (_, args) =>
        {
            if (!args.Request.Uri.Contains("/api/organizations", StringComparison.Ordinal)) return;
            var headers = args.Response.Headers;
            var type = headers.Contains("content-type") ? headers.GetHeader("content-type") : "-";
            var disposition = headers.Contains("content-disposition") ? headers.GetHeader("content-disposition") : "-";
            if (args.Response.StatusCode == 200 &&
                type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return;
            WidgetLog.Write(_accountLabel, "resource",
                $"path={new Uri(args.Request.Uri).AbsolutePath} http={args.Response.StatusCode} type={type} disposition={disposition}");
        };
        core.NavigationStarting += (_, args) =>
        {
            if (args.IsRedirected) WidgetLog.Write(_accountLabel, "nav-redirect", $"to={args.Uri}");
        };
        return core;
    }
}

/// The navigation never happened at all — no answer from the server, only a
/// reason code from WebView2.
///
/// Derives from UsageException and carries the same UsageError.Network as
/// before: to UsageStore (and to the panel's status line) this is still an
/// ordinary network error. All that changed is that FetchPageAsync can now
/// recognise it and decide whether to retry.
internal sealed class NavigationFailedException : UsageException
{
    public CoreWebView2WebErrorStatus Status { get; }

    /// 0 when no HTTP exchange happened; otherwise the code WebView2 saw on
    /// the way into its error page, kept for the log.
    public int HttpStatusCode { get; }

    public NavigationFailedException(CoreWebView2WebErrorStatus status, int httpStatusCode = 0)
        : base(UsageError.Network($"Claude.ai navigation failed: {status}."))
    {
        Status = status;
        HttpStatusCode = httpStatusCode;
    }
}

/// A shared predicate for the two places that look for sessionKey in the
/// profile's cookies: <see cref="ClaudeWebSession.HasSessionCookieAsync"/>
/// (whether usage can already be fetched) and <see cref="LoginWindow"/>
/// (when to close the login window). Separate copies of the same condition
/// would eventually have drifted apart.
internal static class SessionCookie
{
    public static bool IsPresent(IEnumerable<CoreWebView2Cookie> cookies) =>
        cookies.Any(cookie =>
            cookie.Name == "sessionKey" &&
            cookie.Domain.EndsWith("claude.ai", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(cookie.Value));
}
