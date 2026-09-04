using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.Core;
// UseWindowsForms makes System.Drawing/System.Windows.Forms globally
// visible (see ClaudeUsageWidget.App.GlobalUsings.g.cs) — Point/Cursor/
// MouseEventArgs also exist there under the same name.
using Point = System.Windows.Point;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;

namespace ClaudeUsageWidget.App.Windows;

/// <summary>
/// The desktop widget: a frameless panel that doesn't steal focus, always
/// pinned to the bottom of the Z-order (above the wallpaper, below any
/// regular window), with manual drag/resize (not system ones — the window
/// doesn't activate, so <see cref="Window.DragMove"/> doesn't work
/// reliably here). A port of the behavior in
/// <c>Sources/ClaudeUsageWidget/ClaudeUsageWidgetApp.swift:196-213, 238-324</c>.
/// </summary>
public sealed class DesktopWidgetWindow : Window
{
    /// Width of the band at the panel's edge grabbed for resize —
    /// task-14-brief.md: "~8 px grab bands along the edges".
    private const double EdgeBand = 8;

    private readonly SettingsStore _settings;
    private readonly WidgetRootView _root;
    private readonly DispatcherTimer _persistTimer;

    private double _side;

    /// The number of rows the grid is currently built for. Diverges from the
    /// settings when an account is added/removed — Render catches this.
    private int _accountCount;
    private bool _positionLocked;

    private bool _dragging;
    private Point _dragAnchor;

    private bool _resizing;
    private ResizeEdge _resizeEdge;
    private Point _resizeStartPoint;
    private double _resizeStartSide;

    /// The toolbar's Hide button — the window has already hidden itself by then
    /// (see <see cref="OnEyeClicked"/>), so this is a notification only.
    public event Action? HideRequested;

    /// The Sign in button in the NoCredentials notice.
    public event Action? SignInRequested;

    public bool PositionLocked
    {
        get => _positionLocked;
        set
        {
            if (_positionLocked == value) return;
            _positionLocked = value;
            _root.PositionLocked = value;
            PersistPositionLocked(value);
        }
    }

    /// A drop in edit mode produced a new layout. The App saves it.
    public event Action<WidgetLayout>? LayoutEdited;

    /// The toolbar's Status button. The App saves the mode and re-sanitizes the
    /// layout around it.
    public event Action<StatusMode>? StatusModeSelected;

    /// The toolbar's Model button. Same shape as the status one: the App saves
    /// the setting and re-sanitizes the layout around it.
    public event Action<ModelDial>? ModelDialSelected;

    /// The toolbar's Plan button. No Sanitize on the far side — the plan is a
    /// line under the name and never a cell — but the panel is SIZED for it, so
    /// the App still rebuilds the layout rather than only repainting.
    public event Action<PlanLine>? PlanLineSelected;

    /// The toolbar's Done button, on its way to `App.SetEditingLayout(false)`.
    public event Action? EditDoneRequested;

    /// Which side of the panel the strip is drawn on, and how much of the
    /// window's height currently sits ABOVE the panel because of it.
    ///
    /// That pair is what makes the mode invisible to the panel. The strip lives
    /// in a band the WINDOW grows by, so the rounded border keeps its size; and
    /// `Top` moves by the band whenever the band changes, so the border keeps
    /// its place on screen. The user's report: the panel resized on the way in
    /// and sat somewhere else on the way out.
    private bool _stripAbove;
    private double _stripAbovePad;

    /// Layout edit mode. The view does everything the mode means; the window
    /// grows by the strip's band, keeps the PANEL where it was, and repaints.
    public bool EditMode
    {
        get => _root.EditMode;
        set
        {
            // Only on a real transition: SetEditingLayout(false) is reached
            // from three places, and a second "off" would move Top by a band
            // that is no longer there.
            if (_root.EditMode == value) return;

            // Above the panel unless the strip would cross the top of the work
            // area; below is always available, because the window may grow
            // downwards instead. Decided off the PANEL's top — `_stripAbovePad`
            // is 0 here, and naming it keeps this true if the order ever
            // changes — and skipped on the first run, where CenterScreen has
            // left Left/Top NaN.
            if (value)
                _stripAbove = !double.IsNaN(Left) && !double.IsNaN(Top)
                    && Top + _stripAbovePad - _root.ToolbarReserve >= WorkArea().Top;

            _root.StripAbove = _stripAbove;
            _root.EditMode = value;
            // The view has already resized itself around the band. Without this
            // the strip would be drawn outside the window.
            Width = _root.Width;
            Height = _root.Height;

            // Up by the reserve on the way in, back down on the way out, and
            // nothing at all when the strip is below. There is no remembered
            // position to restore: the panel never left the place it was in.
            ApplyStripBand(value && _stripAbove ? _root.ToolbarReserve : 0);
            if (!value) _stripAbove = false;

            ClampToScreen();

            // Saved the way a finished drag saves one, in the mode as well as
            // out of it: PersistGeometry writes the PANEL's top-left, so there
            // is no longer a dishonest coordinate to keep out of settings.
            SchedulePersistGeometry();

            // The mode's hint lives in the status line, which only SetContent
            // writes — and the next poll is up to five minutes away. Without
            // this the hint would appear that late on the way in AND stay that
            // long on the way out, advertising a mode that is already off.
            Draw();
        }
    }

