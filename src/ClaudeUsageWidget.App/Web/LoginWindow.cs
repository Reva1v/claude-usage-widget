using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
// UseWindowsForms makes System.Windows.Forms globally visible and every one of
// these has a namesake there.
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Grid = System.Windows.Controls.Grid;
using RowDefinition = System.Windows.Controls.RowDefinition;
using DockPanel = System.Windows.Controls.DockPanel;
using Dock = System.Windows.Controls.Dock;

namespace ClaudeUsageWidget.App.Web;

/// <summary>
/// A plain, visible sign-in window: a WebView2 pointed at claude.ai/login on
/// the same profile <see cref="ClaudeWebSession"/> reads cookies from. Port
/// of <c>ClaudeLoginWindowController</c> in
/// <c>Sources/ClaudeUsageWidget/ClaudeWebSession.swift:173-265</c>, minus the
/// error-page fallback (not required by task-15-brief.md's Step 3 checklist).
/// </summary>
public sealed class LoginWindow : Window
{
    private readonly CoreWebView2Environment _environment;

    // The profile of the account being signed into. Must match the profile
    // of this same session's fetch controller, or the cookie will silently
    // land somewhere other than where it is later read from.
    private readonly string? _profileName;

    // Named in the title and the hint: with several accounts the sign-in page
    // itself looks identical for every one of them, and the user asked
    // (2026-09-04) how to tell which profile the window was about to sign in.
    private readonly string _accountLabel;

    private readonly WebView2Control _webView = new();
    private readonly TextBox _linkBox = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _cookiePollTimer;
    private bool _signedIn;

    // Loaded → InitializeAsync contains two awaits (EnsureCoreWebView2Async,
    // ClearBrowsingDataAsync); if the user closes the window (via the X) in
    // that gap, Closed manages to run before the method reaches the second
    // half — Stop() there catches a timer that has not started yet (which
    // doesn't help), and without this flag InitializeAsync, after the awaits,
    // would go ahead as if nothing happened and start the timer and
    // Navigate() on an already-dead window, with the timer ticking forever.
    // Checked after every await below.
    private bool _closed;

    /// The sessionKey cookie appeared and the window has already closed
    /// itself.
    public event Action? SignedIn;

    public LoginWindow(CoreWebView2Environment environment, string? profileName, string accountLabel)
    {
        _environment = environment;
        _profileName = profileName;
        _accountLabel = accountLabel;

        // Logged at construction rather than at Show(): the only caller shows
        // the window immediately, and an exception between the two would
        // otherwise leave a login attempt with no trace at all.
        WidgetLog.Write(_accountLabel, "login-window", "open");

        Title = $"Sign in to Claude.ai — account \"{_accountLabel}\"";
        Width = 1000;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildLayout();

        // Polling, not an event: unlike WKHTTPCookieStoreObserver in the
        // original, CoreWebView2CookieManager has no callback for cookie
        // changes — task-15-brief.md, step 3 explicitly asks for polling on
        // two triggers: after every navigation and once every 2 s (in case
        // the cookie appears without a new navigation, for example through a
        // redirect inside the SPA).
        _cookiePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cookiePollTimer.Tick += async (_, _) =>
        {
            // async void: an unhandled exception here would go straight into
            // the Dispatcher and bring down the whole process. The tick could
            // already be sitting in the Dispatcher's queue at the moment the
            // WebView2 controller was destroyed (for example, the window
            // closed) — CookieManager then throws, and that must not be fatal
            // for the tray widget.
            try
            {
                await CheckForSessionCookieAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cookie poll tick failed: {ex}");
            }
        };

        // EnsureCoreWebView2Async creates the control's child HWND — that
        // needs a real parent, so initialization waits for Loaded instead of
        // running straight in the constructor.
        Loaded += async (_, _) =>
        {
            // The same risk as Tick above: EnsureCoreWebView2Async may throw
            // (for example, the WebView2 browser process failed to start, or
            // the profile folder is locked by another process) — async void
            // on the Dispatcher must not bring down the application because
            // of that.
            try
            {
                await InitializeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoginWindow initialization failed: {ex}");
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _cookiePollTimer.Stop();
            // signedIn separates "the cookie arrived and we closed ourselves"
            // from "the user gave up and closed the window", which is the
            // difference between a sign-in that worked and one that did not.
            WidgetLog.Write(_accountLabel, "login-window", $"closed signedIn={_signedIn}");
        };
    }

    /// A hint line, a paste box, and the browser under them.
    ///
    /// The box exists for magic-link sign-in: the link arrives by e-mail, and
    /// clicking it there opens the DEFAULT browser, which signs THAT browser in
    /// and leaves this one — the only one the widget reads cookies from —
    /// logged out. The link is also typically single-use, so opening it in the
    /// wrong browser burns it. Pasting it here keeps request and redemption in
    /// the same cookie jar.
    private Grid BuildLayout()
    {
        _linkBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) NavigateToPastedLink();
        };

