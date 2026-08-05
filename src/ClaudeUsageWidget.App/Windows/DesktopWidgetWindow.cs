using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.Core;
// UseWindowsForms делает System.Drawing/System.Windows.Forms глобально
// видимыми (см. ClaudeUsageWidget.App.GlobalUsings.g.cs) — Point/Cursor/
// MouseEventArgs существуют и там под тем же именем.
using Point = System.Windows.Point;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;

namespace ClaudeUsageWidget.App.Windows;

/// <summary>
/// Виджет на рабочем столе: панель без рамки, не ворующая фокус, всегда
/// прижатая к низу Z-порядка (над обоями, под любым обычным окном), с
/// ручными drag/resize (не системными — окно не активируется, поэтому
/// <see cref="Window.DragMove"/> здесь не работает надёжно). Порт поведения
/// <c>Sources/ClaudeUsageWidget/ClaudeUsageWidgetApp.swift:196-213, 238-324</c>.
/// </summary>
public sealed class DesktopWidgetWindow : Window
{
    /// Ширина полосы у края панели, за которую хватают для resize —
    /// task-14-brief.md: «полосы захвата ~8 px по краям».
    private const double EdgeBand = 8;

    private readonly SettingsStore _settings;
    private readonly WidgetRootView _root;
    private readonly DispatcherTimer _persistTimer;

    private double _side;
    private bool _positionLocked;

    private bool _dragging;
    private Point _dragAnchor;

    private bool _resizing;
    private ResizeEdge _resizeEdge;
    private Point _resizeStartPoint;
    private double _resizeStartSide;

    /// Кнопка-глаз в hover-хедере — окно уже спрятало себя к этому моменту
    /// (см. <see cref="OnEyeClicked"/>), событие тут чисто для оповещения.
    public event Action? HideRequested;

    /// Кнопка Sign in в плашке NoCredentials.
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

    public DesktopWidgetWindow(SettingsStore settings)
    {
        _settings = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        // Системного resize тут не место — единственный источник изменения
        // размера это наша собственная логика по EdgeBand ниже.
        ResizeMode = ResizeMode.NoResize;

        _root = new WidgetRootView();
        _root.HideRequested += OnEyeClicked;
        _root.SignInRequested += () => SignInRequested?.Invoke();
        _root.LockToggleRequested += () => PositionLocked = !PositionLocked;
        Content = _root;

        var data = settings.Load();
        _side = WidgetSettings.ClampSide(data.WidgetSide);
        _positionLocked = data.PositionLocked;
        _root.PositionLocked = _positionLocked;

        Width = _side;
        Height = _side;
        _root.ApplyLayout(_side);

        if (data.WidgetX is { } x && data.WidgetY is { } y)
        {
            Left = x;
            Top = y;
        }
        else
        {
            // Первый запуск: центр экрана, как window.center() в оригинале.
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            PersistGeometry();
        };

        // OnSourceInitialized переопределён ниже (нужен доступ к HWND) —
        // отдельная подписка на событие SourceInitialized тут не нужна и не
        // подошла бы по сигнатуре (EventHandler против EventArgs-only override).
        MouseLeftButtonDown += OnWindowMouseLeftButtonDown;
        MouseMove += OnWindowMouseMove;
        MouseLeftButtonUp += OnWindowMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        Closing += (_, _) => FlushPendingPersist();
    }

    /// <summary>Порт тела WidgetRootView.swift:46-96 + BlockingNotice.swift на
    /// стороне окна: собирает всё, что WidgetRootView нужно для отрисовки, из
    /// сырого состояния стора.</summary>
    public void Render(UsageState state, UsageSnapshot? last, ServiceStatus status, string? preferredModelKey, DateTimeOffset? retryUntil)
    {
        var now = DateTimeOffset.Now;
        var snapshot = state is UsageState.Ok(var okSnapshot, _) ? okSnapshot : last;
        var dimmed = state is not UsageState.Ok;

        var models = DialModel.All(snapshot, preferredModelKey, now);
        var statusLine = StatusLine.Text(state, now, retryUntil);
        var notice = NoticeFor(state);

        _root.SetContent(models, status, dimmed, statusLine, notice);
    }

