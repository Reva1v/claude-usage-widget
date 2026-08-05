using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.App.Tray;

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

    /// <summary>Пункт меню "Refresh now".</summary>
    public event Action? RefreshRequested;

    /// <summary>Пункт меню "Sign in to Claude.ai…".</summary>
    public event Action? SignInRequested;

    /// <summary>Пункт меню "Quit Claude Usage Widget".</summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Открытое меню — последующие задачи (org picker, toggles) дописывают
    /// в него свои пункты, не пересоздавая TrayIcon целиком.
    /// </summary>
    public ContextMenuStrip Menu { get; }

    public TrayIcon()
    {
        Menu = BuildMenu();

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = Menu,
            Visible = true,
        };

        SetIcon(CreateRingIcon());
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
        // DestroyIcon — иначе каждая замена (Task 14/16, живая цифра) течёт
        // в GDI-квоту процесса до её исчерпания.
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

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Порядок и разделители — как в macOS-меню (ClaudeUsageWidgetApp.swift:33-58),
        // за вычетом пунктов, которые ещё не портированы (Check for Updates,
        // model picker, toggles, launch at login — Task 14+).
        menu.Items.Add($"Claude Usage Widget v{CoreInfo.Version} — GitHub", null, (_, _) => OpenUrl(RepoUrl));
        menu.Items.Add("Report an Issue", null, (_, _) => OpenUrl(IssuesUrl));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, (_, _) => RefreshRequested?.Invoke());
        menu.Items.Add("Sign in to Claude.ai…", null, (_, _) => SignInRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Claude Usage Widget", null, (_, _) => QuitRequested?.Invoke());

        return menu;
    }

    private static void OpenUrl(string url)
    {
        // UseShellExecute: true — без него .NET пытается запустить URL как
        // исполняемый файл напрямую и падает с Win32Exception.
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    /// Стартовая иконка до первых реальных данных (кольцо, не цифра) — порт
    /// menuBarIcon из ClaudeUsageWidgetApp.swift:17-30. Там кольцо рисуется
    /// монохромным template-изображением, которое macOS сам тонирует под
    /// светлый/тёмный менюбар; у Win32-трея такого автотонирования нет, а
    /// подавляющее большинство трей-панелей тёмные — поэтому white, не black.
    /// </summary>
    private static Icon CreateRingIcon()
    {
        const int size = 16;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pen = new Pen(Color.White, 2f);
            const float inset = 2f;
            g.DrawEllipse(pen, inset, inset, size - inset * 2, size - inset * 2);
        }

        // Bitmap.GetHicon() выделяет новый HICON, который переживает bitmap —
        // владение переходит вызывающей стороне (см. DestroyIcon в SetIcon/Dispose).
        return Icon.FromHandle(bitmap.GetHicon());
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
