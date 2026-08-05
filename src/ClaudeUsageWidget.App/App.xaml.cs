using System.IO;
using System.Windows;
using ClaudeUsageWidget.App.Tray;
using ClaudeUsageWidget.App.Windows;
using ClaudeUsageWidget.Core;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Без окна и MainWindow сборка живёт, пока жив трей-объект и
        // ShutdownMode остаётся OnExplicitShutdown (см. App.xaml) — иначе WPF
        // закрыл бы процесс сразу после OnStartup, не дождавшись Quit.
        _trayIcon = new TrayIcon();
        _trayIcon.QuitRequested += Shutdown;
        // Refresh/SignIn подключатся к реальным сторам в Task 15/17 — пока
        // пункты меню кликабельны, но ничего не делают.

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageWidget", "settings.json");
        var settings = new SettingsStore(settingsPath);

        _widgetWindow = new DesktopWidgetWindow(settings);

        // === ВРЕМЕННЫЕ ФЕЙКОВЫЕ ДАННЫЕ (Task 14) ===
        // Настоящего стора и веб-сессии ещё нет — они появляются в Task 15.
        // Здесь только чтобы Render() было чем накормить и панель можно было
        // увидеть на экране для ручной проверки (task-14-brief.md, шаг 5).
        // Payload — тот же, что и в Tests/ClaudeUsageWidget.Core.Tests/UsageDecoderTests.cs.
        // TODO(Task 15): убрать этот блок целиком, подключить реальный UsageStore/StatusStore.
        const string fakeUsagePayload = """
        {
          "five_hour":            { "utilization": 42,   "resets_at": "2026-07-29T18:00:00Z" },
          "seven_day":            { "utilization": 17.5, "resets_at": "2026-08-02T00:00:00Z" },
          "seven_day_opus":       { "utilization": 3,    "resets_at": "2026-08-02T00:00:00Z" },
          "seven_day_oauth_apps": { "utilization": 0,    "resets_at": null },
          "currency":             "EUR"
        }
        """;
        var fakeSnapshot = UsageDecoder.Snapshot(fakeUsagePayload);
        var fakeState = new UsageState.Ok(fakeSnapshot, DateTimeOffset.Now);
        _widgetWindow.Render(fakeState, last: null, ServiceStatus.Operational, preferredModelKey: null, retryUntil: null);
        // === КОНЕЦ ВРЕМЕННЫХ ФЕЙКОВЫХ ДАННЫХ ===

        _widgetWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // NotifyIcon переживает процесс визуально до следующего движения
        // мыши, если его не спрятать явно — Dispose убирает иконку сразу.
        _trayIcon?.Dispose();
        _trayIcon = null;

        _widgetWindow?.Close();
        _widgetWindow = null;

        base.OnExit(e);
    }
}

