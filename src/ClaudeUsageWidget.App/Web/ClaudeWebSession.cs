using System.IO;
using System.Text.Json;
using System.Windows.Interop;
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
    private readonly string _profileFolder;
    private readonly SettingsStore _settings;

    // Единственная on-demand среда WebView2 на весь процесс: LoginWindow
    // получает её же через EnsureEnvironmentAsync, чтобы кука, полученная при
    // входе, легла в тот самый профиль, который потом читает
    // HasSessionCookieAsync/FetchUsageAsync.
    private Task<CoreWebView2Environment>? _environmentTask;

    // Скрытый CoreWebView2 для фетча JSON-страниц — один на всю сессию;
    // отдельного webview на запрос не создаём, а сериализуем обращения к
    // этому через _fetchGate ниже.
    private Task<CoreWebView2>? _fetchWebViewTask;
    private HwndSource? _hiddenHost;

    // Один разделяемый webview не может обслуживать две навигации одновременно
    // — вторая Navigate() перезапишет страницу раньше, чем обработчик первой
    // успеет прочитать HttpStatusCode/тело. UsageStore и так не допускает
    // параллельных вызовов FetchUsageAsync (коалесцирует LoadAsync), но этот
    // семафор — самостоятельная гарантия на уровне самого webview, а не
    // побочный эффект чужой логики.
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private LoginWindow? _loginWindow;

    /// Кука появилась и окно логина закрылось — порт uses site's onSignedIn.
    public event Action? SignedIn;

    public ClaudeWebSession(string profileFolder, SettingsStore settings)
    {
        _profileFolder = profileFolder;
        _settings = settings;
    }

    /// Порт <c>hasSessionCookie()</c> — sessionKey на домене claude.ai с
    /// непустым значением.
    public async Task<bool> HasSessionCookieAsync()
    {
        var webView = await EnsureFetchWebViewAsync().ConfigureAwait(true);
        var cookies = await webView.CookieManager.GetCookiesAsync("https://claude.ai").ConfigureAwait(true);
        return SessionCookie.IsPresent(cookies);
    }

    /// Порт <c>fetchUsage()</c>.
    public async Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        if (!await HasSessionCookieAsync().ConfigureAwait(true))
            throw new UsageException(UsageError.NoCredentials);

        var organizationId = _settings.Load().OrganizationId;
        if (string.IsNullOrEmpty(organizationId))
        {
            var body = await FetchPageAsync("https://claude.ai/api/organizations", ct).ConfigureAwait(true);
            organizationId = OrganizationPicker.Pick(body);
            if (string.IsNullOrEmpty(organizationId))
                throw new UsageException(UsageError.MalformedResponse);

            _settings.Save(_settings.Load() with { OrganizationId = organizationId });
        }

        try
        {
            var usageBody = await FetchPageAsync(
                $"https://claude.ai/api/organizations/{organizationId}/usage", ct).ConfigureAwait(true);
            return UsageDecoder.Snapshot(usageBody);
        }
        catch (UsageException ex) when (ex.Error.Kind == UsageErrorKind.Unauthorized)
        {
            // Сохранённая организация могла умереть вместе с сессией (аккаунт
            // удалён из неё, доступ отозван) — не тащить мёртвый id в
            // следующую попытку логина.
            ClearCachedOrganization();
            throw;
        }
    }

    public void ClearCachedOrganization()
    {
        _settings.Save(_settings.Load() with { OrganizationId = null });
    }

    /// Повторный вызов, пока окно логина ещё открыто, поднимает то же самое
    /// окно вместо создания второго — сравнение с <c>_loginWindow</c>, а не
    /// счётчик, поскольку окно само сбрасывает поле в null при закрытии.
    public async Task<LoginWindow> OpenLoginWindowAsync()
    {
        if (_loginWindow is { } existing)
        {
            existing.Show();
            existing.Activate();
            return existing;
        }

        var environment = await EnsureEnvironmentAsync().ConfigureAwait(true);
        var window = new LoginWindow(environment);
        window.SignedIn += OnLoginWindowSignedIn;
        window.Closed += OnLoginWindowClosed;
        _loginWindow = window;
        window.Show();
        return window;
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
    // Page-fetch: порт WebPageJSONFetcher.
    // ------------------------------------------------------------------

    private async Task<string> FetchPageAsync(string url, CancellationToken ct)
    {
        await _fetchGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var webView = await EnsureFetchWebViewAsync().ConfigureAwait(true);
            return await NavigateAndReadAsync(webView, url, ct).ConfigureAwait(true);
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private static async Task<string> NavigateAndReadAsync(CoreWebView2 webView, string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 30 с — тот же таймаут, что и в оригинале
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
                if (!args.IsSuccess)
                {
                    tcs.TrySetException(
                        new UsageException(UsageError.Network($"Claude.ai navigation failed: {args.WebErrorStatus}.")));
                    return;
                }

                // Семантика статусов — порт webView(_:didFinish:) в
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
                if (args.HttpStatusCode is < 200 or >= 300)
                {
                    tcs.TrySetException(
                        new UsageException(UsageError.Network($"Claude.ai returned HTTP {args.HttpStatusCode}.")));
                    return;
                }

                var raw = await webView.ExecuteScriptAsync(
                    "document.body.innerText || document.body.textContent || ''").ConfigureAwait(true);
                // ExecuteScriptAsync возвращает JSON-представление значения
                // скрипта (строка приходит как JSON-строка с экранированием),
                // а не голый текст — распаковываем тем же JSON-декодером.
                var text = JsonSerializer.Deserialize<string>(raw);
                if (text is null) tcs.TrySetException(new UsageException(UsageError.MalformedResponse));
                else tcs.TrySetResult(text);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(new UsageException(UsageError.Network(ex.Message)));
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
    // Ленивая, разделяемая среда/webview.
    // ------------------------------------------------------------------

    private Task<CoreWebView2Environment> EnsureEnvironmentAsync() =>
        _environmentTask ??= CreateEnvironmentAsync();

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(_profileFolder);
        // browserExecutableFolder=null → системный Edge/WebView2 Runtime;
        // options=null → значения по умолчанию. userDataFolder — единственное,
        // что нужно задать явно: это и есть профиль из AccountProfile.
        return await CoreWebView2Environment.CreateAsync(userDataFolder: _profileFolder).ConfigureAwait(true);
    }

    private Task<CoreWebView2> EnsureFetchWebViewAsync() =>
        _fetchWebViewTask ??= CreateFetchWebViewAsync();

    private async Task<CoreWebView2> CreateFetchWebViewAsync()
    {
        var environment = await EnsureEnvironmentAsync().ConfigureAwait(true);

        // HwndSource вместо WPF Window: даёт голый нативный HWND без
        // WS_VISIBLE и без жизненного цикла Show()/Activate/Loaded — этому
        // хосту никогда не нужен экран, только валидный родитель для
        // CreateCoreWebView2ControllerAsync. IsVisible = false на самом
        // контроллере ниже — вторая, независимая гарантия невидимости.
        _hiddenHost = new HwndSource(new HwndSourceParameters("ClaudeWebSessionFetchHost")
        {
            Width = 0,
            Height = 0,
        });

        var controller = await environment
            .CreateCoreWebView2ControllerAsync(_hiddenHost.Handle).ConfigureAwait(true);
        controller.IsVisible = false;
        return controller.CoreWebView2;
    }
}

/// Общий предикат для двух мест, которые ищут sessionKey в куках профиля:
/// <see cref="ClaudeWebSession.HasSessionCookieAsync"/> (можно ли уже
/// фетчить usage) и <see cref="LoginWindow"/> (когда закрывать окно логина).
/// Раздельные копии одного и того же условия рано или поздно разъехались бы.
internal static class SessionCookie
{
    public static bool IsPresent(IEnumerable<CoreWebView2Cookie> cookies) =>
        cookies.Any(cookie =>
            cookie.Name == "sessionKey" &&
            cookie.Domain.EndsWith("claude.ai", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(cookie.Value));
}