    /// Moves the window's top so the PANEL's top-left stays put while the band
    /// above it changes: entering and leaving the mode, and a resize inside it,
    /// where the reserve scales with the side like every other measurement.
    private void ApplyStripBand(double abovePad)
    {
        if (!double.IsNaN(Top)) Top -= abovePad - _stripAbovePad;
        _stripAbovePad = abovePad;
    }

    /// Re-picks the strip's side from where the PANEL is now: above by default,
    /// below as soon as a strip above would cross the top of the work area.
    /// Entry decides once; this runs on every drag tick and every resize inside
    /// the mode, because the panel can be carried to the ceiling while editing
    /// (the user, 2026-09-04) and the strip must drop under it rather than leave
    /// the screen. A flip moves the window's Top by the band while the panel
    /// stays put, so a drag in progress shifts its anchor by the same amount —
    /// otherwise the next tick would read that jump as mouse travel.
    private void ReconsiderStripSide()
    {
        if (!_root.EditMode || double.IsNaN(Top)) return;

        var reserve = _root.ToolbarReserve;
        var wantAbove = Top + _stripAbovePad - reserve >= WorkArea().Top;
        if (wantAbove == _stripAbove) return;

        var before = Top;
        _stripAbove = wantAbove;
        _root.StripAbove = wantAbove;
        ApplyStripBand(wantAbove ? reserve : 0);
        if (_dragging) _dragAnchor.Y += before - Top;
    }

    public DesktopWidgetWindow(SettingsStore settings)
    {
        _settings = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        // System resize has no place here — the only source of size changes is
        // our own EdgeBand logic below.
        ResizeMode = ResizeMode.NoResize;

        _root = new WidgetRootView();
        _root.HideRequested += OnEyeClicked;
        _root.SignInRequested += () => SignInRequested?.Invoke();
        _root.LockToggleRequested += () => PositionLocked = !PositionLocked;
        _root.EditDoneRequested += () => EditDoneRequested?.Invoke();
        _root.LayoutEdited += layout => LayoutEdited?.Invoke(layout);
        _root.StatusModeSelected += mode => StatusModeSelected?.Invoke(mode);
        _root.ModelDialSelected += dial => ModelDialSelected?.Invoke(dial);
        _root.PlanLineSelected += line => PlanLineSelected?.Invoke(line);
        Content = _root;

        var data = settings.Load();
        _side = WidgetSettings.ClampSide(data.WidgetSide);
        _positionLocked = data.PositionLocked;
        _root.PositionLocked = _positionLocked;

        // Start with one row; Render will rebuild the grid as soon as it learns
        // the actual number of accounts.
        _accountCount = data.Accounts.Count > 0 ? data.Accounts.Count : 1;
        ApplyLayoutAndSize(_side);

        if (data.WidgetX is { } x && data.WidgetY is { } y)
        {
            Left = x;
            Top = y;
            // After the assignment, not inside ApplyLayoutAndSize above: there
            // Left/Top are still NaN, and there's nothing to clamp.
            ClampToScreen();
        }
        else
        {
            // First run: center of the screen, like window.center() in the original.
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            PersistGeometry();
        };

        // OnSourceInitialized is overridden below (HWND access is needed) — a
        // separate subscription to the SourceInitialized event isn't needed here
        // and wouldn't fit the signature anyway (EventHandler vs. an
        // EventArgs-only override).
        MouseLeftButtonDown += OnWindowMouseLeftButtonDown;
        MouseMove += OnWindowMouseMove;
        MouseLeftButtonUp += OnWindowMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        Closing += (_, _) => FlushPendingPersist();
    }