    /// App-слойный аналог BlockingNotice.make(for:) — Core его не портирует
    /// (см. task-14-brief.md), поэтому правило живёт здесь. NoCredentials
    /// получает кнопку Sign in (в оригинальном BlockingNotice.swift такой
    /// кнопки нет — только текст со ссылкой на меню); Unauthorized показывает
    /// UsageError.Description, а не зашитую строку, как просит бриф.
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
    /// Держит окно на дне Z-порядка: любая попытка системы переставить его
    /// (SetForegroundWindow где-то ещё, alt-tab, всплытие другого окна)
    /// перехватывается на WM_WINDOWPOSCHANGING, и hwndInsertAfter
    /// принудительно переписывается на HWND_BOTTOM с сброшенным
    /// SWP_NOZORDER — иначе Windows проигнорирует hwndInsertAfter и оставит
    /// окно там, где просил вызывающий. task-14-brief.md, шаг 4.
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

        // handled остаётся false: сообщение должно продолжить обычную
        // обработку (DefWindowProc), просто со structure, которую мы только
        // что подменили по месту через lParam.
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

    /// Какая полоса у края под точкой — либо None (внутренняя область, то
    /// есть drag), либо одна из восьми зон resize.
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

    /// Проекция мышиного смещения на изменение единственной величины —
    /// стороны квадрата. Знак подобран так, чтобы верхний левый угол всегда
    /// оставался на месте (растёт вправо/вниз независимо от того, за какой
    /// край тянут) — task-14-brief.md: «верхний левый угол на месте». Тот же
    /// знак, что и в Grip.delta — WidgetRootView.swift:246-255.
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
            // Ручной drag через захват мыши, а не DragMove: окно
            // ShowActivated=false + WS_EX_NOACTIVATE, а DragMove изнутри
            // шлёт WM_SYSCOMMAND/SC_MOVE, который рассчитан на активное
            // окно и с неактивируемым ведёт себя ненадёжно. task-14-brief.md,
            // шаг 4.
            _dragging = true;
            _dragAnchor = pos;
        }

        CaptureMouse();
        e.Handled = true;
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Пояс поверх подтяжек OnLostMouseCapture: если _dragging/_resizing
        // всё же остался true без реально зажатой левой кнопки (гонка
        // событий или сценарий потери capture, который LostMouseCapture по
        // какой-то причине не поймал), не даём фантомный drag/resize по
        // голому наведению — сбрасываем состояние и ведём себя как обычный
        // hover. WM_MOUSEMOVE приходит независимо от того, захвачена мышь
        // или нет, поэтому без этой проверки следующее наведение на панель
        // читалось бы как продолжение перетаскивания со старым якорем.
        if ((_dragging || _resizing) && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _dragging = false;
            _resizing = false;
            _resizeEdge = ResizeEdge.None;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        if (_dragging)
        {
            // Инкрементальная поправка, пересчитанная на каждый tick:
            // GetPosition(this) всегда относительно ТЕКУЩЕГО положения окна,
            // поэтому "текущее относительное минус исходное относительное"
            // и есть то смещение, на которое сдвинулась мышь с прошлого
            // кадра — окно каждый раз довигается ровно настолько же.
            // Разница с прямым screen-to-DIP пересчётом через PointToScreen:
            // не нужно отдельно учитывать DPI монитора, GetPosition и
            // Left/Top уже в одной системе координат.
            var delta = pos - _dragAnchor;
            Left += delta.X;
            Top += delta.Y;
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
    /// Захват мыши можно потерять не только через наш собственный
    /// ReleaseMouseCapture: системный модальный диалог, блокировка экрана,
    /// разрыв RDP-сессии или другое приложение, перехватившее capture —
    /// во всех этих случаях OnWindowMouseLeftButtonUp никогда не вызывается,
    /// а _dragging/_resizing застряли бы в true навсегда. WM_MOUSEMOVE при
    /// этом продолжает приходить и при обычном наведении без зажатой
    /// кнопки, поэтому без этого сброса следующий hover над панелью читался
    /// бы как продолжение drag/resize со старым (уже неактуальным) якорем —
    /// фантомное перемещение/ресайз до следующего настоящего mouse-down.
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
        Width = side;
        Height = side;
        _root.ApplyLayout(side);
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
        var data = _settings.Load();
        _settings.Save(data with { WidgetX = Left, WidgetY = Top, WidgetSide = _side });
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

/// <summary>P/Invoke для этого окна: пин к низу Z-порядка и стиль
/// «не активируется, не в таскбаре». Не LibraryImport — тот требует
/// AllowUnsafeBlocks ради одного файла P/Invoke, тот же выбор, что и в
/// Tray/TrayIcon.cs.</summary>
internal static class NativeMethods
{
    // int, не nint: nint не может быть const в C#, а сами флаги укладываются
    // в 32 бита — комбинируются через long в SetWindowLongPtr-вызове ниже.
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
