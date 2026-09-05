using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeUsageWidget.App.Windows;

/// <summary>
/// P/Invoke for <see cref="TaskbarBandWindow"/>: locating the taskbar/tray
/// area, setting the owner window (GWLP_HWNDPARENT), and positioning.
/// A separate class from <c>DesktopWidgetWindow.NativeMethods</c> (Task 14) —
/// that one only deals with z-order/no-activate for THAT window on the
/// desktop; here it's a completely different set of calls, and there are
/// exactly two shared signatures (Get/SetWindowLongPtr) — not worth dragging
/// unrelated context there for such a small overlap
/// (task-17-brief.md: "move existing declarations there ONLY if trivially
/// safe").
/// </summary>
internal static class Win32
{
    public const int GwlExStyle = -20;

    /// GWLP_HWNDPARENT — for a top-level (non-WS_CHILD) window this sets not
    /// the parent but the OWNER: Windows always keeps an owned window above
    /// its owner in Z-order, while the window itself remains an ordinary
    /// top-level window — no child compositing (and the associated Mica
    /// muting of content over the taskbar, see the TaskbarBandWindow
    /// doc-comment).
    public const int GwlpHwndParent = -8;

    public const long WsExNoActivate = 0x08000000L;
    public const long WsExToolWindow = 0x00000080L;

    /// HWND_TOPMOST — insert at the front of the overall Z-order (not just
    /// among the children/owned windows of a particular parent).
    public static readonly nint HwndTopMost = -1;

    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;

    /// GW_HWNDPREV — the window that sits ABOVE this one in the Z-order
    /// (toward the top).
    public const uint GwHwndPrev = 3;

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint hwnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hwnd);

    public const int WmDpiChanged = 0x02E0;

    /// MONITOR_DEFAULTTONEAREST — MonitorFromWindow never returns NULL with
    /// this flag (unlike MONITOR_DEFAULTTONULL), even if the window
    /// temporarily doesn't intersect any monitor.
    public const uint MonitorDefaultToNearest = 0x00000002;

    /// WINEVENT_OUTOFCONTEXT — the callback lives in OUR process; the OS
    /// does not inject our DLL into other processes to deliver the event
    /// (unlike WINEVENT_INCONTEXT). The event arrives on the thread that
    /// called SetWinEventHook (the one pumping messages) — for us that's
    /// the WPF Dispatcher's UI thread.
    public const uint WinEventOutOfContext = 0x0000;

    /// EVENT_SYSTEM_FOREGROUND — the active (foreground) window has changed.
    public const uint EventSystemForeground = 0x0003;

    /// EVENT_OBJECT_LOCATIONCHANGE — a window/object moved or changed size.
    /// Catches switching to fullscreen WITHOUT a change of the foreground
    /// window (F11, borderless mode of the same game).
    public const uint EventObjectLocationChange = 0x800B;

    /// EVENT_OBJECT_REORDER — a window's children z-order has changed; for
    /// top-level window reshuffles the desktop acts as the parent. This is
    /// the EXACT moment the shell may have raised the taskbar above the
    /// band (burying it) — by reacting to this instead of a subsequent
    /// focus change, we eliminate even a single-frame flicker of the band
    /// under an opaque taskbar.
    public const uint EventObjectReorder = 0x8004;

    /// EVENT_SYSTEM_MINIMIZESTART/END — a window minimized/restored from a
    /// minimized state. An adjacent pair of values — a single
    /// SetWinEventHook with a range covers both. Needed by the taskbar
    /// visibility probe: after "Minimize" the foreground doesn't always
    /// change instantly, but the fact of minimizing itself arrives right
    /// away.
    public const uint EventSystemMinimizeStart = 0x0016;
    public const uint EventSystemMinimizeEnd = 0x0017;

    /// GA_ROOT — the topmost (top-level) window in the parent chain:
    /// WindowFromPoint returns the deepest child element under the point,
    /// and answering "whose window is this" requires its root.
    public const uint GaRoot = 2;

    /// OBJID_WINDOW/CHILDID_SELF — a filter for "an event about the whole
    /// window itself", not about one of its internal UI elements (a
    /// button, a scrollbar, etc.) — the system-wide hook receives orders
    /// of magnitude more of those IDOBJECT/IDCHILD events, and almost none
    /// of them are related to fullscreen.
    public const int ObjIdWindow = 0;
    public const int ChildIdSelf = 0;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WinEventDelegate(
        nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint idEventTime);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);

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

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    /// SW_SHOWMAXIMIZED — the WINDOWPLACEMENT.ShowCmd value for a maximized
    /// window.
    public const uint SwShowMaximized = 3;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

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

    /// Available since Windows 10 1607 (Anniversary Update) — the minimum
    /// supported version here is already Windows 10/11, so a separate
    /// version check isn't needed.
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    // EntryPoint is required: the method name here ("...Native") doesn't
    // match the real export name, and without an explicit EntryPoint
    // P/Invoke looks in the DLL for a function with the METHOD NAME
    // ("GetClassNameNative") rather than GetClassNameW — it throws
    // EntryPointNotFoundException on the very first call.
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    /// A wrapper over GetClassNameW — declaring the buffer separately and
    /// calling P/Invoke at every call site would be needless duplication
    /// for the sake of the single real consumer (the fullscreen check
    /// below).
    public static string GetClassName(nint hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassNameNative(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    /// DWMWA_CLOAKED — the window is "cloaked": DWM doesn't render it, even
    /// though in Win32 terms it remains WS_VISIBLE and is returned by
    /// WindowFromPoint. This is how suspended UWP apps live, and,
    /// importantly for the taskbar probe, so do exclusive-fullscreen game
    /// windows after losing focus: Deadlock (SDL_app), after being
    /// minimized, hangs around as a ghost ABOVE the taskbar in the
    /// z-order for hours without rendering.
    public const uint DwmwaCloaked = 14;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out uint pvAttribute, int cbAttribute);

    /// DWMWA_WINDOW_CORNER_PREFERENCE / DWMWCP_ROUND — the Windows 11 (build
    /// 22000+) way to ask DWM for rounded corners on a window we don't own the
    /// frame of. Used by the tray menu (MenuChrome); on Windows 10 the call
    /// simply returns a failure HRESULT and the menu stays square.
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwcpRound = 2;

    /// PreserveSig — the HRESULT comes back as the return value instead of
    /// being turned into a COMException: a rounded corner is cosmetic, and the
    /// caller ignores the failure rather than handling an exception.
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// true if DWM genuinely isn't painting this window. A failed query is
    /// read as "not cloaked": better to err on the side of trusting the
    /// window is real than to ignore an actual fullscreen game.
    public static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(uint)) == 0 && cloaked != 0;

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    // EntryPoint given explicitly rather than relying on automatic A/W
    // resolution (the GetMonitorInfo macro does indeed expand to
    // GetMonitorInfoW under Unicode, and the CLR can in theory find it on
    // its own via CharSet — but it was exactly this implicitness that
    // failed on the neighboring GetClassNameNative above: EntryPoint
    // wasn't substituted automatically because the C# method name didn't
    // match any export at all. An explicit EntryPoint here is cheap
    // insurance against the same class of bug).
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
}