    /// <summary>A window-side port of the body of WidgetRootView.swift:46-96 +
    /// BlockingNotice.swift: gathers everything WidgetRootView needs to draw,
    /// from the store's raw state.</summary>
    /// <param name="rows">One row per account, in settings order.</param>
    /// <param name="state">The state of the TRAY account: the notice and the
    /// status line describe it, not all accounts at once — a notice per row
    /// would be separate UI that the design never asked for.</param>
    public void Render(
        IReadOnlyList<AccountRow> rows, UsageState state, ServiceStatus status, DateTimeOffset? retryUntil)
    {
        // The number of rows changes when an account is added/removed — the grid
        // must be rebuilt before filling, otherwise SetContent throws.
        if (rows.Count != _accountCount)
        {
            _accountCount = rows.Count;
            ApplyLayoutAndSize(_side);
        }

        _last = (rows, state, status, retryUntil);
        Draw();
    }

    /// The last frame drawn. Rebuilding the grid (resize, a change in the
    /// number of accounts) zeroes out the cells, and the store's next Changed
    /// might not arrive for five minutes — without this the panel would sit
    /// empty the whole time, which is what looked like "resizing loses
    /// information".
    private (IReadOnlyList<AccountRow> Rows, UsageState State, ServiceStatus Status, DateTimeOffset? RetryUntil)? _last;

    private void Draw()
    {
        if (_last is not { } f) return;

        var dimmed = f.State is not UsageState.Ok;
        _root.SetContent(
            f.Rows, f.Status, dimmed,
            StatusLine.Text(f.State, DateTimeOffset.Now, f.RetryUntil),
            NoticeFor(f.State));
    }

    /// The layout changed in settings — rebuild the grid and recompute the
    /// size, without waiting for either a resize or the store's next update.
    public void RebuildLayout(int accountCount)
    {
        _accountCount = Math.Max(accountCount, 0);
        ApplyLayoutAndSize(_side);
    }

    private void ApplyLayoutAndSize(double side)
    {
        var data = _settings.Load();
        var mode = StatusModes.Resolve(data.StatusMode, data.Layout);
        var modelDial = ModelDials.Resolve(data.ModelDial);
        var planLine = PlanLines.Resolve(data.PlanLine);
        var layout = WidgetLayout.Sanitize(data.Layout, mode, modelDial);

        var metrics = PanelMetrics.For(layout, _accountCount, side, _root.EditMode, planLine);
        Width = metrics.Width;
        Height = metrics.Height;
        _root.ApplyLayout(layout, mode, modelDial, planLine, _accountCount, side);
        // A resize inside edit mode scales the strip's band with everything
        // else — hold the PANEL's top-left still, the same corner a resize
        // holds outside the mode.
        ApplyStripBand(_stripAbove ? metrics.ToolbarReserve : 0);
        // A bigger band may no longer fit above: same rule as a drag.
        ReconsiderStripSide();
        // The grid was just rebuilt and is empty — fill it with the same frame,
        // without waiting for the store's next update.
        Draw();
        ClampToScreen();
    }

    /// The panel stopped being square and grows wider with every account,
    /// while the saved position is from the previous size: without this the
    /// right edge drifts off the screen, and some of the dials simply aren't
    /// visible (caught in a screenshot at 256 pt and four columns).
    private void ClampToScreen()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;

