using System.Drawing;
using System.Windows.Forms;
using ClaudeUsageWidget.App.Windows;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// Keeps the open tray menu inside the work area and on top of the taskbar.
///
/// NotifyIcon opens its menu through ContextMenuStrip.ShowInTaskbar, which
/// puts the menu's bottom edge AT the cursor — on the taskbar — and
/// constrains it to the screen bounds, not the work area (WinForms 8.0
/// source: it switches WorkingAreaConstrained off first). The rows below the
/// taskbar's top edge then share pixels with the taskbar, and the taskbar,
/// itself topmost and just clicked, can be drawn over them. Lifting the menu
/// onto the work area removes the overlap, whatever the z-order; the
/// HWND_TOPMOST re-assertion covers the moment between show and move.
/// </summary>
public static class MenuPlacement
{
    /// The smallest move that puts <paramref name="menu"/> inside
    /// <paramref name="workArea"/>, size unchanged. A menu taller (wider) than
    /// the area keeps its top (left) edge in it — the first rows are the ones
    /// a user reads.
    public static Rectangle Within(Rectangle menu, Rectangle workArea)
    {
        var x = Math.Max(workArea.Left, Math.Min(menu.X, workArea.Right - menu.Width));
        var y = Math.Max(workArea.Top, Math.Min(menu.Y, workArea.Bottom - menu.Height));
        return new Rectangle(x, y, menu.Width, menu.Height);
    }

    /// Once per menu. Opened covers every way the menu opens (right-click,
    /// keyboard on the tray icon); SizeChanged covers a menu that re-lays out
    /// while open (the store refreshes the model items), which WinForms grows
    /// downward from a fixed top-left corner — back into the taskbar.
    public static void Attach(ToolStripDropDown menu)
    {
        menu.Opened += (_, _) => Place(menu);
        menu.SizeChanged += (_, _) =>
        {
            if (menu.Visible) Place(menu);
        };
    }

    private static void Place(ToolStripDropDown menu)
    {
        // Off every screen on purpose: CUW_RENDER_MENU shows it at -31000 to
        // photograph it. A real tray menu never is — ShowInTaskbar clamps it
        // to the screen.
        var screen = Screen.FromRectangle(menu.Bounds);
        if (!screen.Bounds.IntersectsWith(menu.Bounds)) return;

        var target = Within(menu.Bounds, screen.WorkingArea);
        if (target.Location != menu.Location) menu.Location = target.Location;

        Win32.SetWindowPos(menu.Handle, Win32.HwndTopMost, 0, 0, 0, 0,
            Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);
    }
}
