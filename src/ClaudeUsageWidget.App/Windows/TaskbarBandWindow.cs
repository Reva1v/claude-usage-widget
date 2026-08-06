using System.IO;
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
/// Полоска в панели задач: маленькое окно слева от области переполнения
/// трея (или у левого края таскбара — см. <see cref="SetPosition"/>),
/// рисующее колонки «метка над значением» как в меню-баре macOS.
///
/// Техника — top-level окно-владелец (owned window) таскбара, НЕ дочернее
/// (WS_CHILD) окно и не голый Topmost. Первая версия этой задачи пробовала
/// оба других варианта:
/// - SetParent-встраивание как WS_CHILD в Shell_TrayWnd (техника
///   TrafficMonitor) технически удаётся (ненулевой возврат, точное
///   позиционирование), но живая проверка на Windows 11 показала, что
///   Mica-композитинг таскбара делает содержимое такого дочернего окна
///   нечитаемым — пиксельные замеры (round 2, task-17-report.md) показали,
///   что ни смена порядка WS_CHILD/SetParent, ни WS_EX_LAYERED, ни
///   Z-порядок этого не чинят.
/// - Голый Topmost-оверлей без владельца рендерится чётко (тот же
///   WS_EX_LAYERED там работал), но периодически проваливался обратно за
///   Mica-слой шелла без видимой причины (round 4) и не имел механизма
///   держаться выше таскбара при активности шелла (контекстные меню,
///   переключение фокуса).
/// Owned window (SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, Shell_TrayWnd))
/// решает оба: окно остаётся обычным top-level (никакого child-композитинга
/// — можно использовать честную WPF-прозрачность, AllowsTransparency=true,
/// без чёрных прямоугольников и без проваливания), а Windows сама
/// поддерживает инвариант "owned-окно всегда выше своего владельца".
/// HWND_TOPMOST поверх этого нужен только чтобы встать выше ВООБЩЕ всех
/// обычных окон (не только выше конкретно таскбара) — и выставляется РОВНО
/// ОДИН РАЗ, при (пере)стыковке, а не на каждый тик перепозиционирования:
/// периодическая переустановка HWND_TOPMOST утаскивает наверх весь кластер
/// "владелец+owned" (то есть сам таскбар) поверх любого открытого в этот
/// момент контекстного меню шелла и обрезает его — задокументированное
/// поведение, независимо переоткрытое NetSpeedTray (их issue #200:
/// 23 попытки из 23 с периодическим SetWindowPos(HWND_TOPMOST, ...) обрезали
/// меню, 0 из 23 без него).
/// </summary>
public sealed class TaskbarBandWindow : Window
{
    private const string TrayClassName = "Shell_TrayWnd";
    private const string TrayNotifyClassName = "TrayNotifyWnd";

    /// Отступ ленты от области переполнения трея — task-17-brief.md: «встать
    /// левее её на ширину окна с отступом 8 px».
    private const double GapDip = 8;

    private const double OuterPaddingDip = 8;

    /// Отступ от левого края таскбара для BandPosition="left" — живая
    /// проверка (task-17-report.md, round 5) показала лишний отступ ~130px
    /// вместо ожидаемого «впритык к краю»: было 160 DIP, унаследованные из
    /// более ранней идеи встать сразу после Start/Search/Task View/Widgets
    /// при выравнивании таскбара "Слева". Пользователь имел в виду буквально
    /// левый край — тот же зазор, что и GapDip у позиции "tray" (там 8 px от
    /// TrayNotifyWnd), просто с другой стороны экрана, а не отступ вслед за
    /// скрытыми системными кнопками.
    private const double LeftPositionOffsetDip = GapDip;

    /// Высота таскбара Windows 10/11 по умолчанию при масштабе 100% —
    /// используется только как временное значение до первого вызова
    /// <see cref="Reposition"/> (тот вызывается раньше, чем окно становится
    /// видимым, так что реальный размер обычно подставляется ещё до показа).
    private const double DefaultHeightDip = 40;

    /// Доли ширины таскбара, в которых зонд видимости
    /// (<see cref="IsTaskbarObscured"/>) берёт пробы. Разнесены по полосе:
    /// одна точка может быть законно накрыта (флайаут громкости над часами,
    /// наша собственная лента слева или у трея) — все три разом накрывает
    /// только окно, реально лежащее ПОВЕРХ всей полосы таскбара.
    private static readonly double[] ProbeFractions = [0.35, 0.55, 0.8];

    private readonly TaskbarBandContent _content;
    private readonly DispatcherTimer _repositionTimer;

    /// Делегат для SetWinEventHook — ОБЯЗАН жить в поле, а не быть временным
    /// значением на вызове: нативный код держит только указатель на функцию,
    /// без managed-ссылки, так что без этого поля GC вправе собрать делегат
    /// в любой момент между установкой хука и первым же событием —
    /// классическая тихая P/Invoke-ловушка (колбэк вызывается через уже
    /// освобождённую память → падение либо тихая нерабочая доставка событий).
    private readonly Win32.WinEventDelegate _winEventProc;

    /// EVENT_SYSTEM_FOREGROUND — смена активного окна.
    private nint _foregroundHook;

    /// EVENT_OBJECT_LOCATIONCHANGE — перемещение/ресайз foreground-окна
    /// (ловит F11/безрамочный fullscreen без смены активного окна).
    private nint _locationHook;

