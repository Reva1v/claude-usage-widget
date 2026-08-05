using System.Diagnostics;
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
        };
    }

    private async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);
        if (_closed) return;
        var core = _webView.CoreWebView2;

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
