using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.Core;
// UseWindowsForms делает System.Drawing глобально видимым (см.
// ClaudeUsageWidget.App.GlobalUsings.g.cs) — Point/Color/Brushes/Size
// существуют и там под тем же именем (тот же приём, что и в
// Windows/DesktopWidgetWindow.cs и Views/*.cs).
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace ClaudeUsageWidget.App.Windows;

/// <summary>
/// Полоска в панели задач в технике TrafficMonitor: маленькое окно,
/// встраиваемое как дочернее в <c>Shell_TrayWnd</c> слева от области
/// переполнения трея, рисующее колонки «метка над значением» как в
/// меню-баре macOS. Если встраивание не удалось (Windows иногда режет
/// кросс-процессный <see cref="Win32.SetParent"/> политиками безопасности),
/// деградирует до top-level оверлея поверх того же места таскбара —
/// task-17-brief.md, шаг 2.
/// </summary>
public sealed class TaskbarBandWindow : Window
{
    private const string TrayClassName = "Shell_TrayWnd";
    private const string TrayNotifyClassName = "TrayNotifyWnd";

    /// Отступ ленты от области переполнения трея — task-17-brief.md: «встать
    /// левее её на ширину окна с отступом 8 px».
    private const double GapDip = 8;

    private const double OuterPaddingDip = 8;

    /// Высота таскбара Windows 10/11 по умолчанию при масштабе 100% —
    /// используется только как временное значение до первого вызова
    /// <see cref="Reposition"/> (тот в TryAttach/ConfigureOverlay вызывается
    /// раньше, чем окно становится видимым, так что реальный размер обычно
    /// подставляется ещё до показа).
    private const double DefaultHeightDip = 40;

    private readonly TaskbarBandContent _content;
    private readonly DispatcherTimer _repositionTimer;

    private bool _attached;

    /// HWND таскбара, к которому мы сейчас прицеплены (WS_CHILD) — только
    /// для самопроверки в Reposition(): explorer.exe, перезапустившись,
    /// пересоздаёт Shell_TrayWnd под новым хэндлом. nint.Zero, когда не
    /// attached.
    private nint _attachedTrayHwnd;

    /// <summary>
    /// Нативный HWND этого окна уничтожен не через наш собственный Detach()/
    /// Close() — типичная причина: explorer.exe перезапустился, и
    /// DestroyWindow на старом Shell_TrayWnd потянул за собой все его
    /// WS_CHILD, включая нас (Windows уничтожает дочерние окна вместе с
    /// родителем, независимо от того, какому процессу они принадлежат). WPF
    /// не поддерживает повторный Show()/EnsureHandle() у Window, чей HWND
    /// пропал таким образом — единственный рабочий путь восстановления это
    /// новый экземпляр TaskbarBandWindow, поэтому здесь только сигнал,
    /// пересоздание — на стороне владельца (App.xaml.cs).
    /// </summary>
    public event Action? Lost;

    public TaskbarBandWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;

        // AllowsTransparency нельзя сменить после создания HWND окна, а
        // TryAttach() решает "прижиться к таскбару или остаться оверлеем"
        // уже ПОСЛЕ конструктора — переключить стратегию прозрачности по
        // факту неудачи было бы поздно. К тому же кросс-процессный SetParent
        // с layered-window (AllowsTransparency=true) — известная дыра:
        // UpdateLayeredWindow под чужим родителем нередко рисует чёрный
        // прямоугольник вместо реального альфа-блендинга (см.
        // task-17-brief.md). Поэтому окно всегда непрозрачное, с закрашенным
        // фоном "под таскбар" — работает одинаково в обоих режимах и не
        // зависит от исхода TryAttach.
        AllowsTransparency = false;

        // Windows 10/11 по умолчанию красят таскбар в почти чёрный (тёмная
        // тема — подавляющее большинство пользователей). DwmGetColorizationColor
        // отдаёт акцентный цвет заголовков окон, а не реальный цвет
        // поверхности таскбара: акцент на таскбаре включён далеко не у всех,
        // а у части включивших он совсем светлый — тянуть его сюда значило
        // бы чаще промахиваться мимо настоящего фона, чем просто взять
        // нейтральный тёмный, подходящий под тему по умолчанию для
        // подавляющего большинства.
        var background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
        background.Freeze();
        Background = background;

        _content = new TaskbarBandContent();
        var root = new Border
        {
            Background = background,
            Padding = new Thickness(OuterPaddingDip, 0, OuterPaddingDip, 0),
            Child = _content,
        };
        Content = root;