    /// EVENT_SYSTEM_MINIMIZESTART..MINIMIZEEND — сворачивание/разворачивание
    /// окон: после «Свернуть» смена foreground приходит не всегда и не сразу,
    /// а зонд должен пересчитаться немедленно (живой баг: мигание ленты на
    /// кнопке «Свернуть»).
    private nint _minimizeHook;

    /// Дребезг для EVENT_OBJECT_LOCATIONCHANGE — тот сыплется пачками во
    /// время обычного перетаскивания/анимации окна, а не только при входе/
    /// выходе из fullscreen; реально перепроверяем полноэкранность только
    /// спустя ~200 мс тишины после последнего такого события.
    private readonly DispatcherTimer _locationDebounceTimer;

    /// Гистерезис применения самой видимости — отдельно от дребезга
    /// LOCATIONCHANGE выше (тот решает КОГДА перепроверить, этот — стоит ли
    /// уже ДЕЙСТВОВАТЬ на результат проверки). Живая проверка (round 7)
    /// показала, что мимолётная смена foreground-окна (случайный alt-tab,
    /// всплывающее окно поверх игры на долю секунды) иначе заставляла
    /// ленту мигать туда-обратно — цель "мигать по минимуму" требует не
    /// применять Hide()/Show() немедленно на каждое сырое определение, а
    /// только когда желаемое состояние продержалось стабильным ~300 мс. См.
    /// RequestFullscreenVisibility.
    private readonly DispatcherTimer _visibilityStabilityTimer;

    /// Гистерезис видимости, по направлениям. Показ — быстрый: лента и так
    /// не видна, вернуть её пользователю надо как можно раньше. Скрытие —
    /// намеренно медленное: короткоживущие полноэкранные оверлеи (ShareX
    /// на каждое разворачивание чужого окна перекрывает таскбар невидимым
    /// окном на ~0.3-1с) живут заметно меньше этого порога и не должны
    /// доживать до реального Hide() вовсе; настоящий fullscreen (игра)
    /// держится минутами, и лишняя секунда ленты поверх него — приемлемая
    /// цена за полное отсутствие миганий.
    private const int ShowStabilityMs = 300;
    private const int HideStabilityMs = 1200;

    /// Состояние, которое сейчас ожидает применения через
    /// _visibilityStabilityTimer — null, если ничего не отложено (последнее
    /// запрошенное состояние уже совпадает с применённым).
    private bool? _pendingHiddenForFullscreen;

    /// Ещё ни разу не определяли fullscreen-состояние для этого Dock() —
    /// см. why-comment в RequestFullscreenVisibility: самое первое
    /// определение применяется немедленно, в обход 300мс гистерезиса (тот
    /// защищает УЖЕ показанную ленту от мигания, а не откладывает
    /// единственную, ещё никому не видимую установку начального
    /// состояния).
    private bool _fullscreenStateEstablished;

    /// "tray" (по умолчанию) или "left" — см. <see cref="SetPosition"/>.
    private string _position = "tray";

    /// Лента спрятана из-за полноэкранного приложения поверх её монитора —
    /// см. IsTaskbarObscured()/RepositionCore(). Отдельно от обычной
    /// Visibility: не хотим, чтобы обычная логика показа/скрытия путала это
    /// состояние с "лента выключена пользователем" — здесь просто временная
    /// приостановка показа.
    private bool _hiddenForFullscreen;

    /// Последняя геометрия, реально применённая через SetWindowPos в
    /// RepositionCore() — см. why-comment там: используется, чтобы пропускать
    /// SetWindowPos на тиках, где ничего не изменилось (перф). int.MinValue —
    /// заведомо непохоже ни на один настоящий x/y/размер, поэтому самый
    /// первый вызов всегда проходит без специальной ветки на "ещё не было".
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private int _lastWidthPx = int.MinValue;
    private int _lastHeightPx = int.MinValue;

    /// <summary>
    /// Нативный HWND этого окна уничтожен не через наш собственный Detach()/
    /// Close(). Owned window (в отличие от прежнего WS_CHILD-варианта) не
    /// уничтожается автоматически вместе с владельцем — Windows каскадно
    /// рушит только настоящих ДЕТЕЙ (WS_CHILD), а не owned-окна, так что этот
    /// сценарий стал заметно менее вероятным, чем в первых раундах задачи, но
    /// не невозможным (WPF способна закрыть Window и по другим причинам) —
    /// оставлено как защитный бэкстоп. WPF не поддерживает повторный
    /// Show()/EnsureHandle() у Window, чей HWND пропал таким образом —
    /// единственный рабочий путь восстановления это новый экземпляр
    /// TaskbarBandWindow, поэтому здесь только сигнал, пересоздание — на
    /// стороне владельца (App.xaml.cs).
    /// </summary>
    public event Action? Lost;

    public TaskbarBandWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;

