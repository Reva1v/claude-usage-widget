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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Без окна и MainWindow сборка живёт, пока жив трей-объект и
        // ShutdownMode остаётся OnExplicitShutdown (см. App.xaml) — иначе WPF
        // закрыл бы процесс сразу после OnStartup, не дождавшись Quit.
        _trayIcon = new TrayIcon();
        _trayIcon.QuitRequested += Shutdown;
        _trayIcon.RefreshRequested += () => FireAndForget(RefreshAllAsync);
        _trayIcon.SignInRequested += () => FireAndForget(() => _session!.OpenLoginWindowAsync());

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
        _widgetWindow.Show();

        FireAndForget(StartupAsync);
    }

    /// Порт applicationDidFinishLaunching: если сессионной куки нет, окно
    /// логина открывается сразу, не дожидаясь клика по трею.
    private async Task StartupAsync()
    {
        _hasSessionCookie = await _session!.HasSessionCookieAsync();
        if (!_hasSessionCookie) await _session.OpenLoginWindowAsync();

        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        _hasSessionCookie = await _session!.HasSessionCookieAsync();
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
        // событие — здесь заново делать нечего. Подписка оставлена ради
        // симметрии с остальными событиями окна/трея и как место, где это
        // объяснено.
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
        });
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
        var now = DateTimeOffset.Now;
        var snapshot = _usageStore!.CurrentState is UsageState.Ok(var okSnapshot, _)
            ? okSnapshot
            : _usageStore.LastSnapshot;
        var data = _settings!.Load();
        var models = DialModel.All(snapshot, data.ModelBucket, now);
        var metrics = TrayText.Metrics(models);

        _trayIcon!.SetTooltip(metrics.Count == 0
            ? "Claude Usage Widget"
            : "Claude Usage Widget — " + string.Join("  ", metrics.Select(m => $"{m.Label} {m.Value}")));
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

        base.OnExit(e);
    }
}

