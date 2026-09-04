using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// Everything the tray menu needs to reflect current app state, gathered by
/// App.xaml.cs and handed to <see cref="TrayIcon.SyncMenuState"/> — on
/// startup and again every time the menu is about to open (<see
/// cref="TrayIcon.MenuOpening"/>), so checkmarks stay correct even when the
/// underlying setting changed from somewhere other than this menu (e.g. the
/// widget's own lock toggle or its eye button).
/// </summary>
public sealed record TrayMenuState(
    string TrayMetricKey,
    IReadOnlyList<string> AvailableModelBuckets,
    string? SelectedModelBucket,
    bool ShowOnDesktop,
    bool PositionLocked,
    bool TaskbarBandEnabled,
    string BandPosition,
    string? ResolvedModelLabel,
    IReadOnlyList<AccountProfile> Accounts,
    string? TrayAccountId);

/// <summary>
/// Иконка приложения в системном трее и её контекстное меню.
/// </summary>
///
/// WPF не имеет своего трея — оборачиваем WinForms <see cref="NotifyIcon"/>,
/// как это обычно делают в портах меню-бар-приложений на Windows.
public sealed class TrayIcon : IDisposable
{
    private const string RepoUrl = "https://github.com/Reva1v/claude-usage-widget";
    private const string IssuesUrl = RepoUrl + "/issues";

    private readonly NotifyIcon _notifyIcon;
    private Icon? _currentIcon;
    private bool _disposed;

    private ToolStripMenuItem _trayShowsSessionItem = null!;
    private ToolStripMenuItem _trayShowsWeekItem = null!;
    private ToolStripMenuItem _trayShowsModelItem = null!;
    private ToolStripMenuItem _modelLimitMenu = null!;
    private ToolStripMenuItem _showOnDesktopItem = null!;
    private ToolStripMenuItem _taskbarBandItem = null!;
    private ToolStripMenuItem _bandPositionLeftItem = null!;
    private ToolStripMenuItem _bandPositionTrayItem = null!;
    private ToolStripMenuItem _lockPositionItem = null!;
    private ToolStripMenuItem _launchAtLoginItem = null!;
    private ToolStripMenuItem _accountsMenu = null!;
    private ToolStripMenuItem _layoutMenu = null!;

    /// <summary>Пункт меню "Refresh now".</summary>
    public event Action? RefreshRequested;

    /// <summary>Пункт меню "Sign in to Claude.ai…".</summary>
    public event Action? SignInRequested;

    /// <summary>Пункт меню "Quit Claude Usage Widget".</summary>
    public event Action? QuitRequested;

    /// <summary>"Tray shows" — новое значение TrayMetricKey ("five_hour"/"seven_day"/"model").</summary>
    public event Action<string>? TrayMetricSelected;

    /// <summary>"Model limit" — выбранный ключ бакета, либо null для "Auto".</summary>
    public event Action<string?>? ModelBucketSelected;

    /// <summary>"Show on desktop" — новое желаемое состояние.</summary>
    public event Action<bool>? ShowOnDesktopToggled;

    /// <summary>"Taskbar band" — новое желаемое состояние.</summary>
    public event Action<bool>? TaskbarBandToggled;

    /// <summary>"Band position" — новое значение ("tray"/"left").</summary>
    public event Action<string>? BandPositionSelected;

    /// <summary>"Lock position" — новое желаемое состояние.</summary>
    public event Action<bool>? LockPositionToggled;

    /// <summary>Accounts — выбран аккаунт для трей-иконки.</summary>
    public event Action<string>? AccountSelected;

    /// <summary>Accounts — "Add account…".</summary>
    public event Action? AccountAddRequested;

    /// <summary>Accounts — "Rename…" для этого id.</summary>
    public event Action<string>? AccountRenameRequested;

    /// <summary>Accounts — "Sign out and remove" для этого id.</summary>
    public event Action<string>? AccountRemoveRequested;

    /// Raised by the Layout submenu's edit item. The App owns the flag; the
    /// tray only asks for it to be flipped.
    public event Action? EditLayoutToggled;

    /// What the tick beside `Edit layout…` should show. Set before the menu is
    /// opened, like the rest of the Layout submenu's state.
    public bool EditingLayout { get; set; }