        // Честная WPF-прозрачность: окно больше не переезжает в чужой
        // процесс (ни SetParent, ни WS_CHILD), так что кросс-процессный
        // layered-window баг ("рисует чёрный прямоугольник"), из-за которого
        // первая версия задачи держала окно непрозрачным, здесь не
        // применяется — это обычное top-level окно, просто с владельцем.
        // AllowsTransparency обязан быть выставлен до создания HWND (то есть
        // здесь, в конструкторе, а не позже).
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        _content = new TaskbarBandContent();
        var root = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(OuterPaddingDip, 0, OuterPaddingDip, 0),
            Child = _content,
        };
        Content = root;

        Height = DefaultHeightDip;

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _repositionTimer.Tick += (_, _) => Reposition();

        _winEventProc = OnWinEvent;
        _locationDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _locationDebounceTimer.Tick += (_, _) =>
        {
            _locationDebounceTimer.Stop();
            Reposition();
        };

        _visibilityStabilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShowStabilityMs) };
        _visibilityStabilityTimer.Tick += (_, _) =>
        {
            _visibilityStabilityTimer.Stop();
            if (_pendingHiddenForFullscreen is not { } hidden) return;
            _pendingHiddenForFullscreen = null;
            try
            {
                // Свежий зонд В МОМЕНТ истечения таймера, а не вердикт на
                // момент его старта. Живой пример с этой машины: ShareX при
                // разворачивании чужих окон кладёт невидимое WinForms-окно на
                // всю полосу таскбара на доли секунды — вердикт «накрыто»
                // честен в момент снятия, но к истечению таймера оверлей уже
                // исчез, а событий, которые перезапустили бы зонд в этом
                // промежутке, нет (уничтожение окна не приходит как
                // LOCATIONCHANGE). Применять устаревший вердикт — мигать
                // лентой на ровном месте; не подтвердился — переход просто
                // отменяется, и следующий начнётся с чистого листа.
                var ownHwnd = new WindowInteropHelper(this).Handle;
                var tray = Win32.FindWindow(TrayClassName, null);
                if (ownHwnd == nint.Zero || tray == nint.Zero) return;
                var fresh = IsTaskbarObscured(ownHwnd, tray);
                if (fresh != hidden)
                {
                    Diag($"stability expired: verdict flipped ({hidden} -> {fresh}), transition cancelled");
                    return;
                }
                ApplyFullscreenVisibility(hidden);
            }
            catch (InvalidOperationException)
            {
                // Тот же зомби-сценарий, что и в Reposition()/Detach() (см.
                // их комментарии): в отличие от вызова из RepositionCore(),
                // этот Tick не проходит через try/catch Reposition() — окно
                // могло быть закрыто, пока переход ждал 300мс гистерезиса,
                // и необработанный InvalidOperationException здесь уронил
                // бы весь процесс.
                _repositionTimer.Stop();
                UnhookFullscreenEvents();
                Lost?.Invoke();
            }
        };
    }

    /// <summary>
    /// Показывает ленту и (пере)стыкует её с таскбаром — владелец
    /// (GWLP_HWNDPARENT) выставляется внутри Reposition()/RepositionCore(),
    /// который сам обнаруживает "владелец не тот/не выставлен" как частный
    /// случай устаревшего состояния (см. её комментарий) — здесь достаточно
    /// создать HWND и вызвать её один раз. Единственная точка входа для
    /// App.xaml.cs.
    /// </summary>
    public void Dock()
    {
        new WindowInteropHelper(this).EnsureHandle();
        _hiddenForFullscreen = false;
        _fullscreenStateEstablished = false;

        HookFullscreenEvents();
        Reposition();

        // Условно, а не всегда: Reposition() выше уже могла синхронно
        // спрятать окно (самое первое определение fullscreen-состояния
        // применяется немедленно — см. RequestFullscreenVisibility), и
        // безусловный Show() здесь до раунда 7 сводил на нет как раз этот
        // случай — окно, только что спрятанное как fullscreen, тут же
        // показывалось бы обратно.
        if (!_hiddenForFullscreen) Show();
        _repositionTimer.Start();
    }

    /// <summary>
    /// Меняет "tray"/"left" и сразу перепозиционируется, не дожидаясь
    /// следующего тика — заметная задержка при живом переключении из меню
    /// трея выглядела бы как баг. Не трогает владельца/HWND_TOPMOST: это
    /// только смена X, обычный (не "устаревший") путь в RepositionCore().
    /// </summary>
    public void SetPosition(string position)
    {
        if (_position == position) return;
        _position = position;
        if (_repositionTimer.IsEnabled) Reposition();
    }

    /// <summary>
    /// Снимает владельца и прячет окно. Идемпотентно: безопасно вызывать
    /// даже если окно ещё ни разу не показывалось.
    /// </summary>
    public void Detach()
    {
        _repositionTimer.Stop();
        UnhookFullscreenEvents();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero || !Win32.IsWindow(hwnd))
        {
            // Зомби — тот же случай, что и в RepositionCore(): дальше
            // трогать Visibility/Hide() на закрытом WPF Window значило бы
            // поймать InvalidOperationException. Таймер уже остановлен
            // строкой выше, значит Reposition() больше не поднимет Lost сам
            // — сигналим отсюда, иначе владелец (App.xaml.cs) останется с
            // мёртвым экземпляром, который упадёт на следующем Dock().
            Lost?.Invoke();
            return;
        }

        Win32.SetWindowLongPtr(hwnd, Win32.GwlpHwndParent, nint.Zero);

        try
        {
            Hide();
        }
        catch (InvalidOperationException)
        {
            // Защитный бэкстоп на гонку между проверкой IsWindow выше и
            // вызовом Hide() ниже (WPF успела пометить Window закрытым
            // именно в этом промежутке) — тот же вывод: экземпляр мёртв.
            Lost?.Invoke();
        }
    }

    /// <summary>Перерисовывает колонки свежими метриками — источник тот же
    /// <see cref="TrayMetric"/>, что и у иконки трея (TrayText.Metrics).</summary>
    public void Render(IReadOnlyList<TrayMetric> metrics)
    {
        _content.SetMetrics(metrics);

        // Измерение контента НЕ делается здесь (раньше — делалось, сразу
        // после SetMetrics) — оно намеренно перенесено целиком в
        // RepositionCore() (см. её комментарий про "F"/"5"-баг): App.
        // SetTaskbarBandVisible вызывает Render() ДО Dock()/EnsureHandle(),
        // то есть в момент, когда у окна ещё может не быть HWND и
        // PresentationSource — DialText.PixelsPerDip(_content) в этот
        // момент не знает реальный DPI монитора, на котором окно окажется, и
        // намеренный синхронный Measure() тут посчитал бы ширину по
        // неверному DPI. RepositionCore() всегда выполняется уже после
        // EnsureHandle() и сама заново измеряет контент непосредственно
        // перед тем, как прочитать DesiredSize — единственное место, где
        // измерение действительно нужно.
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Не активируется и не появляется в alt-tab. НЕ выставляем
        // WS_EX_LAYERED вручную и НЕ зовём SetLayeredWindowAttributes:
        // AllowsTransparency=true уже сделала окно layered сама (это ровно
        // то, как WPF реализует поканальную прозрачность на Win32), и его
        // собственный UpdateLayeredWindow-конвейер ломается, если поверх
        // него дополнительно вызвать LWA_ALPHA — два независимых механизма
        // управления одной и той же layered-поверхностью конфликтуют.
        var exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle);
        exStyle |= Win32.WsExNoActivate | Win32.WsExToolWindow;
        Win32.SetWindowLongPtr(hwnd, Win32.GwlExStyle, (nint)exStyle);

        var source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HwndSource is not available after SourceInitialized.");
        source.AddHook(WndProc);
    }

    /// <summary>WM_DPICHANGED — перепозиционируемся сразу, не дожидаясь
    /// следующего тика таймера.</summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WmDpiChanged) Reposition();
        return nint.Zero;
    }

    /// <summary>
    /// Устанавливает системные (idProcess=0/idThread=0 — весь компьютер, не
    /// только наш процесс) WinEvent-хуки, чтобы реагировать на вход/выход из
    /// fullscreen мгновенно, а не ждать следующего 5-секундного тика —
    /// живая проверка (task-17-report.md, round 6) показала заметную
    /// задержку до 5 с в обе стороны при чисто тик-based детекте.
    /// EVENT_SYSTEM_FOREGROUND — сменилось активное окно (обычный Alt-Tab/
    /// запуск игры). EVENT_OBJECT_LOCATIONCHANGE — окно поменяло размер/
    /// положение БЕЗ смены активного окна (F11 в том же окне, переключение
    /// эксклюзивный/безрамочный fullscreen той же игры) — единственный
    /// способ поймать этот случай, раз foreground-окно не меняется.
    /// WINEVENT_OUTOFCONTEXT — без инжекции DLL в чужие процессы, события
    /// доставляются в поток-установщик хука (наш UI-поток) через тот же
    /// насос сообщений, что качает WPF Dispatcher. Идемпотентно: Dock()
    /// может вызываться повторно (редок после Detach), хук устанавливается
    /// только если ещё не установлен.
    /// </summary>
    private void HookFullscreenEvents()
    {
        if (_foregroundHook != nint.Zero) return;

        _foregroundHook = Win32.SetWinEventHook(
            Win32.EventSystemForeground, Win32.EventSystemForeground,
            nint.Zero, _winEventProc, 0, 0, Win32.WinEventOutOfContext);
        _locationHook = Win32.SetWinEventHook(
            Win32.EventObjectLocationChange, Win32.EventObjectLocationChange,
            nint.Zero, _winEventProc, 0, 0, Win32.WinEventOutOfContext);
        _minimizeHook = Win32.SetWinEventHook(
            Win32.EventSystemMinimizeStart, Win32.EventSystemMinimizeEnd,
            nint.Zero, _winEventProc, 0, 0, Win32.WinEventOutOfContext);
    }

    /// <summary>Снимает оба хука (если стоят) и гасит оба вспомогательных
    /// таймера (дребезг LOCATIONCHANGE и гистерезис видимости) — вызывается
    /// из Detach() и из всех мест, где экземпляр объявляется мёртвым (см.
    /// Lost), чтобы не оставлять хук, доставляющий события делегату,
    /// который больше никому не нужен, и не применить отложенную видимость
    /// на уже неактуальном экземпляре.</summary>
    private void UnhookFullscreenEvents()
    {
        if (_foregroundHook != nint.Zero)
        {
            Win32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = nint.Zero;
        }
        if (_locationHook != nint.Zero)
        {
            Win32.UnhookWinEvent(_locationHook);
            _locationHook = nint.Zero;
        }
        if (_minimizeHook != nint.Zero)
        {
            Win32.UnhookWinEvent(_minimizeHook);
            _minimizeHook = nint.Zero;
        }
        _locationDebounceTimer.Stop();
        _visibilityStabilityTimer.Stop();
        _pendingHiddenForFullscreen = null;
    }

    /// <summary>Запрашивает желаемую видимость по свежему fullscreen-детекту
    /// — не применяет её напрямую (кроме самого первого раза, см. ниже):
    /// - если <paramref name="hidden"/> уже совпадает с применённым
    ///   состоянием (<see cref="_hiddenForFullscreen"/>), отменяет любой
    ///   незавершённый переход и ничего не делает — "immediate application
    ///   is fine when desired == current".
    /// - если это НОВЫЙ переход (не тот, что уже ожидает применения),
    ///   (пере)запускает таймер стабильности (ShowStabilityMs/HideStabilityMs
    ///   — см. их комментарий про асимметрию) — реальный Hide()/Show()
    ///   произойдёт только если СВЕЖИЙ зонд в момент истечения таймера
    ///   подтвердит всё то же желаемое состояние (см. Tick в конструкторе).
    ///   Мимолётная смена foreground-окна или короткоживущий оверлей поверх
    ///   таскбара поэтому гасятся здесь и не доходят до мигания ленты.
    /// - самое первое определение состояния для этого Dock()
    ///   (<see cref="_fullscreenStateEstablished"/> ещё false) применяется
    ///   немедленно, в обход гистерезиса: тот защищает уже показанную ленту
    ///   от мигания между двумя состояниями, а не единственную, ещё никому
    ///   не видимую установку начального состояния.
    /// </summary>
    private void RequestFullscreenVisibility(bool hidden)
    {
        if (!_fullscreenStateEstablished)
        {
            _fullscreenStateEstablished = true;
            ApplyFullscreenVisibility(hidden);
            return;
        }

        if (hidden == _hiddenForFullscreen)
        {
            _pendingHiddenForFullscreen = null;
            _visibilityStabilityTimer.Stop();
            return;
        }

        if (_pendingHiddenForFullscreen == hidden) return; // уже ждём именно этого перехода

        _pendingHiddenForFullscreen = hidden;
        _visibilityStabilityTimer.Stop();
        // Асимметрия направлений: скрытие — редкое и «дорогое» решение
        // (пользователь теряет ленту из виду), мимолётные оверлеи должны
        // отфильтровываться целиком, поэтому ждём дольше; показ обратно —
        // безобиден, задерживать его дольше 300мс незачем.
        _visibilityStabilityTimer.Interval = TimeSpan.FromMilliseconds(hidden ? HideStabilityMs : ShowStabilityMs);
        _visibilityStabilityTimer.Start();
    }

    /// <summary>Собственно Hide()/Show() — единственное место, которое их
    /// вызывает по fullscreen-причине (см. вызовы из
    /// RequestFullscreenVisibility и из таймера гистерезиса в
    /// конструкторе).</summary>
    private void ApplyFullscreenVisibility(bool hidden)
    {
        if (hidden == _hiddenForFullscreen) return;
        Diag($"apply: hiddenForFullscreen {_hiddenForFullscreen} -> {hidden}");
        _hiddenForFullscreen = hidden;
        if (hidden) Hide(); else Show();
    }

    /// <summary>Колбэк системного WinEvent-хука — вызывается нативным кодом
    /// изнутри насоса сообщений нашего же UI-потока, но не полагаемся на
    /// это как на документированную гарантию: маршалим через
    /// Dispatcher.BeginInvoke, а не делаем что-либо содержательное прямо в
    /// кадре низкоуровневого системного колбэка. BeginInvoke, а не Invoke —
    /// колбэк должен вернуть управление ОС максимально быстро.</summary>
    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint idEventTime)
    {
        if (eventType is Win32.EventSystemForeground
            or Win32.EventSystemMinimizeStart
            or Win32.EventSystemMinimizeEnd)
        {
            Dispatcher.BeginInvoke(Reposition);
            return;
        }

        if (eventType != Win32.EventObjectLocationChange) return;

        // OBJID_WINDOW/CHILDID_SELF — событие про само окно целиком, не про
        // один из его внутренних элементов управления (хук системный, шлёт
        // события по всем окнам всех процессов — без этого фильтра здесь
        // тонуло бы в шуме).
        if (idObject != Win32.ObjIdWindow || idChild != Win32.ChildIdSelf) return;

        Dispatcher.BeginInvoke(() =>
        {
            // Интересует только СЕЙЧАС активное окно — хук системный,
            // событие могло прилететь про любое окно где угодно.
            if (hwnd != Win32.GetForegroundWindow()) return;

            // Дребезг: во время обычного перетаскивания/анимации окна таких
            // событий летят десятки — переоткладываем таймер на 200 мс от
            // каждого нового, реально проверяем полноэкранность только
            // когда окно ~200 мс не двигалось.
            _locationDebounceTimer.Stop();
            _locationDebounceTimer.Start();
        });
    }

    /// <summary>Пересчитывает позицию/размер и (при необходимости)
    /// перепристыковывает к таскбару — ищет таскбар и область трея заново
    /// при каждом вызове (не кэширует хэндлы для самого поиска):
    /// explorer.exe может пересоздать Shell_TrayWnd, а простои/добавление
    /// иконок в трее двигают TrayNotifyWnd — task-17-brief.md: «таскбар
    /// перестраивается». Первым делом — самопроверка на уничтожение
    /// собственного HWND, затем — гвард на полноэкранное приложение поверх
    /// нашего монитора, затем — дешёвая проверка "наш владелец всё ещё
    /// текущий Shell_TrayWnd" (см. комментарий у RepositionCore
    /// ниже).</summary>
    private void Reposition()
    {
        try
        {
            RepositionCore();
        }
        catch (InvalidOperationException)
        {
            // WPF сама уже считает это Window закрытым — какая-то операция
            // ниже (например Show() после выхода из fullscreen) упала с
            // InvalidOperationException("...после закрытия окна"). Экземпляр
            // необратимо мёртв — сигналим наружу вместо попытки продолжить.
            _repositionTimer.Stop();
            UnhookFullscreenEvents();
            Lost?.Invoke();
        }
    }

    private void RepositionCore()
    {
        var ownHwnd = new WindowInteropHelper(this).Handle;
        if (ownHwnd == nint.Zero || !Win32.IsWindow(ownHwnd))
        {
            // Наш собственный HWND уничтожен извне — см. doc-comment у Lost.
            // ownHwnd == Zero тоже сюда: EnsureHandle() ещё не вызывался
            // вовсе (не должно происходить, раз таймер уже тикает — но
            // безопаснее считать это тем же "нечем восстанавливать", чем
            // упасть чуть ниже на SetWindowPos с нулевым hwnd).
            _repositionTimer.Stop();
            UnhookFullscreenEvents();
            Lost?.Invoke();
            return;
        }

        var tray = Win32.FindWindow(TrayClassName, null);
        if (tray == nint.Zero) return; // таскбар временно недоступен (explorer между завершением и стартом) — оставляем прежнюю геометрию и видимость до следующего тика

        // Видимость ленты = фактическая видимость таскбара, и ничего больше.
        // Три поколения эвристик «а не fullscreen ли foreground-окно» (rect
        // vs rcMonitor, SW_SHOWMAXIMIZED, сравнение мониторов — round 6-8)
        // раз за разом ловили ложные срабатывания на реальных приложениях
        // (maximized JetBrains, Toggle Full Screen Mode, кнопка «Свернуть»).
        // Зонд IsTaskbarObscured спрашивает у самой ОС, чьи окна реально
        // лежат в точках полосы таскбара — определение видимости, а не её
        // предсказание. Применение — через RequestFullscreenVisibility
        // (round 7: 300мс гистерезис против мигания на мимолётных сменах).
        RequestFullscreenVisibility(IsTaskbarObscured(ownHwnd, tray));
        if (_hiddenForFullscreen) return; // репозиционировать спрятанное (в т.ч. ещё не отпущенное гистерезисом обратно) окно незачем

        // Дешёвая проверка "устарели ли мы" — вместо кэширования хэндла
        // таскбара как раньше, читаем ТЕКУЩЕГО владельца прямо из окна:
        // явно свежий источник истины, и заодно правильно ловит самый
        // первый вызов (свежесозданный HWND ещё не имеет владельца, то есть
        // GWLP_HWNDPARENT=0 != tray — естественно читается как "устарело",
        // без отдельной ветки на "самый первый раз").
        var currentOwner = Win32.GetWindowLongPtr(ownHwnd, Win32.GwlpHwndParent);
        var stale = currentOwner != tray;

        if (stale)
        {
            // explorer.exe пересоздал Shell_TrayWnd (перезапуск) — или это
            // самый первый Dock(). Перепривязываем владельца. HWND_TOPMOST
            // переустанавливается ниже, вместе с позиционированием, ОДИН РАЗ
            // — не на каждый последующий тик (см. doc-comment класса про то,
            // почему периодическая переустановка ломает контекстные меню
            // шелла).
            Win32.SetWindowLongPtr(ownHwnd, Win32.GwlpHwndParent, tray);
        }

        var notify = Win32.FindWindowEx(tray, nint.Zero, TrayNotifyClassName, null);
        var dpi = Win32.GetDpiForWindow(tray);
        if (dpi == 0) dpi = 96;

        if (!Win32.GetWindowRect(tray, out var trayRect)) return;
        var bandHeightPx = trayRect.Bottom - trayRect.Top;

        // Измеряем контент ЗДЕСЬ, а не полагаемся на Render() (которая может
        // выполниться до EnsureHandle() — см. её комментарий): это гарантирует,
        // что DesiredSize всегда посчитан в том же DPI-контексте, в котором
        // OnRender затем реально рисует текст. Без этого при первом Dock()
        // ширина окна считалась по DPI "до присоединения" (возможен fallback
        // на системный DPI), а рисовался текст уже по настоящему DPI монитора
        // — на масштабе, отличном от 100%, они расходились, и третья колонка
        // обрезалась почти до одного символа ("FAB"/"53%" → "F"/"5",
        // task-17-report.md round 5, live-finding #2). Дёшево — пересчёт
        // ширины трёх FormattedText-колонок, а не перерисовка.
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var contentWidthDip = _content.DesiredSize.Width + OuterPaddingDip * 2;
        var bandWidthPx = ToPhysical(contentWidthDip, dpi);
        var gapPx = ToPhysical(GapDip, dpi);

        var x = _position == "left"
            ? trayRect.Left + ToPhysical(LeftPositionOffsetDip, dpi)
            : ComputeTrayPositionX(trayRect, notify, bandWidthPx, gapPx);

        // Пропускаем SetWindowPos целиком, если геометрия не поменялась ни на
        // пиксель (кроме "устаревшего" случая — там необходимо заново
        // перепривязать владельца/Z-order через SetWindowPos, даже если сами
        // x/y/размер совпали с прошлым разом): каждый вызов SetWindowPos — это
        // WM_WINDOWPOSCHANGED/WM_SIZE и, для layered-окна (AllowsTransparency
        // =true), повторная композиция DWM — недорого один раз, но незачем
        // платить эту цену каждые 5 секунд бесконечно, когда почти всегда
        // ничего не изменилось (task-17-report.md round 5, live-finding #3:
        // "очень заторможено").
        // Анти-burial — СТРОГО до геометрического короткого замыкания ниже:
        // захоронение (шелл поднял таскбар/чужое окно над нами) происходит
        // ровно при неизменной ни на пиксель геометрии, и детект, стоявший
        // после этого return, в реальной жизни не вызывался вообще — лента
        // часами лежала под таскбаром при девственно чистом fullscreen-логе.
        // Для stale-ветки не нужен: она сама переустанавливает HWND_TOPMOST
        // вместе с перепривязкой владельца.
        if (!stale) EnsureNotBuried(ownHwnd, tray);

        if (!stale
            && x == _lastX && trayRect.Top == _lastY
            && bandWidthPx == _lastWidthPx && bandHeightPx == _lastHeightPx)
        {
            return;
        }

        var insertAfter = stale ? Win32.HwndTopMost : nint.Zero;
        var flags = stale ? Win32.SwpNoActivate : (Win32.SwpNoZOrder | Win32.SwpNoActivate);
        Win32.SetWindowPos(ownHwnd, insertAfter, x, trayRect.Top, bandWidthPx, bandHeightPx, flags);
        _lastX = x;
        _lastY = trayRect.Top;
        _lastWidthPx = bandWidthPx;
        _lastHeightPx = bandHeightPx;
    }

    /// <summary>
    /// Возвращает ленту наверх, если её похоронили по Z-порядку — и ТОЛЬКО
    /// тогда (не периодически: см. doc-comment класса про NetSpeedTray #200).
    ///
    /// Живой сценарий (2026-08-06): пользователь разворачивает приложение
    /// (maximized или AWT-фуллскрин с видимым таскбаром) — шелл гасит
    /// topmost-слой на время «rude»-состояния, затем поднимает таскбар
    /// SetWindowPos'ом c SWP_NOOWNERZORDER, то есть БЕЗ owned-окон. Инвариант
    /// «owned всегда выше владельца» действует при обычном поднятии владельца,
    /// но не при NOOWNERZORDER — лента остаётся под окном приложения при
    /// видимом таскбаре. Симптом-подтверждение: клик по таскбару (обычное
    /// поднятие, уже С owned-окнами) возвращал ленту на глазах пользователя.
    ///
    /// Детект без хит-теста: WindowFromPoint не годится — лента прозрачная,
    /// и в прозрачном пикселе он честно вернёт то, что под ней, даже когда
    /// лента сверху. Вместо этого идём по цепочке GW_HWNDPREV (окна СТРОГО
    /// выше нас): любое видимое чужое окно, пересекающее наш прямоугольник,
    /// значит «нас перекрыли». Исключения: контекстные меню (#32768) и
    /// всплывашки — легитимные временные окна, re-assert поверх них — это
    /// ровно баг #200, их пропускаем (они закроются сами).
    /// </summary>
    private void EnsureNotBuried(nint ownHwnd, nint tray)
    {
        if (!Win32.GetWindowRect(ownHwnd, out var own)) return;

        var above = Win32.GetWindow(ownHwnd, Win32.GwHwndPrev);
        // Ограничитель обхода: topmost-слой обычно из единиц окон; 64 — с
        // запасом, и гарантия отсутствия вечного цикла на битой цепочке.
        for (var i = 0; above != nint.Zero && i < 64; i++, above = Win32.GetWindow(above, Win32.GwHwndPrev))
        {
            if (above == tray)
            {
                // Владелец ВЫШЕ owned-окна — инвариант owned-порядка сломан:
                // шелл поднял таскбар с SWP_NOOWNERZORDER, и непрозрачный
                // Shell_TrayWnd теперь рисуется поверх ленты — для глаза она
                // «пропала», хотя формально видима и не Hide()-нута (именно
                // так лента гасла при открытии/скрытии окон из трея —
                // fullscreen-путь в логе при этом девственно чист). Это то же
                // захоронение, что и под окном приложения, просто хоронит
                // сам владелец — и лечится тем же одиночным re-assert.
                Diag("buried under the taskbar itself — re-asserting topmost");
                Win32.SetWindowPos(ownHwnd, Win32.HwndTopMost, 0, 0, 0, 0,
                    Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);
                return;
            }
            if (!Win32.IsWindowVisible(above)) continue;
            if (!Win32.GetWindowRect(above, out var r)) continue;

            var overlaps = r.Left < own.Right && r.Right > own.Left
                && r.Top < own.Bottom && r.Bottom > own.Top;
            if (!overlaps) continue;

            var cls = Win32.GetClassName(above);
            if (cls is "#32768" or "Xaml_WindowedPopupClass") continue; // меню/флайауты — временные, не наш случай

            Diag($"buried under {above} cls={cls} — re-asserting topmost");
            Win32.SetWindowPos(ownHwnd, Win32.HwndTopMost, 0, 0, 0, 0,
                Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);
            return;
        }
    }

    private static int ComputeTrayPositionX(Win32.RECT trayRect, nint notify, int bandWidthPx, int gapPx)
    {
        if (notify != nint.Zero && Win32.GetWindowRect(notify, out var notifyRect))
            return notifyRect.Left - gapPx - bandWidthPx;

        // TrayNotifyWnd не нашёлся (нестандартная сборка explorer) — правый
        // край таскбара как более грубая, но безопасная оценка того же
        // самого места.
        return trayRect.Right - bandWidthPx - gapPx;
    }

    /// <summary>
    /// Перекрыт ли таскбар чужим окном — ground truth вместо предсказаний:
    /// зонд берёт три точки внутри полосы таскбара (см.
    /// <see cref="ProbeFractions"/>) и спрашивает у ОС, чьё top-level окно
    /// реально лежит в каждой (WindowFromPoint → GetAncestor(GA_ROOT)).
    /// Правила:
    /// - точка «за» таскбаром (корень — Shell_TrayWnd) или за нашей же
    ///   лентой (она легитимно висит над полосой) → таскбар в этой точке
    ///   видим;
    /// - перекрытым таскбар считается, только когда ВСЕ точки накрыты
    ///   чужими окнами: одиночную точку законно накрывает флайаут
    ///   громкости/календаря над часами — прятать ленту из-за него нельзя,
    ///   а настоящее полноэкранное приложение накрывает полосу целиком.
    /// Этим определением автоматически решаются все случаи, на которых
    /// ломались эвристики: maximized-окно (любого приложения) не трогает
    /// полосу → лента видна; fullscreen на ДРУГОМ мониторе не трогает НАШ
    /// таскбар → видна; «Свернуть»/Win+D → таскбар сверху → видна;
    /// настоящий fullscreen на нашем мониторе накрывает полосу → прячемся.
    /// WindowFromPoint пропускает прозрачные пиксели layered-окон насквозь,
    /// так что прозрачные области нашей же ленты зонду не мешают.
    /// </summary>
    /// Диагностический лог зонда — включается переменной окружения
    /// CLAUDE_BAND_DIAG=1, пишет в %TEMP%\claude-band-diag.log. Оставлен
    /// намеренно: видимость ленты уже несколько раз глючила только на живой
    /// машине пользователя, и каждый раз главным дефицитом были факты.
    private static readonly bool DiagEnabled =
        Environment.GetEnvironmentVariable("CLAUDE_BAND_DIAG") == "1";

    private static void Diag(string message)
    {
        if (!DiagEnabled) return;
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "claude-band-diag.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException) { /* лог не важнее работы ленты */ }
    }

    private static bool IsTaskbarObscured(nint ownHwnd, nint tray)
    {
        if (!Win32.GetWindowRect(tray, out var trayRect)) return false;

        var width = trayRect.Right - trayRect.Left;
        var midY = (trayRect.Top + trayRect.Bottom) / 2;

        foreach (var fraction in ProbeFractions)
        {
            var point = new Win32.POINT
            {
                X = trayRect.Left + (int)(width * fraction),
                Y = midY,
            };

            var hit = Win32.WindowFromPoint(point);
            if (hit == nint.Zero)
            {
                Diag($"probe f={fraction} pt=({point.X},{point.Y}) hit=0 -> visible");
                return false; // пустота — уж точно не окно поверх таскбара
            }

            var root = Win32.GetAncestor(hit, Win32.GaRoot);
            Diag($"probe f={fraction} pt=({point.X},{point.Y}) hit={hit} root={root} cls={Win32.GetClassName(root)} tray={tray} own={ownHwnd}");
            if (root == tray || root == ownHwnd || root == nint.Zero) return false;

            // Закловленное окно — призрак: DWM его не рисует, пользователь
            // видит таскбар, но WindowFromPoint всё равно возвращает его.
            // Живой пример: окно Deadlock (SDL_app) после выхода из
            // exclusive-fullscreen часами висит закловленным ПОВЕРХ таскбара
            // в z-порядке, и любая перетасовка фокуса (открыть Telegram)
            // снова поднимает его над Shell_TrayWnd — без этой проверки
            // лента пряталась бы при видимом глазу таскбаре. Призрак ничего
            // не заслоняет — точка читается как «таскбар видим».
            if (Win32.IsCloaked(root))
            {
                Diag($"probe f={fraction}: root {root} is DWM-cloaked ghost -> visible");
                return false;
            }
        }

        Diag("probe verdict: OBSCURED (all points foreign)");
        return true;
    }

    private static int ToPhysical(double dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);
}

