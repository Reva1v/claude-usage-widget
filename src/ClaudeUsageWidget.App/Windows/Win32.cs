using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeUsageWidget.App.Windows;

/// <summary>
/// P/Invoke для <see cref="TaskbarBandWindow"/>: поиск таскбара/области
/// трея, установка окна-владельца (GWLP_HWNDPARENT) и позиционирование.
/// Отдельный класс от <c>DesktopWidgetWindow.NativeMethods</c> (Task 14) —
/// тот занят только z-order/no-activate ТОГО окна на рабочем столе; здесь
/// совсем другой набор вызовов, а общих сигнатур (Get/SetWindowLongPtr)
/// ровно две — не стоило тащить туда чужой контекст ради такой мелочи
/// (task-17-brief.md: "move existing declarations there ONLY if trivially
/// safe").
/// </summary>
internal static class Win32
{
    public const int GwlExStyle = -20;

    /// GWLP_HWNDPARENT — для top-level (не WS_CHILD) окна задаёт не
    /// родителя, а ВЛАДЕЛЬЦА: Windows держит owned-окно всегда выше его
    /// владельца в Z-порядке, но при этом окно остаётся обычным top-level —
    /// никакого child-композитинга (и связанного с ним Mica-глушения
    /// содержимого на таскбаре, см. doc-comment TaskbarBandWindow).
    public const int GwlpHwndParent = -8;

    public const long WsExNoActivate = 0x08000000L;
    public const long WsExToolWindow = 0x00000080L;

    /// HWND_TOPMOST — вставить в начало общего Z-порядка (не только среди
    /// детей/owned-окон конкретного родителя).
    public static readonly nint HwndTopMost = -1;

    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;

    public const int WmDpiChanged = 0x02E0;

    /// MONITOR_DEFAULTTONEAREST — MonitorFromWindow никогда не возвращает
    /// NULL с этим флагом (в отличие от MONITOR_DEFAULTTONULL), даже если
    /// окно временно не пересекает ни один монитор.
    public const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowEx(nint hwndParent, nint hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsWindow(nint hWnd);

    /// Доступна с Windows 10 1607 (Anniversary Update) — минимальная
    /// поддерживаемая версия здесь и так Windows 10/11, отдельная проверка
    /// версии не нужна.
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    // EntryPoint обязателен: имя метода здесь ("...Native") не совпадает с
    // именем настоящего экспорта, а без явного EntryPoint P/Invoke ищет в
    // DLL функцию с ИМЕНЕМ МЕТОДА ("GetClassNameNative"), а не с GetClassNameW
    // — падает EntryPointNotFoundException при первом же вызове.
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    /// Обёртка над GetClassNameW — раздельно объявлять буфер и вызывать
    /// P/Invoke на каждом месте использования было бы лишним дублированием
    /// ради единственного реального потребителя (фуллскрин-проверка ниже).
    public static string GetClassName(nint hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassNameNative(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    // EntryPoint явно, а не положились на автоматическое A/W-разрешение
    // (макрос GetMonitorInfo в самом деле разворачивается в GetMonitorInfoW
    // при Unicode, и CLR в теории находит её сам по CharSet — но именно на
    // соседнем GetClassNameNative выше эта неявность подвела: EntryPoint не
    // подставился автоматом, потому что имя C#-метода вообще не совпадало ни
    // с одним экспортом. Явный EntryPoint здесь дешёвая страховка от того же
    // класса ошибки).
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
}
