using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClaudeUsageWidget.App.Tray;
using ClaudeUsageWidget.App.Web;
using ClaudeUsageWidget.App.Windows;
using ClaudeUsageWidget.Core;
using Microsoft.Win32;

namespace ClaudeUsageWidget.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
// Полное имя обязательно: UseWindowsForms делает System.Windows.Forms.Application
// видимым в этом файле, и Application становится неоднозначной ссылкой.
public partial class App : System.Windows.Application
{
    private TrayIcon? _trayIcon;
    private DesktopWidgetWindow? _widgetWindow;
    private TaskbarBandWindow? _bandWindow;
    private SettingsStore? _settings;
    private ClaudeWebSession? _session;
    private UsageStore? _usageStore;
    private StatusStore? _statusStore;
    private DispatcherTimer? _refreshTimer;

    // UsageStore.hasCredentials — синхронная лямбда, а HasSessionCookieAsync
    // ходит в WebView2 и не может быть синхронной. Блокировать UI-поток
    // через .GetAwaiter().GetResult() (как в черновике брифа) значило бы
    // подвесить весь Dispatcher — включая отрисовку и обработку кликов —
    // на время, пока CoreWebView2Environment ещё не готова при первом
    // обращении. Вместо этого RefreshAllAsync обновляет это поле асинхронно
    // непосредственно перед вызовом LoadAsync(), а лямбда просто читает уже
    // готовое значение.
    private volatile bool _hasSessionCookie;

    // Отдельно от _hasSessionCookie: становится true, когда сама проверка
    // куки (HasSessionCookieAsync) бросила исключение (например, WebView2
    // Runtime не установлен, или профиль недоступен) — то есть когда мы даже
    // не смогли выяснить, есть кука или нет, а не когда честно выяснили, что
    // её нет. Читается из UpdateTrayTooltip, чтобы такой сбой не потерялся
    // молча в FireAndForget/Debug-выводе — см. комментарий в RefreshAllAsync.
    private bool _webViewFailing;

    /// Именованный мьютекс единственного экземпляра — живёт полем, чтобы GC
    /// не отпустил его на всё время жизни процесса.
    private System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Один экземпляр на сессию. Живая отладка (2026-08-06) поймала ДВА
        // одновременно работающих экземпляра (зомби от предыдущих запусков
        // dotnet run): их ленты и виджеты дрались за позицию/видимость, и
        // со стороны это выглядело как хаотичные пропадания. Виджету с
        // трей-иконкой второй экземпляр не нужен никогда.
        _singleInstanceMutex = new System.Threading.Mutex(
            initiallyOwned: true, "ClaudeUsageWidget.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        // Для трей-виджета необработанное исключение на UI-потоке (по
        // умолчанию WPF после него завершает процесс) хуже, чем одна неверная
        // или устаревшая цифра на панели — трей-иконка и уже отрисованное
        // состояние должны пережить сбой конкретного обработчика, а не
        // утащить с собой весь процесс. Без диалогов и намеренно минимально:
        // это фоновый виджет, а не окно, в котором есть кому нажать "ОК".
        DispatcherUnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled UI-thread exception: {args.Exception}");
            args.Handled = true;
        };

        // Без окна и MainWindow сборка живёт, пока жив трей-объект и
        // ShutdownMode остаётся OnExplicitShutdown (см. App.xaml) — иначе WPF
        // закрыл бы процесс сразу после OnStartup, не дождавшись Quit.
        _trayIcon = new TrayIcon();
        _trayIcon.QuitRequested += Shutdown;
        _trayIcon.RefreshRequested += () => FireAndForget(RefreshAllAsync);
        _trayIcon.SignInRequested += () => FireAndForget(() => _session!.OpenLoginWindowAsync());
        _trayIcon.TrayMetricSelected += OnTrayMetricSelected;
        _trayIcon.ModelBucketSelected += OnModelBucketSelected;
        _trayIcon.ShowOnDesktopToggled += OnShowOnDesktopToggled;
        _trayIcon.TaskbarBandToggled += OnTaskbarBandToggled;
        _trayIcon.BandPositionSelected += OnBandPositionSelected;
        _trayIcon.LockPositionToggled += OnLockPositionToggled;
        _trayIcon.MenuOpening += RefreshTrayMenuState;

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageWidget", "settings.json");
        _settings = new SettingsStore(settingsPath);