    /// <summary>
    /// Меню вот-вот откроется — момент подтянуть свежее состояние
    /// (SettingsStore, PositionLocked виджета, доступные model-бакеты) через
    /// <see cref="SyncMenuState"/>, не дожидаясь следующего Changed стора.
    /// </summary>
    public event Action? MenuOpening;

    /// <summary>
    /// Открытое меню — последующие задачи (org picker) дописывают в него свои
    /// пункты, не пересоздавая TrayIcon целиком.
    /// </summary>
    public ContextMenuStrip Menu { get; }

    public TrayIcon()
    {
        Menu = BuildMenu();
        Menu.Opening += (_, _) => MenuOpening?.Invoke();

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = Menu,
            Visible = true,
        };

        SetIcon(TrayIconRenderer.Render(null));
        SetTooltip("Claude Usage Widget");
    }

    public void SetIcon(Icon icon)
    {
        var previous = _currentIcon;
        _notifyIcon.Icon = icon;
        _currentIcon = icon;

        if (previous is null) return;

        // NotifyIcon.Icon = ... не забирает владение хэндлом; иконка,
        // созданная из HICON через Icon.FromHandle, требует ручного
        // DestroyIcon — иначе каждая замена (живая цифра, TrayIconRenderer)
        // течёт в GDI-квоту процесса до её исчерпания.
        var handle = previous.Handle;
        previous.Dispose();
        NativeMethods.DestroyIcon(handle);
    }

    public void SetTooltip(string text)
    {
        // NOTIFYICONDATA.szTip вмещает 128 символов включая завершающий NUL;
        // WinForms бросает ArgumentOutOfRangeException при 128+, отсюда 127.
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>Синхронизирует динамическую часть меню (чекбоксы, «Model
    /// limit», «Tray shows») с текущим состоянием настроек/виджета. Вызывать
    /// на старте и на каждый <see cref="MenuOpening"/> — источник истины
    /// живёт в App.xaml.cs (SettingsStore/DesktopWidgetWindow), не здесь.</summary>
    public void SyncMenuState(TrayMenuState state)
    {
        // MetricIndex, не точечное сравнение с "five_hour": та же функция,
        // что и App.xaml.cs.RefreshTrayIcon использует для самой цифры —
        // нераспознанный/битый TrayMetricKey (например, из вручную
        // отредактированного settings.json) обязан читаться в меню как
        // SESSION ровно потому же правилу, по которому иконка в этом случае
        // рисует SESSION, а не оставлять все три чекбокса пустыми.
        SyncAccountsMenu(state.Accounts, state.TrayAccountId);
        SyncLayoutMenu(state.ShowOnDesktop);

        var metricIndex = MetricIndex(state.TrayMetricKey);
        _trayShowsSessionItem.Checked = metricIndex == 0;
        _trayShowsWeekItem.Checked = metricIndex == 1;
        _trayShowsModelItem.Checked = metricIndex == 2;

        // "MODEL (Fable)" once we know which bucket actually resolved (same
        // resolution DialModel.All uses for the third dial's own title) —
        // plain "MODEL" before any snapshot has loaded. ModelBuckets.Label
        // comes back SHOUTY ("FABLE"); title-case it for the menu specifically
        // — the widget's own dial keeps the shouty version, this is purely a
        // menu-text nicety.
        _trayShowsModelItem.Text = string.IsNullOrEmpty(state.ResolvedModelLabel)
            ? "MODEL"
            : $"MODEL ({TitleCase(state.ResolvedModelLabel)})";

        // ModelBucketPicker.swift:145-163 — видно только когда есть из чего
        // выбирать; при 0/1 бакете выбор бессмысленен (Resolve и так возьмёт
        // единственный доступный).
        _modelLimitMenu.Visible = state.AvailableModelBuckets.Count > 1;
        _modelLimitMenu.DropDownItems.Clear();

        var autoItem = new ToolStripMenuItem("Auto", null, (_, _) => ModelBucketSelected?.Invoke(null))
        {
            Checked = string.IsNullOrEmpty(state.SelectedModelBucket),
        };
        _modelLimitMenu.DropDownItems.Add(autoItem);
        _modelLimitMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var key in state.AvailableModelBuckets)
        {
            var capturedKey = key; // не полагаемся на semantics захвата переменной цикла — на всякий случай локальная копия
            _modelLimitMenu.DropDownItems.Add(new ToolStripMenuItem(
                ModelBuckets.Label(capturedKey), null, (_, _) => ModelBucketSelected?.Invoke(capturedKey))
            {
                Checked = state.SelectedModelBucket == capturedKey,
            });
        }

        _showOnDesktopItem.Checked = state.ShowOnDesktop;
        _taskbarBandItem.Checked = state.TaskbarBandEnabled;
        _bandPositionLeftItem.Checked = state.BandPosition == "left";
        _bandPositionTrayItem.Checked = state.BandPosition != "left"; // "tray" and any unrecognized value default here, same fallback shape as MetricIndex
        _lockPositionItem.Checked = state.PositionLocked;

        // Launch at login не приходит через TrayMenuState: это не
        // JSON-настройка, а сам реестр — источник истины уже под рукой.
        _launchAtLoginItem.Checked = Autostart.IsEnabled();
    }

    /// "FABLE" -> "Fable" — ModelBuckets.Label is deliberately SHOUTY for the
    /// widget's own dial (Theme.LabelWeight etc. render everything caps
    /// anyway); the tray menu is normal UI chrome where a shouty parenthetical
    /// would look out of place next to "Auto"/"Left corner"/etc.
    private static string TitleCase(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>SESSION/WEEK/MODEL → индекс в DialModel.All, которое всегда
    /// возвращает ровно эти три циферблата в этом порядке. Публичный и общий
    /// с App.xaml.cs.RefreshTrayIcon: обе стороны обязаны сходиться в том,
    /// что означает нераспознанный/битый TrayMetricKey (SESSION), иначе
    /// чекбоксы меню и цифра в иконке способны разойтись.</summary>
    public static int MetricIndex(string trayMetricKey) => trayMetricKey switch
    {
        "seven_day" => 1,
        "model" => 2,
        _ => 0, // "five_hour" и любое нераспознанное значение — сессия по умолчанию
    };

    /// Пересобирается целиком на каждое открытие меню, а не мутируется:
    /// аккаунты добавляются и удаляются из этого же подменю, и пересборка —
    /// то, что не даёт галочке, списку и настройкам разъехаться.
    private void SyncAccountsMenu(IReadOnlyList<AccountProfile> accounts, string? trayAccountId)
    {
        foreach (var item in _accountsMenu.DropDownItems.OfType<ToolStripMenuItem>())
            item.DropDown.Closing -= CancelCloseOnItemClick;
        _accountsMenu.DropDownItems.Clear();

        // Подменю аккаунтов бессмысленно, пока аккаунт один и добавить второй
        // нельзя; но «добавить» нужно всегда, поэтому скрываем только сам
        // список, а не пункт целиком.
        foreach (var account in accounts)
        {
            var item = new ToolStripMenuItem(
                account.DisplayName, null, (_, _) => AccountSelected?.Invoke(account.Id))
            {
                Checked = account.Id == trayAccountId,
            };
            var id = account.Id;
            item.DropDownItems.Add("Rename…", null, (_, _) => AccountRenameRequested?.Invoke(id));
            item.DropDownItems.Add("Sign out and remove", null, (_, _) => AccountRemoveRequested?.Invoke(id));
            item.DropDown.Closing += CancelCloseOnItemClick;
            _accountsMenu.DropDownItems.Add(item);
        }

        if (accounts.Count > 0) _accountsMenu.DropDownItems.Add(new ToolStripSeparator());

        // Выключен, а не скрыт: пропавший пункт читается как баг, погашенный
        // с подсказкой — как предел.
        var atLimit = accounts.Count >= AccountLimits.Max;
        _accountsMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Add account…", null, (_, _) => AccountAddRequested?.Invoke())
        {
            Enabled = !atLimit,
            ToolTipText = atLimit ? $"The widget shows at most {AccountLimits.Max} accounts." : null,
        });
    }

    /// One item now. Every flow, the name placement and the status live on the
    /// panel's own toolbar — one editing surface rather than two that have to
    /// keep agreeing with each other.
    private void SyncLayoutMenu(bool widgetVisible)
    {
        _layoutMenu.DropDownItems.Clear();
        _layoutMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Edit layout…", null, (_, _) => EditLayoutToggled?.Invoke())
        {
            Checked = EditingLayout,
            // Disabled while the widget is hidden: a ticked item and an
            // invisible mode is the trap this closes.
            Enabled = widgetVisible,
            ToolTipText = widgetVisible
                ? "Toolbar on the panel: flows, name, status, lock, hide. Drag a cell onto another to swap them."
                : "Show the widget on the desktop first.",
        });
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Порядок и разделители — как в macOS-меню (ClaudeUsageWidgetApp.swift:33-58):
        // сначала GitHub/issues, затем refresh/sign-in, затем toggles, затем quit.
        // "Check for Updates…" не портирован (нет Sparkle-эквивалента в этом
        // таске); "Tray shows" — пункт без аналога в Swift, специфичный для
        // Windows-трея: там менюбар всегда рисует все три цифры разом,
        // здесь иконка вмещает только одну.
        menu.Items.Add($"Claude Usage Widget v{CoreInfo.Version} — GitHub", null, (_, _) => OpenUrl(RepoUrl));
        menu.Items.Add("Report an Issue", null, (_, _) => OpenUrl(IssuesUrl));
        menu.Items.Add(new ToolStripSeparator());
        // Refresh now — исключение из «меню не закрывается по клику» ниже:
        // это действие, а не переключатель, держать меню открытым после него
        // незачем. Явный Close() приходит с причиной CloseCalled, которую
        // CancelCloseOnItemClick пропускает.
        menu.Items.Add("Refresh now", null, (_, _) =>
        {
            RefreshRequested?.Invoke();
            menu.Close();
        });
        menu.Items.Add("Sign in to Claude.ai…", null, (_, _) => SignInRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());

        // Подписи короче ("5H"/"7D" вместо "SESSION"/"WEEK") — те же ключи
        // TrayMetricKey под капотом ("five_hour"/"seven_day"/"model"), меняется
        // только то, что видно в меню. "MODEL" здесь — заглушка на старте
        // (снапшот ещё не загружен); SyncMenuState дописывает разрешённый
        // бакет в скобках, как только он известен ("MODEL (Fable)").
        var trayShowsMenu = new ToolStripMenuItem("Tray shows");
        _trayShowsSessionItem = new ToolStripMenuItem("5H", null, (_, _) => TrayMetricSelected?.Invoke("five_hour"));
        _trayShowsWeekItem = new ToolStripMenuItem("7D", null, (_, _) => TrayMetricSelected?.Invoke("seven_day"));
        _trayShowsModelItem = new ToolStripMenuItem("MODEL", null, (_, _) => TrayMetricSelected?.Invoke("model"));
        trayShowsMenu.DropDownItems.Add(_trayShowsSessionItem);
        trayShowsMenu.DropDownItems.Add(_trayShowsWeekItem);
        trayShowsMenu.DropDownItems.Add(_trayShowsModelItem);
        menu.Items.Add(trayShowsMenu);

        // DropDownItems наполняются заново в SyncMenuState — на старте
        // доступных бакетов ещё нет (снапшот не загружен), поэтому здесь
        // пустое подменю, скрытое до первого SyncMenuState.
        _modelLimitMenu = new ToolStripMenuItem("Model limit") { Visible = false };
        menu.Items.Add(_modelLimitMenu);

        // Какой аккаунт описывает трей-иконка. Альтернативой были бы четыре
        // иконки, но Windows прячет их в переполнение по своему усмотрению.
        _accountsMenu = new ToolStripMenuItem("Accounts");
        menu.Items.Add(_accountsMenu);

        // One item: the panel's toolbar is the editing surface, and this is the
        // way into it.
        _layoutMenu = new ToolStripMenuItem("Layout");
        menu.Items.Add(_layoutMenu);

        _showOnDesktopItem = new ToolStripMenuItem("Show on desktop");
        _showOnDesktopItem.Click += (_, _) => ShowOnDesktopToggled?.Invoke(!_showOnDesktopItem.Checked);
        menu.Items.Add(_showOnDesktopItem);

        _taskbarBandItem = new ToolStripMenuItem("Taskbar band");
        _taskbarBandItem.Click += (_, _) => TaskbarBandToggled?.Invoke(!_taskbarBandItem.Checked);
        menu.Items.Add(_taskbarBandItem);

        // "Near tray"/"Left corner" — задаёт BandPosition независимо от того,
        // включена ли сама лента сейчас (тот же принцип, что и "Tray shows":
        // предпочтение сохраняется даже пока не на что смотреть).
        var bandPositionMenu = new ToolStripMenuItem("Band position");
        _bandPositionTrayItem = new ToolStripMenuItem("Near tray", null, (_, _) => BandPositionSelected?.Invoke("tray"));
        _bandPositionLeftItem = new ToolStripMenuItem("Left corner", null, (_, _) => BandPositionSelected?.Invoke("left"));
        bandPositionMenu.DropDownItems.Add(_bandPositionTrayItem);
        bandPositionMenu.DropDownItems.Add(_bandPositionLeftItem);
        menu.Items.Add(bandPositionMenu);

        _lockPositionItem = new ToolStripMenuItem("Lock position");
        _lockPositionItem.Click += (_, _) => LockPositionToggled?.Invoke(!_lockPositionItem.Checked);
        menu.Items.Add(_lockPositionItem);

        // Launch at login самодостаточен (порт LaunchAtLoginToggle,
        // ClaudeUsageWidgetApp.swift:168-188): в отличие от остальных
        // чекбоксов, у него нет соответствующего поля в WidgetSettingsData —
        // источник истины это сам реестр (Autostart), поэтому не нужен
        // круговой путь через App/SettingsStore, а откат при сбое проще
        // сделать на месте.
        _launchAtLoginItem = new ToolStripMenuItem("Launch at login");
        _launchAtLoginItem.Click += OnLaunchAtLoginClicked;
        menu.Items.Add(_launchAtLoginItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Claude Usage Widget", null, (_, _) => QuitRequested?.Invoke());

        // Меню не закрывается после клика по пункту — только явно (клик мимо
        // меню, Esc, потеря фокуса): почти все пункты здесь — переключатели,
        // и пользователь щёлкает несколько за одно открытие, а WinForms по
        // умолчанию схлопывает меню на первом же. Подписка нужна и корню, и
        // каждому подменю: клик по вложенному пункту закрывает всю цепочку
        // DropDown'ов, и каждое звено спрашивает о закрытии отдельно. Один
        // общий цикл в конце, а не строка у каждого подменю — новое подменю
        // подхватится само, без риска забыть подписку.
        menu.Closing += CancelCloseOnItemClick;
        foreach (var submenu in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (submenu.HasDropDownItems || submenu == _modelLimitMenu)
                submenu.DropDown.Closing += CancelCloseOnItemClick;
        }

        return menu;
    }

    /// ItemClicked — единственная причина закрытия, которую гасим; Quit это
    /// не задерживает (приложение гасит меню целиком через Dispose), а
    /// клавиатура и клик за пределами меню продолжают закрывать как обычно.
    private static void CancelCloseOnItemClick(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
    }

    private void OnLaunchAtLoginClicked(object? sender, EventArgs e)
    {
        var desired = !_launchAtLoginItem.Checked;
        try
        {
            Autostart.SetEnabled(desired);
            _launchAtLoginItem.Checked = desired;
        }
        catch (Exception ex)
        {
            // Порт catch-семантики LaunchAtLoginToggle (swift:174-185): при
            // сбое чекбокс не просто откатывается к старому значению, а
            // перечитывает реальное состояние реестра — SetEnabled мог
            // упасть на середине (например SetValue после успешного
            // OpenSubKey), и старое "было" не обязательно совпадает с тем,
            // что там сейчас.
            _launchAtLoginItem.Checked = Autostart.IsEnabled();
            Debug.WriteLine($"Failed to change launch-at-login: {ex}");
        }
    }

    private static void OpenUrl(string url)
    {
        // "-" for the account: the tray's links (repository, issues) are about
        // the widget itself, not about whichever account the tray shows.
        WidgetLog.Write("-", "browser-open", $"site=tray url={url}");

        // UseShellExecute: true — без него .NET пытается запустить URL как
        // исполняемый файл напрямую и падает с Win32Exception.
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Menu.Dispose();

        if (_currentIcon is null) return;

        var handle = _currentIcon.Handle;
        _currentIcon.Dispose();
        NativeMethods.DestroyIcon(handle);
        _currentIcon = null;
    }
}

internal static class NativeMethods
{
    // DllImport, а не LibraryImport: последний требует AllowUnsafeBlocks для
    // one-off P/Invoke не стоит того в проекте, где unsafe больше нигде не нужен.
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint handle);
}
