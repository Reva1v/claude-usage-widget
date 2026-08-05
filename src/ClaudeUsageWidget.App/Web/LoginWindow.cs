using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;

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
    private readonly WebView2Control _webView = new();
    private readonly DispatcherTimer _cookiePollTimer;
    private bool _signedIn;

    /// The sessionKey cookie appeared and the window has already closed
    /// itself.
    public event Action? SignedIn;

    public LoginWindow(CoreWebView2Environment environment)
    {
        _environment = environment;

        Title = "Sign in to Claude.ai";
        Width = 1000;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _webView;

        // Опрос, а не событие: у CoreWebView2CookieManager, в отличие от
        // WKHTTPCookieStoreObserver в оригинале, нет колбэка на изменение
        // кук — task-15-brief.md, шаг 3 явно просит опрос по двум триггерам:
        // после каждой навигации и раз в 2 с (на случай, если кука появится
        // без новой навигации, например через редирект внутри SPA).
        _cookiePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cookiePollTimer.Tick += async (_, _) => await CheckForSessionCookieAsync().ConfigureAwait(true);

        // EnsureCoreWebView2Async создаёт дочерний HWND контрола — тому
        // нужен реальный родитель, поэтому инициализация ждёт Loaded, а не
        // запускается прямо в конструкторе.
        Loaded += async (_, _) => await InitializeAsync().ConfigureAwait(true);
        Closed += (_, _) => _cookiePollTimer.Stop();
    }

    private async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);
        var core = _webView.CoreWebView2;

        // Порт сброса browsing data перед логином — ClaudeWebSession.swift:200-209.
        // Отклонённый Turnstile-челлендж переживает пересоздание окна и
        // немедленно повторяет тот же луп, если не стирать состояние
        // профиля перед КАЖДЫМ показом окна логина, а не только один раз за
        // всё время жизни приложения.
        await core.Profile.ClearBrowsingDataAsync().ConfigureAwait(true);

        core.NavigationCompleted += async (_, _) => await CheckForSessionCookieAsync().ConfigureAwait(true);
        _cookiePollTimer.Start();

        core.Navigate("https://claude.ai/login");
    }

    private async Task CheckForSessionCookieAsync()
    {
        if (_signedIn) return;
        var core = _webView.CoreWebView2;
        if (core is null) return;

        var cookies = await core.CookieManager.GetCookiesAsync("https://claude.ai").ConfigureAwait(true);
        if (!SessionCookie.IsPresent(cookies)) return;

        _signedIn = true;
        _cookiePollTimer.Stop();
        Close();
        SignedIn?.Invoke();
    }
}