        var open = new Button { Content = "Open", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0) };
        open.Click += (_, _) => NavigateToPastedLink();

        var bar = new DockPanel { Margin = new Thickness(8, 0, 8, 8) };
        DockPanel.SetDock(open, Dock.Right);
        bar.Children.Add(open);
        bar.Children.Add(_linkBox);

        var hint = new TextBlock
        {
            Text = $"This window signs in the widget account \"{_accountLabel}\". "
                 + "Signing in with a magic link? Request it on this page, then paste the link from the e-mail here — "
                 + "opening it in your normal browser signs that browser in, not this one.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 8, 8, 4),
            Opacity = 0.75,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(hint, 0);
        Grid.SetRow(bar, 1);
        Grid.SetRow(_webView, 2);
        root.Children.Add(hint);
        root.Children.Add(bar);
        root.Children.Add(_webView);
        return root;
    }

    /// Https only, and no host allow-list: magic-link e-mails routinely go
    /// through a tracking redirector, and Microsoft-federated accounts pass
    /// through login.microsoftonline.com — an allow-list of claude.ai would
    /// reject exactly the links this box exists for.
    private void NavigateToPastedLink()
    {
        var raw = _linkBox.Text?.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            _linkBox.ToolTip = "That is not an https link.";
            return;
        }

        _linkBox.ToolTip = null;
        _webView.CoreWebView2?.Navigate(uri.ToString());
    }

    private async Task InitializeAsync()
    {
        // The same profile as this account's fetch controller, through the one
        // helper that keeps the two identical — see the invariant on
        // WebViewEnvironment. Null options mean the implicit default profile.
        await _webView.EnsureCoreWebView2Async(
            _environment, WebViewEnvironment.ControllerOptionsFor(_environment, _profileName)).ConfigureAwait(true);
        if (_closed) return;
        var core = _webView.CoreWebView2;
        WebViewEnvironment.VerifyProfile(core, _profileName, _accountLabel);

        // Port of clearing browsing data before login — ClaudeWebSession.swift:200-209.
        // A rejected Turnstile challenge survives the window being recreated
        // and immediately repeats the same loop, unless the profile's state
        // is wiped before EVERY showing of the login window, not just once
        // over the whole lifetime of the application.
        await core.Profile.ClearBrowsingDataAsync().ConfigureAwait(true);
        if (_closed) return;

        core.NavigationCompleted += async (_, _) =>
        {
            // Same as Tick/Loaded above: async void on a WebView2 event must
            // not bring down the process if CheckForSessionCookieAsync throws
            // (for example, CookieManager reached an already-destroyed
            // controller between the navigation and the handling of its
            // event) — port of the same principle as in
            // ClaudeWebSession.OnNavigationCompleted (ClaudeWebSession.cs:207-250).
            try
            {
                await CheckForSessionCookieAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation-completed cookie check failed: {ex}");
            }
        };
        _cookiePollTimer.Start();

        core.Navigate("https://claude.ai/login");
    }

    private async Task CheckForSessionCookieAsync()
    {
        if (_signedIn) return;

        // Both the timer and NavigationCompleted can fire after Close()
        // already happened: the tick — if it managed to get into the
        // Dispatcher's queue before Stop(), NavigationCompleted — if the
        // event was raised before InitializeAsync got to unsubscribing.
        // Without this check the method would reach CoreWebView2/CookieManager
        // on an already-closed window.
        if (_closed) return;

        var core = _webView.CoreWebView2;
        if (core is null) return;

        var cookies = await core.CookieManager.GetCookiesAsync("https://claude.ai").ConfigureAwait(true);
        if (!SessionCookie.IsPresent(cookies)) return;

        _signedIn = true;
        _cookiePollTimer.Stop();

        // Order matters: Close() synchronously raises Closed, whose handler
        // in ClaudeWebSession.OnLoginWindowClosed unsubscribes SignedIn first
        // thing (window.SignedIn -= OnLoginWindowSignedIn) — if the window is
        // closed first, by the time SignedIn?.Invoke() runs the subscriber
        // list is already empty, the event goes nowhere, ClaudeWebSession.SignedIn
        // never fires, and App.OnSignedIn (ClearCachedOrganization + an
        // immediate refresh) does not run at all — the dials silently wait
        // for the next 300-second tick. Raise the event while the subscribers
        // are still in place, and only then close the window.
        SignedIn?.Invoke();
        Close();
    }
}