        Height = DefaultHeightDip;

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _repositionTimer.Tick += (_, _) => Reposition();
    }

    /// <summary>
    /// Пытается встроиться в таскбар: <c>FindWindow("Shell_TrayWnd")</c> →
    /// <see cref="Win32.SetParent"/>. При успехе окно становится дочерним
    /// (WS_CHILD) и перепозиционируется каждые 5 с и на WM_DPICHANGED; при
    /// неудаче остаётся top-level оверлеем поверх того же места таскбара —
    /// вызывающему стороне (App.xaml.cs) дополнительных действий не
    /// требуется в обоих случаях, здесь уже показано.
    /// </summary>
    public bool TryAttach()
    {
        _repositionTimer.Stop();

        var interop = new WindowInteropHelper(this);
        interop.EnsureHandle();
        var hwnd = interop.Handle;

        var tray = Win32.FindWindow(TrayClassName, null);
        if (tray == nint.Zero)
        {
            ConfigureOverlay();
            return false;
        }

        // Стиль меняем ДО SetParent, не после: смена родителя первой (старый
        // порядок) иногда оставляет DWM redirection-surface окна в "ещё
        // top-level" состоянии даже после того как SetParent формально
        // сделал его дочерним — задокументированный артефакт порядка
        // вызовов при встраивании через SetParent (проверено по замечанию
        // ревью; см. отчёт по итогам измерения).
        SetChildStyle(hwnd, isChild: true);

        if (Win32.SetParent(hwnd, tray) == nint.Zero)
        {
            // SetParent не удался — откатываем стиль: WS_CHILD с текущим
            // родителем "рабочий стол" (0) невалидная комбинация.
            SetChildStyle(hwnd, isChild: false);
            ConfigureOverlay();
            return false;
        }

        _attached = true;
        _attachedTrayHwnd = tray;
        Topmost = false;

        Reposition();
        Show();
        _repositionTimer.Start();
        return true;
    }

    /// <summary>
    /// Отвязывает окно от таскбара (SetParent обратно на рабочий стол) и
    /// прячет его. Идемпотентно: безопасно вызывать даже если сейчас оверлей
    /// (не был встроен) или окно ещё ни разу не показывалось.
    /// </summary>
    public void Detach()
    {
        _repositionTimer.Stop();

        var hwnd = new WindowInteropHelper(this).Handle;
        var hwndAlive = hwnd != nint.Zero && Win32.IsWindow(hwnd);

        if (hwndAlive)
        {
            Win32.SetParent(hwnd, nint.Zero);
            if (_attached) SetChildStyle(hwnd, isChild: false);
        }

        _attached = false;
        _attachedTrayHwnd = nint.Zero;

        if (!hwndAlive)
        {
            // Зомби: тот же сценарий, что и в RepositionCore() — explorer.exe
            // перезапустился, наш WS_CHILD уничтожен вместе со старым
            // Shell_TrayWnd, и WPF уже считает это Window закрытым. Раньше
            // это было недостижимо отсюда (процесс падал на следующем тике
            // Reposition() раньше, чем пользователь успел бы снять галочку),
            // но самовосстановление сделало гонку видимой: между смертью
            // HWND и ближайшим тиком таймера есть до 5 секунд, и Detach()
            // (пользователь снял галочку) может случиться первым. Мы только
            // что остановили _repositionTimer выше — значит Reposition()
            // больше НЕ поднимет Lost сам, и просто промолчать здесь означало
            // бы оставить владельца (App.xaml.cs) с мёртвым экземпляром,
            // который упадёт на следующем TryAttach(). Поэтому сигналим сами
            // и не трогаем Topmost/Hide() — оба бросили бы то же
            // InvalidOperationException на закрытом Window.
            Lost?.Invoke();
            return;
        }

        try
        {
            Topmost = false;
            Hide();
        }
        catch (InvalidOperationException)
        {
            // Защитный бэкстоп на гонку между проверкой IsWindow выше и
            // вызовом Hide() ниже (WPF пометила Window закрытым именно в
            // этом промежутке) — тот же вывод, что и в ветке выше: экземпляр
            // мёртв, сигналим Lost вместо того чтобы тихо проглотить и
            // оставить владельца с нерабочей ссылкой.
            Lost?.Invoke();
        }
    }

    /// <summary>Перерисовывает колонки свежими метриками — источник тот же
    /// <see cref="TrayMetric"/>, что и у иконки трея (TrayText.Metrics).</summary>
    public void Render(IReadOnlyList<TrayMetric> metrics)
    {
        _content.SetMetrics(metrics);

        // Измеряем немедленно, а не откладываем на автоматический
        // layout-pass — Reposition() (в том числе самая первая, из
        // TryAttach) должна знать актуальную ширину контента прямо сейчас,
        // а не после следующего кадра отрисовки.
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Не активируется и не появляется в alt-tab. WS_EX_TOOLWINDOW имеет
        // смысл только для top-level окна (оверлей-режим) — дочернее окно и
        // так не попадает в alt-tab, но выставлять флаги безусловно дешевле,
        // чем помнить, в каком режиме окно сейчас находится, а
        // WS_EX_NOACTIVATE не мешает и дочернему.
        //
        // WS_EX_LAYERED + SetLayeredWindowAttributes(..., 255, LWA_ALPHA) —
        // стандартный приём оверлей-инструментов (FPS-счётчики, OSD), рисующих
        // поверх контента чужих окон: заставляет DWM завести под окно
        // независимую композиционную поверхность вместо обычной, вместо того
        // чтобы полагаться на композитинг родителя/области, в которой окно
        // оказалось. Alpha=255 — полностью непрозрачная (не полупрозрачный
        // оверлей, просто другой механизм композитинга).
        var exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle);
        exStyle |= Win32.WsExNoActivate | Win32.WsExToolWindow | Win32.WsExLayered;
        Win32.SetWindowLongPtr(hwnd, Win32.GwlExStyle, (nint)exStyle);
        Win32.SetLayeredWindowAttributes(hwnd, 0, 255, Win32.LwaAlpha);

        var source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HwndSource is not available after SourceInitialized.");
        source.AddHook(WndProc);
    }

    /// <summary>WM_DPICHANGED долетает до top-level окна (оверлей-режим) —
    /// перепозиционируемся сразу, не дожидаясь следующего тика таймера.
    /// Дочернее окно (attached) такого сообщения обычно не получает вовсе;
    /// для него единственная страховка — 5-секундный таймер.</summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WmDpiChanged) Reposition();
        return nint.Zero;
    }

    /// <summary>
    /// Откат к оверлею — вызывается и когда таскбар не нашёлся вовсе, и
    /// когда SetParent отказал (Windows иногда режет кросс-процессный
    /// SetParent политиками безопасности — задокументированная в брифе
    /// причина деградации). Окно остаётся top-level, но не ворует фокус и не
    /// встаёт в alt-tab (WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW уже выставлены
    /// безусловно в OnSourceInitialized).
    /// </summary>
    private void ConfigureOverlay()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_attached && hwnd != nint.Zero)
        {
            // Сюда можно попасть и после ранее успешного attach (повторный
            // TryAttach, например таскбар на мгновение пропал) — откатываем
            // WS_CHILD, иначе окно осталось бы "ребёнком" уже отсоединённого
            // родителя.
            Win32.SetParent(hwnd, nint.Zero);
            SetChildStyle(hwnd, isChild: false);
        }

        _attached = false;
        _attachedTrayHwnd = nint.Zero;
        Topmost = true;

        Reposition();
        Show();
        _repositionTimer.Start();
    }

    /// <summary>Пересчитывает позицию/размер под оба режима — ищет таскбар и
    /// область трея заново при каждом вызове (не кэширует хэндлы для самого
    /// поиска): explorer.exe может пересоздать Shell_TrayWnd, а простои/
    /// добавление иконок в трее двигают TrayNotifyWnd — task-17-brief.md:
    /// «таскбар перестраивается». Первым делом — самопроверка на
    /// перезапуск explorer.exe: если он уничтожил старый Shell_TrayWnd, то
    /// либо уничтожил вместе с ним и наш HWND (мы — WS_CHILD), либо, если мы
    /// каким-то образом пережили это, всё ещё привязаны к уже мёртвому
    /// хэндлу нового таскбара не видим. Оба случая требуют разного
    /// восстановления, см. комментарии ниже.</summary>
    private void Reposition()
    {
        try
        {
            RepositionCore();
        }
        catch (InvalidOperationException)
        {
            // WPF сама уже считает это Window закрытым (WM_NCDESTROY от
            // уничтоженного родителя дошёл до её собственного WndProc раньше
            // этого тика) — тогда WindowInteropHelper.Handle успевает
            // обнулиться ДО того как наша проверка ниже её увидит, и
            // RepositionCore() падает на EnsureHandle/Show с
            // InvalidOperationException("...после закрытия окна") вместо
            // того чтобы аккуратно вернуть Zero. Ловим здесь, а не только
            // проверкой IsWindow ниже: тот путь на практике не успевает
            // сработать первым (проверено — исходная версия без try/catch
            // валила весь процесс необработанным исключением при
            // перезапуске explorer.exe).
            _repositionTimer.Stop();
            Lost?.Invoke();
        }
    }

    private void RepositionCore()
    {
        var ownHwnd = new WindowInteropHelper(this).Handle;
        if (ownHwnd == nint.Zero || !Win32.IsWindow(ownHwnd))
        {
            // Наш собственный HWND уничтожен извне — типичная причина:
            // explorer.exe перезапустился, и DestroyWindow на старом
            // Shell_TrayWnd потянул за собой все WS_CHILD, включая нас
            // (Windows уничтожает дочерние окна вместе с родителем
            // синхронно, вне зависимости от того, какому процессу они
            // принадлежат). Этот экземпляр Window необратимо мёртв —
            // сигналим наружу вместо попытки продолжить с ним работать.
            // ownHwnd == Zero тоже сюда: EnsureHandle() ещё не вызывался
            // вовсе (не должно происходить, раз таймер уже тикает — но
            // безопаснее считать это тем же "нечем восстанавливать", чем
            // упасть чуть ниже на GetClientRect/SetWindowPos с нулевым hwnd).
            _repositionTimer.Stop();
            Lost?.Invoke();
            return;
        }

        var tray = Win32.FindWindow(TrayClassName, null);
        if (tray == nint.Zero) return; // таскбар временно недоступен (explorer между завершением и стартом) — оставляем прежнюю геометрию до следующего тика

        if (_attached && _attachedTrayHwnd != nint.Zero && (_attachedTrayHwnd != tray || !Win32.IsWindow(_attachedTrayHwnd)))
        {
            // Наш HWND жив (проверка выше не сработала), но Shell_TrayWnd, к
            // которому мы прицеплены, — уже не тот (explorer пересоздал его
            // под новым хэндлом) или вовсе мёртв. Перепривязываемся к
            // текущему таскбару тем же путём, что и первый TryAttach —
            // включая откат на оверлей, если по какой-то причине новый
            // SetParent тоже не пройдёт. Если и это упадёт (наш HWND всё же
            // не переживёт следующую строчку) — исключение поймает Reposition().
            TryAttach();
            return;
        }

        var notify = Win32.FindWindowEx(tray, nint.Zero, TrayNotifyClassName, null);
        var dpi = Win32.GetDpiForWindow(tray);
        if (dpi == 0) dpi = 96;

        var contentWidthDip = _content.DesiredSize.Width + OuterPaddingDip * 2;
        var bandWidthPx = ToPhysical(contentWidthDip, dpi);
        var gapPx = ToPhysical(GapDip, dpi);

        if (_attached)
            RepositionAttached(tray, notify, dpi, bandWidthPx, gapPx);
        else
            RepositionOverlay(tray, notify, dpi, bandWidthPx, gapPx);
    }

    private void RepositionAttached(nint tray, nint notify, uint dpi, int bandWidthPx, int gapPx)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;

        Win32.GetClientRect(tray, out var trayClient);
        var bandHeightPx = trayClient.Bottom - trayClient.Top;
        if (bandHeightPx <= 0) bandHeightPx = ToPhysical(DefaultHeightDip, dpi);

        int x;
        if (notify != nint.Zero && Win32.GetWindowRect(notify, out var notifyRect))
        {
            var topLeft = new Win32.POINT { X = notifyRect.Left, Y = notifyRect.Top };
            Win32.ScreenToClient(tray, ref topLeft);
            x = topLeft.X - gapPx - bandWidthPx;
        }
        else
        {
            // TrayNotifyWnd не нашёлся (нестандартная сборка explorer) —
            // правый край клиентской области таскбара как более грубая, но
            // безопасная оценка того же самого места.
            x = trayClient.Right - trayClient.Left - bandWidthPx - gapPx;
        }
        x = Math.Max(0, x);

        // HWND_TOP, не SWP_NOZORDER: вставляем себя в начало Z-порядка среди
        // детей Shell_TrayWnd — на случай если наш дочерний HWND физически
        // рисуется, но перекрыт/затенён другим композиционным слоем шелла
        // (composited поверх нас, несмотря на формально более позднее
        // создание нашего окна).
        Win32.SetWindowPos(hwnd, Win32.HwndTop, x, 0, bandWidthPx, bandHeightPx, Win32.SwpNoActivate);
    }

    private void RepositionOverlay(nint tray, nint notify, uint dpi, int bandWidthPx, int gapPx)
    {
        if (!Win32.GetWindowRect(tray, out var trayRect)) return;
        var bandHeightPx = trayRect.Bottom - trayRect.Top;

        int xPhysical;
        if (notify != nint.Zero && Win32.GetWindowRect(notify, out var notifyRect))
            xPhysical = notifyRect.Left - gapPx - bandWidthPx;
        else
            xPhysical = trayRect.Right - bandWidthPx - gapPx;

        // Оверлей — самостоятельное top-level WPF окно: позиционируем через
        // Left/Top/Width/Height в DIP, а не SetWindowPos. Начиная с .NET
        // Core 3 WPF корректно учитывает DPI монитора, на котором окно
        // физически оказывается — конвертируем через DPI самого таскбара
        // (тот монитор, где он есть, обычно и есть искомый монитор).
        Left = ToDip(xPhysical, dpi);
        Top = ToDip(trayRect.Top, dpi);
        Width = ToDip(bandWidthPx, dpi);
        Height = ToDip(bandHeightPx, dpi);
    }

    /// <summary>Переключает WS_CHILD/WS_POPUP — обязательный шаг после
    /// SetParent (и его отката): Windows не делает это автоматически, а без
    /// него дочернее окно продолжает вести себя как independent popup
    /// (task-17-brief.md: важная деталь SetParent+WPF).</summary>
    private static void SetChildStyle(nint hwnd, bool isChild)
    {
        var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GwlStyle);
        style = isChild ? (style & ~Win32.WsPopup) | Win32.WsChild : (style & ~Win32.WsChild) | Win32.WsPopup;
        Win32.SetWindowLongPtr(hwnd, Win32.GwlStyle, (nint)style);

        // SWP_FRAMECHANGED — пересчитать неклиентскую область под новый
        // стиль немедленно, а не откладывать до следующего перемещения/ресайза.
        Win32.SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
            Win32.SwpNoZOrder | Win32.SwpNoActivate | Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpFrameChanged);
    }

    private static int ToPhysical(double dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);

    private static double ToDip(int physical, uint dpi) => physical * 96.0 / dpi;
}

