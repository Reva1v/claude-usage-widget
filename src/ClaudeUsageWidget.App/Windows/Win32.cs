using System.Runtime.InteropServices;

namespace ClaudeUsageWidget.App.Windows;

/// <summary>
/// P/Invoke для <see cref="TaskbarBandWindow"/>: поиск таскбара/области
/// трея, смена родителя окна и его позиционирование. Отдельный класс от
/// <c>DesktopWidgetWindow.NativeMethods</c> (Task 14) — тот занят только
/// z-order/no-activate ТОГО окна на рабочем столе; здесь совсем другой набор
/// вызовов, а общих сигнатур (Get/SetWindowLongPtr) ровно две — не стоило
/// тащить туда чужой контекст ради такой мелочи (task-17-brief.md: "move
/// existing declarations there ONLY if trivially safe").
/// </summary>
internal static class Win32
{
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;

    public const long WsChild = 0x40000000L;
    public const long WsPopup = unchecked((long)0x80000000);

    public const long WsExNoActivate = 0x08000000L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExLayered = 0x00080000L;

    public const uint LwaAlpha = 0x2;

    /// HWND_TOP — вставить в начало Z-порядка среди детей текущего родителя.
    /// Значение 0 совпадает с nint.Zero, который раньше передавался вместе с
    /// SWP_NOZORDER (где он игнорируется) — здесь используется осознанно,
    /// SWP_NOZORDER не выставляется.
    public static readonly nint HwndTop = 0;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;

    public const int WmDpiChanged = 0x02E0;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowEx(nint hwndParent, nint hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsWindow(nint hWnd);

    /// Доступна с Windows 10 1607 (Anniversary Update) — минимальная
    /// поддерживаемая версия здесь и так Windows 10/11, отдельная проверка
    /// версии не нужна.
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);
}
