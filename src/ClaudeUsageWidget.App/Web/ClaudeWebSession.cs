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
    // Имя ПРОФИЛЯ WebView2, а не папка: аккаунты разделены профилями внутри
    // одной общей user data folder (WebViewEnvironment), иначе на каждый
    // аккаунт поднимался бы свой набор процессов браузера.
    //
    // NULL — особый и нужный случай: «неявный профиль по умолчанию». Именованные
    // профили WebView2 складывает в `EBWebView\WV2Profile_<имя>`, а профиль,
    // созданный БЕЗ опций, — в `EBWebView\Default`, и добраться до второго по
    // имени нельзя никаким `ProfileName` (измерено 2026-08-26 на живом
    // WebView2 Runtime).
    // Всё, что было залогинено до мультиаккаунта, лежит именно там — поэтому
    // мигрированный аккаунт остаётся на неявном профиле и переживает
    // обновление, а каждый следующий получает именованный.
    private readonly string? _profileName;
    private readonly string _accountId;

    // What this account is called in the log. The DisplayName rather than the
    // id, because the reader of widget.log is a person comparing it against
    // the names on the panel — a GUID there tells them nothing. A rename does
    // not reach this copy until the next launch; the log is diagnostic, and
    // the account it names is still the right one.
    private readonly string _accountLabel;

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

    public ClaudeWebSession(string? profileName, string accountId, string accountLabel, SettingsStore settings)
    {
        _profileName = profileName;
        _accountId = accountId;
        _accountLabel = accountLabel;
        _settings = settings;
    }

    /// Организация у каждого аккаунта своя: до мультиаккаунта это поле лежало
    /// на верхнем уровне настроек просто потому, что аккаунт был один.
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

    /// Стирает куки и кэш ЭТОГО аккаунта. Нужно для «Remove» в трее: удалить
    /// аккаунт из настроек, не почистив профиль, значит оставить его залогиненным
    /// — и следующий аккаунт с тем же id унаследовал бы чужую сессию.
    public async Task ClearBrowsingDataAsync()
    {
        var webView = await EnsureFetchWebViewAsync().ConfigureAwait(true);
        await webView.Profile.ClearBrowsingDataAsync().ConfigureAwait(true);
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
            // Сохранённая организация могла умереть вместе с сессией (аккаунт
            // удалён из неё, доступ отозван) — не тащить мёртвый id в
            // следующую попытку логина.
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

    /// Повторный вызов, пока окно логина ещё открыто (или ещё только
    /// открывается), поднимает то же самое окно вместо создания второго —
    /// сравнение с <c>_loginWindow</c>, а не счётчик, поскольку окно само
    /// сбрасывает поле в null при закрытии.
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
            // Тот же профиль, что читает фетч — см. инвариант в CreateFetchWebViewAsync.
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
            // Контроллер и так мёртв — Close() поверх умершего процесса
            // браузера может бросить, терять из-за этого пересоздание нельзя.
        }
        _fetchController = null;
        _hiddenHost?.Close();
        _hiddenHost = null;
    }

    // Not static: it logs under the account's name.
    private async Task<string> NavigateAndReadAsync(CoreWebView2 webView, string url, CancellationToken ct)
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

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
        // Среда теперь одна на процесс: разделяет аккаунты ProfileName на
        // контроллере, а не отдельная папка на каждый.
        WebViewEnvironment.SharedAsync();

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

        // The same profile the login window uses, through the one helper that
        // keeps the two identical — see the invariant on WebViewEnvironment.
        var controller = await WebViewEnvironment
            .CreateControllerAsync(environment, hwnd, _profileName, _accountId)
            .ConfigureAwait(true);

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
