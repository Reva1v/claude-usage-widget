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

    // Профиль того аккаунта, в который входим. Обязан совпадать с профилем
    // фетч-контроллера этой же сессии, иначе кука ляжет не туда, откуда её
    // потом читают, — молча.
    private readonly string? _profileName;

    // Named in the title and the hint: with several accounts the sign-in page
    // itself looks identical for every one of them, and the user asked
    // (2026-09-04) how to tell which profile the window was about to sign in.
    private readonly string _accountLabel;

    private readonly WebView2Control _webView = new();
    private readonly TextBox _linkBox = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _cookiePollTimer;
    private bool _signedIn;

    // Loaded → InitializeAsync содержит два await (EnsureCoreWebView2Async,
    // ClearBrowsingDataAsync); если пользователь закроет окно (крестиком) в
    // этом промежутке, Closed успевает отработать раньше, чем метод дойдёт
    // до второй половины — Stop() там застаёт ещё не запущенный таймер (не
    // помогает), а без этого флага InitializeAsync после awaits как ни в чём
    // не бывало стартовал бы таймер и Navigate() на уже мёртвом окне, и
    // таймер тикал бы вечно. Проверяется после каждого await ниже.
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

        // Опрос, а не событие: у CoreWebView2CookieManager, в отличие от
        // WKHTTPCookieStoreObserver в оригинале, нет колбэка на изменение
        // кук — task-15-brief.md, шаг 3 явно просит опрос по двум триггерам:
        // после каждой навигации и раз в 2 с (на случай, если кука появится
        // без новой навигации, например через редирект внутри SPA).
        _cookiePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cookiePollTimer.Tick += async (_, _) =>
        {
            // async void: необработанное исключение здесь ушло бы прямо в
            // Dispatcher и уронило бы весь процесс. Тик мог уже стоять в
            // очереди Dispatcher'а в момент, когда контроллер WebView2 был
            // разрушен (например, окно закрылось) — CookieManager тогда
            // бросает, и это не должно быть фатальным для трей-виджета.
            try
            {
                await CheckForSessionCookieAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cookie poll tick failed: {ex}");
            }
        };

        // EnsureCoreWebView2Async создаёт дочерний HWND контрола — тому
        // нужен реальный родитель, поэтому инициализация ждёт Loaded, а не
        // запускается прямо в конструкторе.
        Loaded += async (_, _) =>
        {
            // Тот же риск, что и у Tick выше: EnsureCoreWebView2Async может
            // бросить (например, процесс браузера WebView2 не поднялся, или
            // папка профиля занята другим процессом) — async void на
            // Dispatcher'е не должен ронять приложение из-за этого.
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

        // Порт сброса browsing data перед логином — ClaudeWebSession.swift:200-209.
        // Отклонённый Turnstile-челлендж переживает пересоздание окна и
        // немедленно повторяет тот же луп, если не стирать состояние
        // профиля перед КАЖДЫМ показом окна логина, а не только один раз за
        // всё время жизни приложения.
        await core.Profile.ClearBrowsingDataAsync().ConfigureAwait(true);
        if (_closed) return;

        core.NavigationCompleted += async (_, _) =>
        {
            // Как и Tick/Loaded выше: async void на событии WebView2 не
            // должен ронять процесс, если CheckForSessionCookieAsync бросит
            // (например, CookieManager обратился к уже разрушенному
            // контроллеру между навигацией и обработкой её события) —
            // порт того же принципа, что и в ClaudeWebSession.OnNavigationCompleted
            // (ClaudeWebSession.cs:207-250).
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

        // И таймер, и NavigationCompleted могут сработать уже после Close():
        // тик — если успел встать в очередь Dispatcher'а до Stop(),
        // NavigationCompleted — если событие поднялось до того, как
        // InitializeAsync дошёл до отписки. Без этой проверки метод обратился
        // бы к CoreWebView2/CookieManager на уже закрытом окне.
        if (_closed) return;

        var core = _webView.CoreWebView2;
        if (core is null) return;

        var cookies = await core.CookieManager.GetCookiesAsync("https://claude.ai").ConfigureAwait(true);
        if (!SessionCookie.IsPresent(cookies)) return;

        _signedIn = true;
        _cookiePollTimer.Stop();

        // Порядок важен: Close() синхронно поднимает Closed, чей обработчик
        // в ClaudeWebSession.OnLoginWindowClosed первым делом отписывает
        // SignedIn (window.SignedIn -= OnLoginWindowSignedIn) — если сначала
        // закрыть окно, к моменту SignedIn?.Invoke() список подписчиков уже
        // пуст, событие уходит в никуда, ClaudeWebSession.SignedIn никогда не
        // срабатывает, и App.OnSignedIn (ClearCachedOrganization + немедленный
        // refresh) не выполняется вовсе — циферблаты молча ждут следующего
        // 300-секундного тика. Поднимаем событие, пока подписчики ещё на
        // месте, и только потом закрываем окно.
        SignedIn?.Invoke();
        Close();
    }
}
