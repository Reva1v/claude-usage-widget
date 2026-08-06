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
    private Window? _hiddenHost;

    // КРИТИЧНО держать сильную ссылку на контроллер, а не только на его
    // CoreWebView2: .NET-обёртка CoreWebView2Controller при сборке мусора
    // закрывает нативный контроллер (финализатор → Close()), после чего
    // любой вызов кэшированного CoreWebView2 навсегда падает с "CoreWebView2
    // members cannot be accessed after the WebView2 control is disposed".
    // Именно так и проявлялось: данные шли, пока не случилась Gen2-сборка,
    // а дальше каждый рефреш — одна и та же ошибка до перезапуска.
    private CoreWebView2Controller? _fetchController;

    // Один разделяемый webview не может обслуживать две навигации одновременно
    // — вторая Navigate() перезапишет страницу раньше, чем обработчик первой
    // успеет прочитать HttpStatusCode/тело. UsageStore и так не допускает
    // параллельных вызовов FetchUsageAsync (коалесцирует LoadAsync), но этот
    // семафор — самостоятельная гарантия на уровне самого webview, а не
    // побочный эффект чужой логики.
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private LoginWindow? _loginWindow;

    // Открытие окна логина само по себе асинхронное (нужна среда WebView2),
    // а значит между проверкой `_loginWindow == null` и её присвоением есть
    // await-разрыв. Без отдельного поля два конкурирующих вызова (автооткрытие
    // на старте гонится с ручным кликом "Sign in" из трея) оба успевают
    // увидеть null и создать по окну каждый. `_openLoginWindowTask`
    // выставляется синхронно, до первого await — второй вызов видит уже
    // запущенную задачу и просто дожидается того же самого окна.
    private Task<LoginWindow>? _openLoginWindowTask;

    /// Кука появилась и окно логина закрылось — порт uses site's onSignedIn.
    public event Action? SignedIn;

    public ClaudeWebSession(string profileFolder, SettingsStore settings)
    {
        _profileFolder = profileFolder;
        _settings = settings;
    }

    /// Порт <c>hasSessionCookie()</c> — sessionKey на домене claude.ai с
    /// непустым значением.
    public Task<bool> HasSessionCookieAsync() =>
        RunWithFetchWebViewAsync(async webView =>
        {
            var cookies = await webView.CookieManager.GetCookiesAsync("https://claude.ai").ConfigureAwait(true);
            return SessionCookie.IsPresent(cookies);
        });

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

    /// Повторный вызов, пока окно логина ещё открыто (или ещё только
    /// открывается), поднимает то же самое окно вместо создания второго —
    /// сравнение с <c>_loginWindow</c>, а не счётчик, поскольку окно само
    /// сбрасывает поле в null при закрытии.
    public Task<LoginWindow> OpenLoginWindowAsync()
    {
        if (_loginWindow is { } existing)
        {
            existing.Show();
            existing.Activate();
            return Task.FromResult(existing);
        }

        // `??=` присваивает синхронно, до какого-либо await внутри
        // CreateLoginWindowAsync — конкурирующий вызов, попавший сюда же на
        // том же UI-потоке во время await EnsureEnvironmentAsync() ниже,
        // увидит уже не-null _openLoginWindowTask и получит ту же задачу
        // вместо того, чтобы начать создавать второе окно с нуля.
        return _openLoginWindowTask ??= CreateLoginWindowAsync();
    }

    private async Task<LoginWindow> CreateLoginWindowAsync()
    {
        try
        {
            var environment = await EnsureEnvironmentAsync().ConfigureAwait(true);
            var window = new LoginWindow(environment);
            window.SignedIn += OnLoginWindowSignedIn;
            window.Closed += OnLoginWindowClosed;
            _loginWindow = window;
            window.Show();
            return window;
        }
        finally
        {
            // Освобождаем "замок" независимо от исхода: при успехе
            // следующий вызов пойдёт по ветке `_loginWindow is { } existing`
            // выше; при сбое (например, среда WebView2 не создалась) —
            // разрешаем следующему вызову попробовать заново, а не залипнуть
            // на однажды провалившейся задаче навсегда.
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
    // Page-fetch: порт WebPageJSONFetcher.
    // ------------------------------------------------------------------

    private async Task<string> FetchPageAsync(string url, CancellationToken ct)
    {
        await _fetchGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            return await RunWithFetchWebViewAsync(
                webView => NavigateAndReadAsync(webView, url, ct)).ConfigureAwait(true);
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    /// Все обращения к разделяемому fetch-webview идут через эту обёртку:
    /// если движок умер (процесс браузера завершился/упал — например, после
    /// сна — или контроллер оказался закрыт), кэш сбрасывается и попытка
    /// повторяется один раз на свежесозданном webview вместо того, чтобы
    /// возвращать одну и ту же ошибку до перезапуска приложения.
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

    /// Формы смерти WebView2, после которых кэшированный CoreWebView2
    /// бесполезен: ObjectDisposedException — обёртка закрыта ("...cannot be
    /// accessed after the WebView2 control is disposed"); COMException
    /// 0x8007139F (ERROR_INVALID_STATE) и 0x80010108 (RPC_E_DISCONNECTED) —
    /// процесс браузера завершился, нативный объект отвалился.
    private static bool IsWebViewDead(Exception ex) => ex switch
    {
        ObjectDisposedException => true,
        System.Runtime.InteropServices.COMException com =>
            (uint)com.HResult is 0x8007139F or 0x80010108,
        _ => false,
    };

    private void ResetFetchWebView()
    {
        _fetchWebViewTask = null;
        try
        {
            _fetchController?.Close();
        }
        catch
        {
            // Контроллер и так мёртв — Close() поверх умершего процесса
            // браузера может бросить, терять из-за этого пересоздание нельзя.
        }
        _fetchController = null;
        _hiddenHost?.Close();
        _hiddenHost = null;
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
                // Смерть webview пробрасываем как есть — по ней
                // RunWithFetchWebViewAsync пересоздаёт движок и повторяет
                // запрос; завёрнутая в UsageException.Network она выглядела
                // бы обычной сетевой ошибкой и уходила пользователю на дисплей
                // ("cannot be accessed after the WebView2...") до перезапуска.
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
    // Ленивая, разделяемая среда/webview.
    // ------------------------------------------------------------------

    private Task<CoreWebView2Environment> EnsureEnvironmentAsync()
    {
        // Тот же приём, что и у `_openLoginWindowTask` в CreateLoginWindowAsync
        // (см. finally-комментарий выше): без него `??=` запомнил бы FAULTED
        // (или CANCELED) Task навсегда — если первая попытка провалилась
        // (например, Runtime WebView2 ещё не установлен), пользователь мог бы
        // поставить его прямо во время работы приложения, но UI продолжал бы
        // показывать ту же самую ошибку до перезапуска, потому что
        // CreateEnvironmentAsync() больше никогда не вызвалась бы повторно.
        // Сбрасываем кеш перед `??=`, чтобы следующий вызов пересоздал среду
        // с нуля.
        //
        // Конкурентность: блокировка не нужна, потому что все вызовы этого
        // метода приходят с UI-потока. EnsureEnvironmentAsync вызывается
        // только из CreateFetchWebViewAsync (через EnsureFetchWebViewAsync) и
        // CreateLoginWindowAsync — оба, в свою очередь, вызываются
        // исключительно из App.xaml.cs (RefreshAllAsync/StartupAsync и
        // OpenLoginWindowAsync через SignInRequested/OnSignedIn), где каждый
        // await всюду использует ConfigureAwait(true) и возвращается на
        // Dispatcher; единственный источник вызовов с не-UI-потока,
        // SystemEvents.PowerModeChanged, сам явно маршалится через
        // Dispatcher.Invoke перед тем, как дойти до RefreshAllAsync.
        if (_environmentTask is { IsFaulted: true } or { IsCanceled: true })
            _environmentTask = null;

        return _environmentTask ??= CreateEnvironmentAsync();
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(_profileFolder);
        // browserExecutableFolder=null → системный Edge/WebView2 Runtime;
        // options=null → значения по умолчанию. userDataFolder — единственное,
        // что нужно задать явно: это и есть профиль из AccountProfile.
        return await CoreWebView2Environment.CreateAsync(userDataFolder: _profileFolder).ConfigureAwait(true);
    }

    private Task<CoreWebView2> EnsureFetchWebViewAsync()
    {
        // Тот же сброс FAULTED/CANCELED-кеша, что и в EnsureEnvironmentAsync
        // ниже: провал самого создания webview (а не его последующая смерть)
        // не должен залипать до перезапуска приложения.
        if (_fetchWebViewTask is { IsFaulted: true } or { IsCanceled: true })
            _fetchWebViewTask = null;

        return _fetchWebViewTask ??= CreateFetchWebViewAsync();
    }

    private async Task<CoreWebView2> CreateFetchWebViewAsync()
    {
        var environment = await EnsureEnvironmentAsync().ConfigureAwait(true);

        // Раньше здесь был голый HwndSource(Width=0,Height=0) в расчёте на
        // то, что CreateWindowEx без WS_VISIBLE в стиле останется невидимым
        // сам по себе. На практике пользователь всё равно увидел на рабочем
        // столе чёрное окно "ClaudeWebSessionFetchHost" — судя по всему,
        // CoreWebView2Controller при первом прикреплении к родителю сам
        // выставляет этому HWND WS_VISIBLE как побочный эффект (раннер
        // WebView2 недокументированно расчитан на "обычное" встраивание в
        // видимое окно, а не в чистый message-only хост). Полагаться на то,
        // что родитель, отданный контроллеру, останется невидимым сам по
        // себе, оказалось недостаточно — нужна гарантия, которая переживёт
        // то, что делает сам контроллер.
        //
        // Поэтому вместо HwndSource — обычное WPF Window, но с HWND,
        // принудительно созданным через EnsureHandle() (тот же приём, что
        // и в Windows/TaskbarBandWindow.TryAttach — там тоже нужен реальный
        // HWND до того, как окно вообще может появиться на экране): весь
        // путь WPF, которым выставляется WS_VISIBLE, лежит внутри Show()/
        // Visibility-setter'а, а раз Show() здесь не вызывается никогда,
        // сработать ему просто негде. Поверх — четыре независимых слоя
        // подстраховки на случай, если WebView2 всё же попытается вернуть
        // родителю видимость: офф-скрин позиция, нулевой размер,
        // WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE (не попадёт в alt-tab/панель
        // задач, даже если формально станет видимым) и, отдельно,
        // controller.IsVisible=false на стороне самого WebView2.
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

        var controller = await environment.CreateCoreWebView2ControllerAsync(hwnd).ConfigureAwait(true);
        controller.IsVisible = false;
        // В поле, не в локальную переменную — см. doc-comment у
        // _fetchController: без сильной ссылки GC финализирует обёртку
        // контроллера и тем самым закрывает CoreWebView2 под нами.
        _fetchController = controller;

        // Проактивный сброс при смерти процесса браузера (крэш рантайма,
        // завершение после сна): следующий фетч сразу начнёт с создания
        // нового движка, а не с гарантированно провальной попытки на мёртвом.
        // Событие приходит на UI-поток (тот, где создан webview) — гонок с
        // ResetFetchWebView из RunWithFetchWebViewAsync нет.
        controller.CoreWebView2.ProcessFailed += (_, args) =>
        {
            // Сравнение с _fetchController отсекает запоздавшее событие от
            // УЖЕ заменённого движка (ретрай успел пересоздать) — иначе оно
            // снесло бы свежий контроллер и скрытый хост под ним.
            if (!ReferenceEquals(_fetchController, controller)) return;
            if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                ResetFetchWebView();
            }
        };
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
