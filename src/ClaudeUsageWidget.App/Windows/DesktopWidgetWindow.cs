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

    /// Число строк, под которое сейчас построена сетка. Расходится с
    /// настройками при добавлении/удалении аккаунта — Render это ловит.
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
        // Системного resize тут не место — единственный источник изменения
        // размера это наша собственная логика по EdgeBand ниже.
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

        // Стартуем с одной строкой; Render пересоберёт сетку, как только
        // узнает реальное число аккаунтов.
        _accountCount = data.Accounts.Count > 0 ? data.Accounts.Count : 1;
        ApplyLayoutAndSize(_side);

        if (data.WidgetX is { } x && data.WidgetY is { } y)
        {
            Left = x;
            Top = y;
            // После присвоения, а не внутри ApplyLayoutAndSize выше: там
            // Left/Top ещё NaN, и прижимать нечего.
            ClampToScreen();
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
    /// <param name="rows">По строке на аккаунт, в порядке настроек.</param>
    /// <param name="state">Состояние ТРЕЙ-аккаунта: плашка и строка статуса
    /// описывают его, а не все аккаунты сразу — плашка на каждую строку была бы
    /// отдельным UI, которого дизайн не просил.</param>
    public void Render(
        IReadOnlyList<AccountRow> rows, UsageState state, ServiceStatus status, DateTimeOffset? retryUntil)
    {
        // Число строк меняется при добавлении/удалении аккаунта — сетка обязана
        // быть пересобрана до заполнения, иначе SetContent бросит.
        if (rows.Count != _accountCount)
        {
            _accountCount = rows.Count;
            ApplyLayoutAndSize(_side);
        }

        _last = (rows, state, status, retryUntil);
        Draw();
    }

    /// Последний отрисованный кадр. Пересборка сетки (ресайз, смена числа
    /// аккаунтов) обнуляет ячейки, а следующий Changed от стора может прийти
    /// через пять минут — без этого панель всё это время стоит пустой, что и
    /// выглядело как «при ресайзе теряется информация».
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

    /// Раскладка сменилась в настройках — пересобрать сетку и пересчитать
    /// размер, не дожидаясь ни ресайза, ни следующего обновления стора.
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
        // Сетка только что пересобрана и пуста — заполняем её тем же кадром,
        // не дожидаясь следующего обновления стора.
        Draw();
        ClampToScreen();
    }

    /// Панель перестала быть квадратом и растёт вширь с каждым аккаунтом, а
    /// сохранённая позиция — от прежнего размера: без этого правый край
    /// уезжает за границу экрана, и часть циферблатов просто не видна
    /// (поймано скриншотом на 256 pt и четырёх колонках).
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
