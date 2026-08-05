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
    bool TaskbarBandEnabled);

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
    private ToolStripMenuItem _lockPositionItem = null!;
    private ToolStripMenuItem _launchAtLoginItem = null!;

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

    /// <summary>"Lock position" — новое желаемое состояние.</summary>
    public event Action<bool>? LockPositionToggled;

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
        var metricIndex = MetricIndex(state.TrayMetricKey);
        _trayShowsSessionItem.Checked = metricIndex == 0;
        _trayShowsWeekItem.Checked = metricIndex == 1;
        _trayShowsModelItem.Checked = metricIndex == 2;

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
        _lockPositionItem.Checked = state.PositionLocked;

        // Launch at login не приходит через TrayMenuState: это не
        // JSON-настройка, а сам реестр — источник истины уже под рукой.
        _launchAtLoginItem.Checked = Autostart.IsEnabled();
    }

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
        menu.Items.Add("Refresh now", null, (_, _) => RefreshRequested?.Invoke());
        menu.Items.Add("Sign in to Claude.ai…", null, (_, _) => SignInRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());

        var trayShowsMenu = new ToolStripMenuItem("Tray shows");
        _trayShowsSessionItem = new ToolStripMenuItem("SESSION", null, (_, _) => TrayMetricSelected?.Invoke("five_hour"));
        _trayShowsWeekItem = new ToolStripMenuItem("WEEK", null, (_, _) => TrayMetricSelected?.Invoke("seven_day"));
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

        _showOnDesktopItem = new ToolStripMenuItem("Show on desktop");
        _showOnDesktopItem.Click += (_, _) => ShowOnDesktopToggled?.Invoke(!_showOnDesktopItem.Checked);
        menu.Items.Add(_showOnDesktopItem);

        _taskbarBandItem = new ToolStripMenuItem("Taskbar band");
        _taskbarBandItem.Click += (_, _) => TaskbarBandToggled?.Invoke(!_taskbarBandItem.Checked);
        menu.Items.Add(_taskbarBandItem);

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

        return menu;
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