        var area = WorkArea();

        Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
        Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
    }

    /// The work area of the screen this window is on. One reader, so the side
    /// the strip goes on and the clamp that follows cannot disagree about where
    /// the top of the screen is — they would, on a secondary monitor. Callers
    /// must have checked Left/Top for NaN: the cast to int does not.
    private System.Drawing.Rectangle WorkArea() =>
        System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)Left, (int)Top)).WorkingArea;

    /// An App-layer counterpart to BlockingNotice.make(for:) — Core doesn't
    /// port it (see task-14-brief.md), so the rule lives here. NoCredentials
    /// gets a Sign in button (the original BlockingNotice.swift has no such
    /// button — just text referring to the menu); Unauthorized shows
    /// UsageError.Description rather than a hardcoded string, as the brief
    /// asks.
    private static WidgetNotice? NoticeFor(UsageState state) => state switch
    {
        UsageState.Failed(var error) when error.Kind == UsageErrorKind.NoCredentials =>
            new WidgetNotice("Not signed in", "Use the menu to sign in to Claude.ai", ShowSignIn: true),
        UsageState.Failed(var error) when error.Kind == UsageErrorKind.Unauthorized =>
            new WidgetNotice("Session expired", error.Description, ShowSignIn: false),
        _ => null,
    };

    private void OnEyeClicked()
    {
        PersistWidgetVisible(false);
        Hide();
        HideRequested?.Invoke();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        var newExStyle = (nint)((long)exStyle | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, newExStyle);

        var source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("HwndSource is not available after SourceInitialized.");
        source.AddHook(WndProc);
    }

    /// <summary>
    /// Keeps the window at the bottom of the Z-order: any attempt by the
    /// system to reposition it (SetForegroundWindow elsewhere, alt-tab,
    /// another window popping up) is intercepted at WM_WINDOWPOSCHANGING, and
    /// hwndInsertAfter is forcibly rewritten to HWND_BOTTOM with SWP_NOZORDER
    /// cleared — otherwise Windows would ignore hwndInsertAfter and leave the
    /// window where the caller asked. task-14-brief.md, step 4.
    /// </summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmWindowPosChanging)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            pos.hwndInsertAfter = NativeMethods.HwndBottom;
            pos.flags &= ~NativeMethods.SwpNozorder;
            Marshal.StructureToPtr(pos, lParam, false);
        }

        // handled stays false: the message should continue normal processing
        // (DefWindowProc), just with the structure we just swapped in place via
        // lParam.
        return nint.Zero;
    }

    private enum ResizeEdge
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// Which edge band is under the point — either None (the interior area,
    /// meaning drag), or one of the eight resize zones.
    private ResizeEdge HitTestEdge(Point pos)
    {
        var nearLeft = pos.X <= EdgeBand;
        var nearRight = pos.X >= ActualWidth - EdgeBand;
        var nearTop = pos.Y <= EdgeBand;
        var nearBottom = pos.Y >= ActualHeight - EdgeBand;

        return (nearLeft, nearRight, nearTop, nearBottom) switch
        {
            (true, _, true, _) => ResizeEdge.TopLeft,
            (_, true, true, _) => ResizeEdge.TopRight,
            (true, _, _, true) => ResizeEdge.BottomLeft,
            (_, true, _, true) => ResizeEdge.BottomRight,
            (true, false, false, false) => ResizeEdge.Left,
            (false, true, false, false) => ResizeEdge.Right,
            (false, false, true, false) => ResizeEdge.Top,
            (false, false, false, true) => ResizeEdge.Bottom,
            _ => ResizeEdge.None,
        };
    }

    /// Projects the mouse offset onto a change in the single quantity — the
    /// square's side. The sign is chosen so the top-left corner always stays
    /// put (it grows right/down regardless of which edge is being dragged) —
    /// task-14-brief.md: "top-left corner stays put". The same sign as in
    /// Grip.delta — WidgetRootView.swift:246-255.
    private static double ResizeDelta(ResizeEdge edge, double dx, double dy) => edge switch
    {
        ResizeEdge.Left => -dx,
        ResizeEdge.Right => dx,
        ResizeEdge.Top => -dy,
        ResizeEdge.Bottom => dy,
        ResizeEdge.TopLeft => Math.Max(-dx, -dy),
        ResizeEdge.TopRight => Math.Max(dx, -dy),
        ResizeEdge.BottomLeft => Math.Max(-dx, dy),
        ResizeEdge.BottomRight => Math.Max(dx, dy),
        _ => 0,
    };

    private static Cursor CursorFor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
        ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
        ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
        ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PositionLocked) return;

        var pos = e.GetPosition(this);
        var edge = HitTestEdge(pos);

        if (edge != ResizeEdge.None)
        {
            _resizing = true;
            _resizeEdge = edge;
            _resizeStartPoint = pos;
            _resizeStartSide = _side;
        }
        else
        {
            // Manual drag via mouse capture, not DragMove: the window has
            // ShowActivated=false + WS_EX_NOACTIVATE, and DragMove internally sends
            // WM_SYSCOMMAND/SC_MOVE, which expects an active window and behaves
            // unreliably with a non-activating one. task-14-brief.md, step 4.
            _dragging = true;
            _dragAnchor = pos;
        }

        CaptureMouse();
        e.Handled = true;
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        // A belt-and-suspenders check on top of OnLostMouseCapture: if
        // _dragging/_resizing somehow stayed true without the left button
        // actually being held (an event race, or a capture-loss scenario that
        // LostMouseCapture failed to catch for some reason), we don't let a bare
        // hover trigger a phantom drag/resize — reset the state and behave like
        // a normal hover. WM_MOUSEMOVE arrives regardless of whether the mouse
        // is captured, so without this check the next hover over the panel would
        // read as a continuation of the drag with a stale anchor.
        if ((_dragging || _resizing) && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _dragging = false;
            _resizing = false;
            _resizeEdge = ResizeEdge.None;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        if (_dragging)
        {
            // An incremental correction, recomputed on every tick:
            // GetPosition(this) is always relative to the window's CURRENT position,
            // so "current relative minus original relative" is exactly the offset
            // the mouse moved since the previous frame — the window moves by exactly
            // that much each time. The difference from a direct screen-to-DIP
            // recomputation via PointToScreen: there's no need to separately account
            // for the monitor's DPI, GetPosition and Left/Top are already in the
            // same coordinate system.
            var delta = pos - _dragAnchor;
            Left += delta.X;
            Top += delta.Y;
            // A drag INSIDE edit mode: the panel's top-left follows the window's
            // and PersistGeometry keeps writing the panel's; the only extra is
            // the strip changing sides when the panel reaches the ceiling.
            ReconsiderStripSide();
            SchedulePersistGeometry();
            return;
        }

        if (_resizing)
        {
            var dx = pos.X - _resizeStartPoint.X;
            var dy = pos.Y - _resizeStartPoint.Y;
            var newSide = WidgetSettings.ClampSide(_resizeStartSide + ResizeDelta(_resizeEdge, dx, dy));
            ApplySide(newSide);
            SchedulePersistGeometry();
            return;
        }

        Cursor = PositionLocked ? Cursors.Arrow : CursorFor(HitTestEdge(pos));
    }

    private void OnWindowMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging && !_resizing) return;

        _dragging = false;
        _resizing = false;
        _resizeEdge = ResizeEdge.None;
        ReleaseMouseCapture();
        SchedulePersistGeometry();
    }

    /// <summary>
    /// Mouse capture can be lost not only through our own
    /// ReleaseMouseCapture: a system modal dialog, a screen lock, an RDP
    /// session drop, or another application stealing capture — in all these
    /// cases OnWindowMouseLeftButtonUp is never called, and _dragging/_resizing
    /// would stay stuck at true forever. WM_MOUSEMOVE keeps arriving even on
    /// an ordinary hover with no button held, so without this reset the next
    /// hover over the panel would read as a continuation of the drag/resize
    /// with a stale (already outdated) anchor — a phantom move/resize until
    /// the next real mouse-down.
    /// </summary>
    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_dragging && !_resizing) return;

        _dragging = false;
        _resizing = false;
        _resizeEdge = ResizeEdge.None;
        SchedulePersistGeometry();
    }

    private void ApplySide(double side)
    {
        if (Math.Abs(side - _side) < 0.5) return;

        _side = side;
        ApplyLayoutAndSize(side);
    }

    private void SchedulePersistGeometry()
    {
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void FlushPendingPersist()
    {
        if (!_persistTimer.IsEnabled) return;
        _persistTimer.Stop();
        PersistGeometry();
    }

    private void PersistGeometry()
    {
        // Before the window has been placed (first run is CenterScreen) Left and
        // Top are NaN, and a NaN in settings.json is a position nothing reads back.
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;

        var data = _settings.Load();
        // The PANEL's top-left, never the window's. The strip's band belongs to
        // the mode, and saving the window's top with the band in it is what
        // moved the panel by a strip's height on every visit to the mode.
        // Outside the mode the two points are the same.
        _settings.Save(data with { WidgetX = Left, WidgetY = Top + _stripAbovePad, WidgetSide = _side });
    }

    private void PersistWidgetVisible(bool visible)
    {
        var data = _settings.Load();
        _settings.Save(data with { WidgetVisible = visible });
    }

    private void PersistPositionLocked(bool locked)
    {
        var data = _settings.Load();
        _settings.Save(data with { PositionLocked = locked });
    }
}

/// <summary>P/Invoke for this window: pinning to the bottom of the
/// Z-order and the "doesn't activate, not in the taskbar" style. Not
/// LibraryImport — that requires AllowUnsafeBlocks for the sake of a
/// single P/Invoke file, the same choice as in Tray/TrayIcon.cs.</summary>
internal static class NativeMethods
{
    // int, not nint: nint can't be const in C#, and the flags themselves fit
    // in 32 bits — they're combined via long in the SetWindowLongPtr call
    // below.
    public const int GwlExStyle = -20;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;

    public const int WmWindowPosChanging = 0x0046;
    public static readonly nint HwndBottom = 1;
    public const uint SwpNozorder = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
