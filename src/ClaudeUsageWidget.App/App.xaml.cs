using System.Windows;
using ClaudeUsageWidget.App.Tray;

namespace ClaudeUsageWidget.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
// Полное имя обязательно: UseWindowsForms делает System.Windows.Forms.Application
// видимым в этом файле, и Application становится неоднозначной ссылкой.
public partial class App : System.Windows.Application
{
    private TrayIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Без окна и MainWindow сборка живёт, пока жив трей-объект и
        // ShutdownMode остаётся OnExplicitShutdown (см. App.xaml) — иначе WPF
        // закрыл бы процесс сразу после OnStartup, не дождавшись Quit.
        _trayIcon = new TrayIcon();
        _trayIcon.QuitRequested += Shutdown;
        // Refresh/SignIn подключатся к реальным сторам в Task 14/17 — пока
        // пункты меню кликабельны, но ничего не делают.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // NotifyIcon переживает процесс визуально до следующего движения
        // мыши, если его не спрятать явно — Dispose убирает иконку сразу.
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnExit(e);
    }
}