/// <summary>
/// Содержимое ленты: горизонтальный ряд колонок «метка над значением»,
/// нарисованный вручную через <see cref="OnRender"/> — тот же подход, что и
/// у циферблатов виджета (DialControl/StatusDialControl), переиспользующий
/// DialText.Format/DrawStackCentered, а не StackPanel из TextBlock (проще
/// точно посчитать ширину каждой колонки для Reposition, чем заводить лишние
/// алиасы под StackPanel/TextBlock, у которых есть тёзки в System.Windows.Forms).
/// </summary>
internal sealed class TaskbarBandContent : FrameworkElement
{
    private const double LabelFontSize = 10;
    private const double ValueFontSize = 14;
    private const double LineSpacingDip = 1;
    private const double ColumnSpacingDip = 14;

    private IReadOnlyList<TrayMetric> _metrics = Array.Empty<TrayMetric>();
    private double[] _columnWidths = Array.Empty<double>();

    public void SetMetrics(IReadOnlyList<TrayMetric> metrics)
    {
        _metrics = metrics;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var pixelsPerDip = DialText.PixelsPerDip(this);
        var widths = new double[_metrics.Count];
        double totalWidth = 0;

        for (var i = 0; i < _metrics.Count; i++)
        {
            var metric = _metrics[i];
            var labelText = DialText.Format(metric.Label, LabelFontSize, Theme.LabelWeight, Brushes.White, pixelsPerDip);
            var valueText = DialText.Format(metric.Value, ValueFontSize, Theme.ValueWeight, Brushes.White, pixelsPerDip);
            var width = Math.Max(labelText.Width, valueText.Width);
            widths[i] = width;

            totalWidth += width;
            if (i > 0) totalWidth += ColumnSpacingDip;
        }

        _columnWidths = widths;

        var height = double.IsInfinity(availableSize.Height)
            ? LabelFontSize + LineSpacingDip + ValueFontSize
            : availableSize.Height;
        return new Size(totalWidth, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pixelsPerDip = DialText.PixelsPerDip(this);
        var y = ActualHeight / 2;
        var x = 0.0;

        for (var i = 0; i < _metrics.Count; i++)
        {
            var metric = _metrics[i];
            var width = i < _columnWidths.Length ? _columnWidths[i] : 0;

            var labelText = DialText.Format(metric.Label, LabelFontSize, Theme.LabelWeight, Brushes.White, pixelsPerDip);
            var valueText = DialText.Format(metric.Value, ValueFontSize, Theme.ValueWeight, Brushes.White, pixelsPerDip);

            DialText.DrawStackCentered(dc, new Point(x + width / 2, y), LineSpacingDip, labelText, valueText);

            x += width + ColumnSpacingDip;
        }
    }
}