/// <summary>
/// Содержимое ленты: горизонтальный ряд колонок «метка над значением»,
/// нарисованный вручную через <see cref="OnRender"/> — тот же подход, что и
/// у циферблатов виджета (DialControl/StatusDialControl), переиспользующий
/// DialText.Format/DrawStackCentered, а не StackPanel из TextBlock (проще
/// точно посчитать ширину каждой колонки для Reposition, чем заводить лишние
/// алиасы под StackPanel/TextBlock, у которых есть тёзки в System.Windows.Forms).
/// Белый текст с тёмной тенью-обводкой в 1 px — на прозрачном фоне поверх
/// произвольного (светлого или тёмного) цвета таскбара один только белый
/// местами сливался бы с фоном; тень читается на любом фоне.
/// </summary>
internal sealed class TaskbarBandContent : FrameworkElement
{
    private const double LabelFontSize = 10;
    private const double ValueFontSize = 14;
    private const double LineSpacingDip = 1;
    private const double ColumnSpacingDip = 14;
    private const double ShadowOffsetDip = 1;

    private static readonly SolidColorBrush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)));

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
            var center = new Point(x + width / 2, y);

            // Тень — та же стопка строк, тем же кеглем, сдвинутая на 1 px
            // по диагонали, рисуется ПЕРВОЙ (белый текст ложится поверх).
            var labelShadow = DialText.Format(metric.Label, LabelFontSize, Theme.LabelWeight, ShadowBrush, pixelsPerDip);
            var valueShadow = DialText.Format(metric.Value, ValueFontSize, Theme.ValueWeight, ShadowBrush, pixelsPerDip);
            DialText.DrawStackCentered(dc, new Point(center.X + ShadowOffsetDip, center.Y + ShadowOffsetDip), LineSpacingDip, labelShadow, valueShadow);

            var labelText = DialText.Format(metric.Label, LabelFontSize, Theme.LabelWeight, Brushes.White, pixelsPerDip);
            var valueText = DialText.Format(metric.Value, ValueFontSize, Theme.ValueWeight, Brushes.White, pixelsPerDip);
            DialText.DrawStackCentered(dc, center, LineSpacingDip, labelText, valueText);

            x += width + ColumnSpacingDip;
        }
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