        // "default" — единственный профиль, который этот таск заводит
        // жёстко; свитч между несколькими AccountProfile — задача будущего
        // таска (см. WidgetSettingsData.Accounts).
        var profileFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeUsageWidget", "profiles", "default");
        _session = new ClaudeWebSession(profileFolder, _settings);
        _session.SignedIn += OnSignedIn;

        _usageStore = new UsageStore(
            fetch: _session.FetchUsageAsync,
            hasCredentials: () => _hasSessionCookie,
            now: () => DateTimeOffset.Now,
            loadRetryState: LoadRetryState,
            saveRetryState: SaveRetryState);
        _usageStore.Changed += OnStoresChanged;

        _statusStore = new StatusStore(new StatusApi().FetchAsync);
        _statusStore.Changed += OnStoresChanged;

        _widgetWindow = new DesktopWidgetWindow(_settings);
        _widgetWindow.HideRequested += OnWidgetHideRequested;
        _widgetWindow.SignInRequested += () => FireAndForget(() => _session!.OpenLoginWindowAsync());

        // Сервер обновляет свои цифры медленно — совпадает с
        // UsageStore.RefreshIntervalSeconds/StatusStore.RefreshIntervalSeconds,
        // оба стора всё равно молча схлопывают более частые запросы, но нет
        // смысла тикать чаще, чем есть новые данные.
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(UsageStore.RefreshIntervalSeconds) };
        _refreshTimer.Tick += (_, _) => FireAndForget(RefreshAllAsync);
        _refreshTimer.Start();

        // Порт didWakeNotification: ноутбук проспал дольше интервала таймера
        // — не ждать следующего тика, обновиться сразу после пробуждения.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();

        // Порт widgetVisible-хранимого состояния: если пользователь спрятал
        // виджет глазом в прошлом запуске, он не должен возвращаться сам
        // собой — иначе "Show on desktop" в меню и то, что реально на
        // экране, тут же разойдутся сразу после старта.
        if (_settings.Load().WidgetVisible) _widgetWindow.Show();

        // Тот же принцип, что и для WidgetVisible строкой выше: состояние
        // ленты в таскбаре переживает перезапуск приложения.
        if (_settings.Load().TaskbarBandEnabled) SetTaskbarBandVisible(true);

        FireAndForget(StartupAsync);
    }

    /// Порт applicationDidFinishLaunching: если сессионной куки нет, окно
    /// логина открывается сразу, не дожидаясь клика по трею.
    ///
    /// Оба шага здесь обёрнуты в try — сбой WebView2 (например, не
    /// установлен Runtime) на старте не должен помешать RefreshAllAsync()
    /// ниже выполниться: тот сам заново попробует и корректно отрапортует об
    /// этой же ошибке (см. его комментарий), а StatusStore обязан загрузиться
    /// независимо от того, что случилось с веб-сессией.
    private async Task StartupAsync()
    {
        var hasSession = false;
        try
        {
            hasSession = await _session!.HasSessionCookieAsync();
        }
        catch
        {
            // Ничего не делаем: hasSession остаётся false, и код ниже всё
            // равно попытается открыть LoginWindow — это не страшно, даже
            // если среда WebView2 сломана. EnsureEnvironmentAsync внутри
            // OpenLoginWindowAsync либо создаст среду заново (см. её
            // faulted-сброс), либо просто ещё раз дёшево провалится — и этот
            // сбой отдельно перехвачен ниже. RefreshAllAsync() после этого
            // блока в любом случае столкнётся с той же ошибкой и честно её
            // покажет, независимо от того, открылось окно логина или нет.
        }

        if (!hasSession)
        {
            try
            {
                await _session!.OpenLoginWindowAsync();
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
        // Проверка куки — в собственном try/catch, отдельном от
        // Task.WhenAll ниже: если она бросит (сбой WebView2, а не просто
        // "куки нет"), это не должно помешать _statusStore.LoadAsync()
        // выполниться — тот читает публичный, не завязанный на WebView2
        // эндпоинт status.claude.com, и его судьба никак не связана с
        // состоянием веб-сессии.
        try
        {
            _hasSessionCookie = await _session!.HasSessionCookieAsync();
            _webViewFailing = false;
        }
        catch (Exception ex)
        {
            // Специально НЕ false: если тут выставить _hasSessionCookie в
            // false, UsageStore.LoadAsync ниже коротко замкнётся на
            // Failed(NoCredentials) ещё до вызова _fetch — а это неверный,
            // вводящий в заблуждение диагноз ("не авторизован", хотя на
            // самом деле не работает WebView2, и кнопка Sign in ниже
            // сломается точно так же). Оставляя true, даём LoadAsync дойти
            // до _fetch() = session.FetchUsageAsync, который сам заново
            // наткнётся на ту же ошибку внутри собственного
            // HasSessionCookieAsync — и на этот раз её поймает уже
            // UsageStore.PerformLoadAsync (внешний catch(Exception)),
            // превратив в честный Failed(Network(ex.Message)), видимый в
            // строке статуса на панели. UpdateTrayTooltip ниже дополнительно
            // подсвечивает то же самое в тултипе трея, пока сбой не пройдёт.
            _hasSessionCookie = true;
            _webViewFailing = true;
            System.Diagnostics.Debug.WriteLine($"WebView2 session check failed: {ex}");
        }

        await Task.WhenAll(_usageStore!.LoadAsync(), _statusStore!.LoadAsync());
    }

    private void OnSignedIn()
    {
        // Логин мог пройти под другим аккаунтом — сохранённый id организации
        // от предыдущей сессии не должен пережить новый вход. Порт
        // fetchUsage's onSignedIn-эквивалента в applicationDidFinishLaunching.
        _session!.ClearCachedOrganization();
        FireAndForget(RefreshAllAsync);
    }

    private void OnWidgetHideRequested()
    {
        // Сам DesktopWidgetWindow уже спрятал себя и сохранил
        // WidgetVisible=false в OnEyeClicked до того, как поднял это
        // событие — здесь заново делать нечего, кроме как подтянуть в меню
        // трея снятую галочку "Show on desktop": глаз на панели меняет то же
        // самое состояние, что и пункт меню, и они обязаны показывать одно и
        // то же, даже если это меню сейчас не открыто.
        RefreshTrayMenuState();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        // SystemEvents вызывает обработчики на отдельном системном потоке —
        // и Dispatcher-чувствительный рендер внутри OnStoresChanged, и сами
        // вызовы WebView2 в ClaudeWebSession ожидают UI-поток, поэтому
        // маршалим целиком обработчик, а не только его хвост.
        Dispatcher.Invoke(() => FireAndForget(RefreshAllAsync));
    }

    private void OnStoresChanged()
    {
        // Changed у обоих сторов может прийти не с UI-потока (см.
        // OnPowerModeChanged) — WPF запрещает трогать визуальное дерево не с
        // потока, который им владеет.
        Dispatcher.Invoke(() =>
        {
            RenderWidget();
            UpdateTrayTooltip();
            RefreshTrayIcon();
            RefreshTrayMenuState();
            RenderTaskbarBand();
        });
    }

    /// <summary>Пересчитывает живую цифру трея из свежих данных — та же
    /// пара DialModel.All/TrayText.Metrics, что и тултип, но берёт из неё
    /// только метрику, выбранную в "Tray shows" (TrayMetricKey).</summary>
    private void RefreshTrayIcon()
    {
        var now = DateTimeOffset.Now;
        var snapshot = _usageStore!.CurrentState is UsageState.Ok(var okSnapshot, _)
            ? okSnapshot
            : _usageStore.LastSnapshot;
        var data = _settings!.Load();
        var models = DialModel.All(snapshot, data.ModelBucket, now);
        var metrics = TrayText.Metrics(models);

        _trayIcon!.SetIcon(TrayIconRenderer.Render(metrics[TrayIcon.MetricIndex(data.TrayMetricKey)].Value));
    }

    /// <summary>Подтягивает в меню трея (чекбоксы, "Model limit") актуальное
    /// состояние — на старте и на каждый TrayIcon.MenuOpening/Changed
    /// стора, поскольку часть этого состояния (PositionLocked, доступные
    /// model-бакеты) может измениться не через сам пункт меню.</summary>
    private void RefreshTrayMenuState()
    {
        var data = _settings!.Load();
        var snapshot = _usageStore!.CurrentState is UsageState.Ok(var okSnapshot, _)
            ? okSnapshot
            : _usageStore.LastSnapshot;
        var availableBuckets = snapshot is null ? Array.Empty<string>() : ModelBuckets.Available(snapshot);

        // То же разрешение бакета, что DialModel.All использует для заголовка
        // третьего циферблата — "MODEL (X)" в меню трея должно называть ровно
        // ту модель, которую сейчас реально показывает панель/лента, а не
        // просто "выбранную пользователем" (та может быть Auto/null).
        var resolvedModelLabel = snapshot is not null && ModelBuckets.Resolve(data.ModelBucket, snapshot) is { } resolvedKey
            ? ModelBuckets.Label(resolvedKey)
            : null;

        _trayIcon!.SyncMenuState(new TrayMenuState(
            data.TrayMetricKey,
            availableBuckets,
            data.ModelBucket,
            data.WidgetVisible,
            _widgetWindow!.PositionLocked,
            data.TaskbarBandEnabled,
            data.BandPosition,
            resolvedModelLabel));
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

        // В отличие от TrayMetricKey, ModelBucket виден и на панели виджета
        // (третий циферблат), и в тултипе, а не только в иконке трея.
        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();
    }

    private void OnShowOnDesktopToggled(bool visible)
    {
        var data = _settings!.Load();
        _settings.Save(data with { WidgetVisible = visible });

        // Тот же переключатель, что и глаз на панели — просто вход с
        // противоположной стороны, поэтому просто Show/Hide, без
        // PersistWidgetVisible ниже: настройка уже сохранена строкой выше.
        if (visible) _widgetWindow!.Show(); else _widgetWindow!.Hide();

        RefreshTrayMenuState();
    }

    private void OnLockPositionToggled(bool locked)
    {
        // Сеттер PositionLocked сам обновляет _root.PositionLocked и
        // сохраняет настройку (см. DesktopWidgetWindow.PositionLocked) —
        // здесь дублировать нечего.
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

    /// <summary>Включает/выключает ленту. Окно создаётся лениво при первом
    /// включении и переживает последующие выключения (Detach лишь прячет и
    /// снимает владельца) — пересоздавать TaskbarBandWindow на каждый
    /// чекбокс незачем, Dock/Detach уже идемпотентны.</summary>
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
            // Рендерим ДО Dock: тот сразу вызывает Reposition(), которому
            // нужна актуальная ширина контента, а не ширина от предыдущего
            // показа (или вовсе нулевая при самом первом).
            RenderTaskbarBand();
            _bandWindow.Dock();
        }
        else
        {
            _bandWindow?.Detach();
        }
    }

    /// <summary>TaskbarBandWindow.Lost: нативный HWND ленты пропал не через
    /// наш собственный Detach()/Close() — owned window не рушится каскадно
    /// вместе с таскбаром (в отличие от прежнего WS_CHILD-варианта), так что
    /// это стало защитным бэкстопом на непредвиденный случай, а не основным
    /// путём восстановления после перезапуска explorer.exe (тот теперь чинит
    /// себя сам на ближайшем тике — см. TaskbarBandWindow.RepositionCore).
    /// WPF не даёт повторно показать Window, чей нативный хэндл пропал таким
    /// образом — бросаем старый экземпляр (его HWND уже недействителен,
    /// закрывать нечего) и, если лента всё ещё должна быть включена, заводим
    /// новый, как при обычном первом включении.</summary>
    private void OnTaskbarBandLost()
    {
        // Закрываем WPF-оболочку мёртвого экземпляра: его нативный HWND уже
        // недействителен, но сам Window-объект без Close() остаётся жить в
        // списке окон приложения — живая отладка (2026-08-06) находила по
        // несколько таких осиротевших скрытых окон за один запуск.
        var dead = _bandWindow;
        _bandWindow = null;
        try { dead?.Close(); }
        catch (InvalidOperationException) { /* уже закрыт самим WPF — и хорошо */ }

        if (_settings!.Load().TaskbarBandEnabled) SetTaskbarBandVisible(true);
    }

    /// <summary>Свежие метрики для ленты — тот же DialModel.All/TrayText.Metrics,
    /// что и у иконки трея и тултипа, просто нарисованные в другом окне.
    /// Безвредно вызывать и когда лента выключена/ещё не создана.</summary>
    private void RenderTaskbarBand()
    {
        if (_bandWindow is null) return;

        var now = DateTimeOffset.Now;
        var snapshot = _usageStore!.CurrentState is UsageState.Ok(var okSnapshot, _)
            ? okSnapshot
            : _usageStore.LastSnapshot;
        var data = _settings!.Load();
        var models = DialModel.All(snapshot, data.ModelBucket, now);
        _bandWindow.Render(TrayText.Metrics(models));
    }

    private void RenderWidget()
    {
        var data = _settings!.Load();
        _widgetWindow!.Render(
            _usageStore!.CurrentState,
            _usageStore.LastSnapshot,
            _statusStore!.Status,
            data.ModelBucket,
            _usageStore.RetryPausedUntil);
    }

    private void UpdateTrayTooltip()
    {
        if (_webViewFailing)
        {
            // Простейший честный сигнал наружу для сбоя, который иначе
            // тонет в FireAndForget/Debug-выводе — не модальный MessageBox
            // (тот заблокировал бы Dispatcher прямо во время старта, пока
            // проверка куки ещё не завершилась) и не разовое сообщение
            // (тогда его смыл бы первый же следующий Changed от стора,
            // например когда RefreshWidget вызывается таймером) — а тултип
            // трея, который держится именно до тех пор, пока проблема
            // сохраняется. Панель виджета тем временем и так покажет
            // Failed(Network(...)) через обычный путь UsageStore, как только
            // FetchUsageAsync внутри LoadAsync наткнётся на ту же ошибку
            // (см. комментарий в RefreshAllAsync).
            _trayIcon!.SetTooltip("Claude Usage Widget — WebView2 error, see widget for details");
            return;
        }

        var now = DateTimeOffset.Now;
        var state = _usageStore!.CurrentState;
        var snapshot = state is UsageState.Ok(var okSnapshot, _) ? okSnapshot : _usageStore.LastSnapshot;
        var data = _settings!.Load();
        var models = DialModel.All(snapshot, data.ModelBucket, now);
        var metrics = TrayText.Metrics(models);
        var statusLine = StatusLine.Text(state, now, _usageStore.RetryPausedUntil);

        // "5H 42% · 7D 18% · FAB 8%" — тот же разделитель " · ", что и в
        // строке статуса под циферблатами на панели; статус (если есть)
        // идёт отдельной строкой, а не тем же " · ", чтобы не сливаться с
        // цифрами при беглом взгляде на всплывающую подсказку.
        var metricsText = metrics.Count == 0
            ? "Claude Usage Widget"
            : "Claude Usage Widget — " + string.Join(" · ", metrics.Select(m => $"{m.Label} {m.Value}"));

        _trayIcon!.SetTooltip(statusLine is null ? metricsText : $"{metricsText}\n{statusLine}");
    }

    private UsageRetryState? LoadRetryState()
    {
        var data = _settings!.Load();
        return data.RetryPausedUntil is { } until ? new UsageRetryState(until, data.ConsecutiveRateLimits) : null;
    }

    private void SaveRetryState(UsageRetryState? state)
    {
        var data = _settings!.Load();
        _settings.Save(data with
        {
            RetryPausedUntil = state?.Until,
            ConsecutiveRateLimits = state?.ConsecutiveRateLimits ?? 0,
        });
    }

    /// Обёртка над "выстрелил и забыл" для обработчиков кликов/таймера:
    /// без неё необработанное исключение внутри async-задачи, на которую
    /// никто не подписан через await, тихо теряется (в лучшем случае — уходит
    /// в TaskScheduler.UnobservedTaskException при следующей сборке мусора).
    /// Здесь оно хотя бы попадает в Debug-вывод.
    private static async void FireAndForget(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled error in fire-and-forget task: {ex}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Статический ивент — не отписаться значит держать App живым в
        // подписчиках SystemEvents дольше, чем нужно.
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _refreshTimer?.Stop();

        // NotifyIcon переживает процесс визуально до следующего движения
        // мыши, если его не спрятать явно — Dispose убирает иконку сразу.
        _trayIcon?.Dispose();
        _trayIcon = null;

        _widgetWindow?.Close();
        _widgetWindow = null;

        // Detach перед Close: наше окно всё ещё может быть дочерним
        // Shell_TrayWnd (чужого процесса) — сначала аккуратно отвязываем,
        // потом закрываем как обычное WPF-окно.
        _bandWindow?.Detach();
        _bandWindow?.Close();
        _bandWindow = null;

        base.OnExit(e);
    }
}

