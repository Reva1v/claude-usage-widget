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
    private StatusStore? _statusStore;
    private DispatcherTimer? _refreshTimer;

    /// Edit mode is a session state, not a setting: a widget that starts up in
    /// edit mode because it was closed in edit mode would be a trap.
    private bool _editingLayout;

    /// Один AccountRuntime на настроенный аккаунт, в порядке настроек.
    /// Пересоздаётся целиком при добавлении/удалении аккаунта, а не
    /// мутируется на месте — так порядок строк на панели и порядок в
    /// настройках не могут разойтись.
    private IReadOnlyList<AccountRuntime> _accounts = [];

    // UsageStore.hasCredentials — синхронная лямбда, а HasSessionCookieAsync
    // ходит в WebView2 и не может быть синхронной. Блокировать UI-поток
    // через .GetAwaiter().GetResult() (как в черновике брифа) значило бы
    // подвесить весь Dispatcher — включая отрисовку и обработку кликов —
    // на время, пока CoreWebView2Environment ещё не готова при первом
    // обращении. Вместо этого RefreshAllAsync обновляет это поле асинхронно
    // непосредственно перед вызовом LoadAsync(), а лямбда просто читает уже
    // готовое значение.
    ///
    /// Ключ — id аккаунта: один общий флаг на четыре аккаунта означал бы, что
    /// залогиненный аккаунт читается как разлогиненный, стоит соседнему
    /// потерять куку.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _hasSessionCookie = new();

    // Отдельно от _hasSessionCookie: становится true, когда сама проверка
    // куки (HasSessionCookieAsync) бросила исключение (например, WebView2
    // Runtime не установлен, или профиль недоступен) — то есть когда мы даже
    // не смогли выяснить, есть кука или нет, а не когда честно выяснили, что
    // её нет. Читается из UpdateTrayTooltip, чтобы такой сбой не потерялся
    // молча в FireAndForget/Debug-выводе — см. комментарий в RefreshAllAsync.
    /// Тоже по аккаунтам: сломаться WebView2 может на одном профиле и работать
    /// на остальных.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _webViewFailing = new();

    /// Именованный мьютекс единственного экземпляра — живёт полем, чтобы GC
    /// не отпустил его на всё время жизни процесса.
    private System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LayoutPreview.RunIfRequested();

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
            // Debug output reaches nobody on a tray widget; the log is the
            // only place a swallowed exception can be seen afterwards.
            WidgetLog.Write("-", "unhandled", $"dispatcher {args.Exception.GetType().Name}: {args.Exception.Message}");
            args.Handled = true;
        };

        // Без окна и MainWindow сборка живёт, пока жив трей-объект и
        // ShutdownMode остаётся OnExplicitShutdown (см. App.xaml) — иначе WPF
        // закрыл бы процесс сразу после OnStartup, не дождавшись Quit.
        _trayIcon = new TrayIcon();
        _trayIcon.QuitRequested += Shutdown;
        // Только трей-аккаунт: ручное обновление всех четырёх — ровно то, за
        // что этот эндпоинт наказывает лимитом.
        _trayIcon.RefreshRequested += () => FireAndForget(async () =>
        {
            if (TrayAccount() is { } account) await RefreshAccountAsync(account);
            await _statusStore!.LoadAsync();
        });
        _trayIcon.SignInRequested += () => FireAndForget(async () =>
        {
            WidgetLog.Write(TrayAccount()?.Profile.DisplayName ?? "-", "sign-in-requested", "source=tray");
            if (TrayAccount() is { } account) await account.Session.OpenLoginWindowAsync();
        });
        _trayIcon.TrayMetricSelected += OnTrayMetricSelected;
        _trayIcon.ModelBucketSelected += OnModelBucketSelected;
        _trayIcon.ShowOnDesktopToggled += OnShowOnDesktopToggled;
        _trayIcon.TaskbarBandToggled += OnTaskbarBandToggled;
        _trayIcon.BandPositionSelected += OnBandPositionSelected;
        _trayIcon.LockPositionToggled += OnLockPositionToggled;
        _trayIcon.MenuOpening += RefreshTrayMenuState;
        _trayIcon.AccountSelected += OnAccountSelected;

        _trayIcon.EditLayoutToggled += () => SetEditingLayout(!_editingLayout);
        _trayIcon.AccountAddRequested += OnAccountAddRequested;
        _trayIcon.AccountRenameRequested += OnAccountRenameRequested;
        _trayIcon.AccountRemoveRequested += id => FireAndForget(() => OnAccountRemoveRequestedAsync(id));

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageWidget", "settings.json");
        _settings = new SettingsStore(settingsPath);

        BuildAccounts();

        _statusStore = new StatusStore(new StatusApi().FetchAsync);
        _statusStore.Changed += OnStoresChanged;

        _widgetWindow = new DesktopWidgetWindow(_settings);
        _widgetWindow.HideRequested += OnWidgetHideRequested;
        // The same handler the tray's Layout menu uses: two save paths for one
        // record is how they drift.
        _widgetWindow.LayoutEdited += OnLayoutSelected;
        // The panel's Done button and the tray's Edit layout are one toggle,
        // through the one method that keeps the flag, the tick and the mode
        // agreeing.
        _widgetWindow.EditDoneRequested += () => SetEditingLayout(false);
        _widgetWindow.StatusModeSelected += OnStatusModeSelected;
        _widgetWindow.ModelDialSelected += OnModelDialSelected;
        _widgetWindow.PlanLineSelected += OnPlanLineSelected;
        _widgetWindow.SignInRequested += () => FireAndForget(async () =>
        {
            // The user reported the panel's Sign in button "does nothing"
            // (2026-09-04) with no login-window line in the log: this line says
            // whether the click even arrives.
            WidgetLog.Write(TrayAccount()?.Profile.DisplayName ?? "-", "sign-in-requested", "source=panel");
            if (TrayAccount() is { } account) await account.Session.OpenLoginWindowAsync();
        });

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
        // Окно логина открываем только для трей-аккаунта, даже если куки нет у
        // нескольких: четыре окна логина разом на старте — это не помощь, а
        // засада. Остальные покажут пустые циферблаты и войдут по клику из
        // подменю Accounts.
        var account = TrayAccount();
        if (account is null) return;

        var hasSession = false;
        try
        {
            hasSession = await account.Session.HasSessionCookieAsync();
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
                await account.Session.OpenLoginWindowAsync();
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
        var accounts = _accounts;

        // Аккаунты разносятся внутри пятиминутного цикла, а не бьют залпом:
        // PollSchedule.OffsetFor. Ждём только статус сервиса — он общий, и
        // задерживать его на 225 секунд ради последнего аккаунта незачем.
        for (var i = 0; i < accounts.Count; i++)
        {
            var account = accounts[i];
            var offset = PollSchedule.OffsetFor(i, accounts.Count);
            if (offset == TimeSpan.Zero)
            {
                await RefreshAccountAsync(account);
                continue;
            }

            // Продолжение обязано вернуться на UI-поток: и WebView2, и рендер
            // по Changed живут на Dispatcher'е.
            _ = Task.Delay(offset).ContinueWith(
                _ =>
                {
                    // The account may have been removed while this waited: its
                    // profile is already cleared, and a poll now would only log a
                    // sign-in failure for a row that no longer exists.
                    if (!_accounts.Contains(account)) return;
                    FireAndForget(() => RefreshAccountAsync(account));
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        await _statusStore!.LoadAsync();
    }

    private async Task RefreshAccountAsync(AccountRuntime account)
    {
        var id = account.Profile.Id;
        try
        {
            _hasSessionCookie[id] = await account.Session.HasSessionCookieAsync();
            _webViewFailing[id] = false;
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
            _hasSessionCookie[id] = true;
            _webViewFailing[id] = true;
            System.Diagnostics.Debug.WriteLine($"WebView2 session check failed for '{id}': {ex}");
        }

        await account.Store.LoadAsync();
        ExportUsage(account);
    }

    /// A side channel for another tool, off by default.
    ///
    /// Two ways to switch it on, and their order matters: an account's
    /// ExportPath is a targeted override and always wins; ExportDirectory in
    /// the settings turns the export on for ALL accounts at once, each into
    /// its own file. The reader is another tool, and it wants every account, not
    /// just the one that once had a path written in by hand.
    private void ExportUsage(AccountRuntime account)
    {
        var data = _settings!.Load();

        // ExportPath comes from the freshly loaded settings, not from
        // account.Profile: it is set by hand-editing settings.json, and such
        // an edit must take effect on the next refresh, not after a restart.
        var path = data.Accounts.FirstOrDefault(a => a.Id == account.Profile.Id)?.ExportPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (string.IsNullOrWhiteSpace(data.ExportDirectory)) return;

            // The file name comes from DisplayName, with the id only as a
            // fallback: the reader wants `personal.widget.json`, not a GUID.
            // The profile here is current — ReloadAccountProfiles pulls a
            // rename into the live AccountRuntime.
            path = Path.Combine(
                data.ExportDirectory,
                UsageExport.FileNameFor(account.Profile.DisplayName, account.Profile.Id) + ".widget.json");
        }

        // Только по свежему успешному ответу: перезаписывать файл последним
        // хорошим снимком значило бы обновлять его "возраст", ничего не узнав.
        if (account.Store.CurrentState is not UsageState.Ok(var snapshot, _)) return;
        // ModelBucket is the same choice the third dial shows; otherwise the
        // file and the panel would call two different limits by one name.
        if (UsageExport.Payload(snapshot, DateTimeOffset.Now, account.Profile.DisplayName, data.ModelBucket)
            is not { } payload) return;

        UsageExportWriter.Write(path, payload);
    }

    private void OnSignedIn(string accountId)
    {
        // Логин мог пройти под другим аккаунтом — сохранённый id организации
        // от предыдущей сессии не должен пережить новый вход. Порт
        // fetchUsage's onSignedIn-эквивалента в applicationDidFinishLaunching.
        var account = _accounts.FirstOrDefault(a => a.Profile.Id == accountId);
        if (account is null) return;

        account.Session.ClearCachedOrganization();
        // Только этот аккаунт: вход в один не повод дёргать claude.ai за
        // остальные три.
        FireAndForget(() => RefreshAccountAsync(account));
    }

    private void OnWidgetHideRequested()
    {
        // Сам DesktopWidgetWindow уже спрятал себя и сохранил
        // WidgetVisible=false в OnEyeClicked до того, как поднял это
        // событие — здесь заново делать нечего, кроме как подтянуть в меню
        // трея снятую галочку "Show on desktop": глаз на панели меняет то же
        // самое состояние, что и пункт меню, и они обязаны показывать одно и
        // то же, даже если это меню сейчас не открыто.
        SetEditingLayout(false);
    }

    /// The flag, the tray's tick and the panel's mode, together. Four callers
    /// need this — the tray item, the toolbar's Done and Hide buttons, and
    /// switching the widget off from the tray — and a mode left on behind a
    /// hidden widget is a ticked item nobody can act on.
    private void SetEditingLayout(bool editing)
    {
        _editingLayout = editing;
        _trayIcon!.EditingLayout = editing;
        if (_widgetWindow is not null) _widgetWindow.EditMode = editing;
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
        var account = TrayAccount();
        if (account is null) return;

        var data = _settings!.Load();
        var models = DialModel.All(account.Snapshot, data.ModelBucket, DateTimeOffset.Now);
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
        // Список модельных бакетов берём у трей-аккаунта: выбор "Model limit"
        // один на виджет, и предлагать в нём то, чего у показываемого аккаунта
        // нет, было бы враньём.
        var snapshot = TrayAccount()?.Snapshot;
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
            resolvedModelLabel,
            data.Accounts,
            data.TrayAccountId));
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

        if (!visible) SetEditingLayout(false); else RefreshTrayMenuState();
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

    /// <summary>Fresh data for the band — one BandText entry per account, from
    /// the same AccountRow list the panel draws. Harmless to call when the band
    /// is switched off or not created yet.</summary>
    private void RenderTaskbarBand()
    {
        if (_bandWindow is null) return;

        var data = _settings!.Load();
        var rows = AccountRow.ForAll(
            data.Accounts, SnapshotsById(), data.ModelBucket, DateTimeOffset.Now);

        _bandWindow.Render(BandText.Entries(rows));
    }

    private void RenderWidget()
    {
        var account = TrayAccount();
        if (account is null) return;

        var data = _settings!.Load();
        var rows = AccountRow.ForAll(
            data.Accounts, SnapshotsById(), data.ModelBucket, DateTimeOffset.Now);

        _widgetWindow!.Render(
            rows,
            account.Store.CurrentState,
            _statusStore!.Status,
            account.Store.RetryPausedUntil);
    }

    private void UpdateTrayTooltip()
    {
        var account = TrayAccount();
        if (account is null) return;

        if (_webViewFailing.TryGetValue(account.Profile.Id, out var failing) && failing)
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
        var state = account.Store.CurrentState;
        var data = _settings!.Load();
        var models = DialModel.All(account.Snapshot, data.ModelBucket, now);
        var metrics = TrayText.Metrics(models);
        var statusLine = StatusLine.Text(state, now, account.Store.RetryPausedUntil);

        // "5H 42% · 7D 18% · FAB 8%" — тот же разделитель " · ", что и в
        // строке статуса под циферблатами на панели; статус (если есть)
        // идёт отдельной строкой, а не тем же " · ", чтобы не сливаться с
        // цифрами при беглом взгляде на всплывающую подсказку.
        var metricsText = metrics.Count == 0
            ? "Claude Usage Widget"
            : "Claude Usage Widget — " + string.Join(" · ", metrics.Select(m => $"{m.Label} {m.Value}"));

        _trayIcon!.SetTooltip(statusLine is null ? metricsText : $"{metricsText}\n{statusLine}");
    }

    /// Пауза после 429 — персональная для аккаунта: общая заморозила бы опрос
    /// остальных трёх из-за одного упершегося.
    private UsageRetryState? LoadRetryState(string accountId)
    {
        var account = _settings!.Load().Account(accountId);
        return account?.RetryPausedUntil is { } until
            ? new UsageRetryState(until, account.ConsecutiveRateLimits)
            : null;
    }

    private void SaveRetryState(string accountId, UsageRetryState? state)
    {
        _settings!.Save(_settings.Load().WithAccount(accountId, a => a with
        {
            RetryPausedUntil = state?.Until,
            ConsecutiveRateLimits = state?.ConsecutiveRateLimits ?? 0,
        }));
    }

    /// Собирает список аккаунтов из настроек. На самом первом запуске файла
    /// нет вовсе, миграция не срабатывает, и список пуст — тогда заводим один
    /// аккаунт, чтобы виджету было что показывать и куда логиниться.
    private void BuildAccounts()
    {
        var data = _settings!.Load();
        if (data.Accounts.Count == 0)
        {
            data = data with
            {
                Accounts = [new AccountProfile(SettingsMigration.LegacyAccountId, "Claude", null, null, 0)],
                TrayAccountId = SettingsMigration.LegacyAccountId,
            };
            _settings.Save(data);
        }

        _accounts = data.Accounts.Select(CreateAccountRuntime).ToList();
    }

    private AccountRuntime CreateAccountRuntime(AccountProfile profile)
    {
        // Мигрированный (он же первый) аккаунт остаётся на НЕЯВНОМ профиле
        // WebView2 — там лежит вход, сделанный до мультиаккаунта, и именем
        // тот профиль не адресуется. Остальные аккаунты получают именованный
        // профиль по своему id. Подробности и измерение — в комментарии к
        // ClaudeWebSession._profileName.
        var profileName = profile.Id == SettingsMigration.LegacyAccountId ? null : profile.Id;
        var session = new ClaudeWebSession(profileName, profile.Id, profile.DisplayName, _settings!);
        session.SignedIn += () => OnSignedIn(profile.Id);

        var store = new UsageStore(
            fetch: session.FetchUsageAsync,
            hasCredentials: () => _hasSessionCookie.TryGetValue(profile.Id, out var has) && has,
            now: () => DateTimeOffset.Now,
            loadRetryState: () => LoadRetryState(profile.Id),
            saveRetryState: state => SaveRetryState(profile.Id, state));
        store.Changed += OnStoresChanged;

        return new AccountRuntime(profile, session, store);
    }

    /// Аккаунт, к которому привязаны трей-иконка, её тултип и «Refresh now».
    /// Null только когда аккаунтов нет вообще.
    private AccountRuntime? TrayAccount()
    {
        var id = _settings!.Load().TrayAccountId;
        return _accounts.FirstOrDefault(a => a.Profile.Id == id) ?? _accounts.FirstOrDefault();
    }

    private IReadOnlyDictionary<string, UsageSnapshot?> SnapshotsById() =>
        _accounts.ToDictionary(a => a.Profile.Id, a => a.Snapshot);

    private void OnLayoutSelected(WidgetLayout layout)
    {
        var data = _settings!.Load();
        // Both settings are written back on every save, so a file that predated
        // either stops being ambiguous after the first edit.
        var mode = StatusModes.Resolve(data.StatusMode, data.Layout);
        var modelDial = ModelDials.Resolve(data.ModelDial);
        _settings.Save(data with
        {
            Layout = WidgetLayout.Sanitize(layout, mode, modelDial),
            StatusMode = mode,
            ModelDial = modelDial,
        });

        // Смена раскладки меняет и размер панели, и её сетку — окно
        // пересобирается целиком, а не перерисовывается.
        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    /// The status is one decision. Saving the mode and re-sanitizing the layout
    /// with it is what adds or removes the cell — there is no second switch that
    /// could disagree.
    private void OnStatusModeSelected(StatusMode mode)
    {
        var data = _settings!.Load();
        var modelDial = ModelDials.Resolve(data.ModelDial);
        _settings.Save(data with
        {
            StatusMode = mode,
            ModelDial = modelDial,
            Layout = WidgetLayout.Sanitize(data.Layout, mode, modelDial),
        });

        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    /// The model dial is the same one decision in one place: the setting is
    /// saved and the layout re-sanitized around it, so the cell cannot disagree
    /// with the switch. Only the CELL goes — the tray tooltip and the taskbar
    /// band read DialModel.All and keep reporting the percentage.
    private void OnModelDialSelected(ModelDial dial)
    {
        var data = _settings!.Load();
        var mode = StatusModes.Resolve(data.StatusMode, data.Layout);
        _settings.Save(data with
        {
            ModelDial = dial,
            StatusMode = mode,
            Layout = WidgetLayout.Sanitize(data.Layout, mode, dial),
        });

        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    /// The plan line, on the same one-place path as the two above minus the
    /// Sanitize: the plan is a line under the account name, never a cell, so
    /// nothing in `Order` follows it and there is no second switch to keep in
    /// agreement. RebuildLayout is still needed — the panel is SIZED for the
    /// line, so switching it changes the geometry and not only the paint.
    private void OnPlanLineSelected(PlanLine line)
    {
        _settings!.Save(_settings.Load() with { PlanLine = line });

        _widgetWindow!.RebuildLayout(_accounts.Count);
        RenderWidget();
        RefreshTrayMenuState();
    }

    private void OnAccountSelected(string accountId)
    {
        _settings!.Save(_settings.Load() with { TrayAccountId = accountId });
        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();
    }

    private void OnAccountAddRequested()
    {
        var data = _settings!.Load();
        if (data.Accounts.Count >= AccountLimits.Max) return;

        // Guid — внутри алфавита ProfileName и заведомо не совпадёт с
        // legacy-id "default", который обязан остаться на неявном профиле.
        var profile = new AccountProfile(Guid.NewGuid().ToString(), "New account", null, null, 0);
        _settings.Save(data with { Accounts = [.. data.Accounts, profile] });

        var runtime = CreateAccountRuntime(profile);
        _accounts = [.. _accounts, runtime];
        RenderWidget();
        RefreshTrayMenuState();
        FireAndForget(() => runtime.Session.OpenLoginWindowAsync());
    }

    private void OnAccountRenameRequested(string accountId)
    {
        var data = _settings!.Load();
        var account = data.Account(accountId);
        if (account is null) return;

        var name = RenameWindow.Ask(account.DisplayName);
        if (string.IsNullOrWhiteSpace(name)) return;

        _settings.Save(data.WithAccount(accountId, a => a with { DisplayName = name.Trim() }));
        ReloadAccountProfiles();
        RenderWidget();
        RefreshTrayMenuState();
    }

    private async Task OnAccountRemoveRequestedAsync(string accountId)
    {
        var runtime = _accounts.FirstOrDefault(a => a.Profile.Id == accountId);
        if (runtime is null) return;

        // Чистим ДО удаления из настроек: после удаления уже нечему знать,
        // какой профиль стирать, и следующий аккаунт с тем же id унаследовал бы
        // чужую сессию.
        try
        {
            await runtime.Session.ClearBrowsingDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clearing browsing data for '{accountId}' failed: {ex}");
        }

        var data = _settings!.Load();
        _settings.Save(data with
        {
            Accounts = data.Accounts.Where(a => a.Id != accountId).ToList(),
            TrayAccountId = data.TrayAccountId == accountId ? null : data.TrayAccountId,
        });
        _accounts = _accounts.Where(a => a.Profile.Id != accountId).ToList();

        RenderWidget();
        UpdateTrayTooltip();
        RefreshTrayIcon();
        RefreshTrayMenuState();
    }

    /// Подтягивает изменившиеся поля профиля (пока только DisplayName) в уже
    /// живые AccountRuntime, не пересоздавая сессии и не теряя загруженные
    /// снимки.
    private void ReloadAccountProfiles()
    {
        var byId = _settings!.Load().Accounts.ToDictionary(a => a.Id);
        _accounts = _accounts
            .Where(a => byId.ContainsKey(a.Profile.Id))
            .Select(a => a with { Profile = byId[a.Profile.Id] })
            .ToList();
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
            WidgetLog.Write("-", "unhandled", $"fire-and-forget {ex.GetType().Name}: {ex.Message}");
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

