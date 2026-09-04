using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsageWidget.Core;
// UseWindowsForms exposes System.Windows.Forms/System.Drawing globally;
// UserControl, Border, Brushes, Point, Rectangle and MouseEventArgs collide
// with the WPF types of the same name.
using UserControl = System.Windows.Controls.UserControl;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Button = System.Windows.Controls.Button;
using Style = System.Windows.Style;
using FontFamily = System.Windows.Media.FontFamily;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// The "the numbers can't be trusted" notice — an App-layer analog of BlockingNotice.swift.
/// Core doesn't port it (see task-14-brief.md), so the text rules
/// live right here, next to whatever displays them.
/// </summary>
public sealed record WidgetNotice(string Title, string Detail, bool ShowSignIn);

/// <summary>
/// The widget panel: one dial row per account + the status line + the
/// BlockingNotice card + the edit toolbar. Port of
/// <c>Sources/ClaudeUsageWidgetCore/Views/WidgetRootView.swift</c>.
/// </summary>
public partial class WidgetRootView : UserControl
{
    /// The toolbar's Hide button.
    public event Action? HideRequested;

    /// The toolbar's Done button. The mode had no way out from the panel — you
    /// had to go back to the tray for the item that turned it on (the user,
    /// 2026-09-04) — and the tray item is still the other half of the toggle,
    /// so this asks for the same single `App.SetEditingLayout(false)`.
    public event Action? EditDoneRequested;

    /// The Sign in button in the NoCredentials notice — the original
    /// BlockingNotice.swift has no such button at all (there it's just text
    /// with a reference to the menu), but the Task 14 brief explicitly asks
    /// for a button.
    public event Action? SignInRequested;

    /// The toolbar's Lock button. A notification of the click only — the source
    /// of truth for PositionLocked lives in DesktopWidgetWindow, and this view
    /// draws what it is told through <see cref="PositionLocked"/>.
    public event Action? LockToggleRequested;

    /// Raised when a drop in edit mode actually changes the layout. The App
    /// saves it and re-renders — the same path the tray's LayoutSelected
    /// already takes.
    public event Action<WidgetLayout>? LayoutEdited;

    /// The toolbar's Status button. Not part of WidgetLayout: the mode is the
    /// master and the layout's status cell follows it through Sanitize, so the
    /// App saves the mode and re-sanitizes rather than being handed a layout.
    public event Action<StatusMode>? StatusModeSelected;

    /// The toolbar's Model button, on the same path as Status: the setting is
    /// the master and the layout's model cell follows it through Sanitize.
    public event Action<ModelDial>? ModelDialSelected;

    /// The toolbar's Plan button. Simpler than the two above: the plan is a line
    /// under the name, never a cell, so nothing in `Order` follows it and there
    /// is no Sanitize step — the App saves the setting and re-renders.
    public event Action<PlanLine>? PlanLineSelected;

    private bool _positionLocked;

    public bool PositionLocked
    {
        get => _positionLocked;
        set
        {
            _positionLocked = value;
            RefreshToolbar();
        }
    }

    private bool _stripAbove;

    /// Which side of the panel the edit strip is drawn on. The window decides
    /// it — only the window knows where the panel sits on which screen — and
    /// sets it before switching the mode on.
    public bool StripAbove
    {
        get => _stripAbove;
        set
        {
            if (_stripAbove == value) return;
            _stripAbove = value;
            if (_editMode) ApplySize(Metrics(editMode: true));
        }
    }

    /// The band edit mode adds to the WINDOW — never to the panel — whatever
    /// the mode is right now. The window reads it before the switch, to pick
    /// the side and to move its own top by the same number.
    public double ToolbarReserve => Metrics(editMode: true).ToolbarReserve;

    /// One reader for the panel's geometry, so no caller can forget a setting.
    /// It already happened once: the plan line's reserve reaches the panel
    /// through PanelMetrics, and a `PanelMetrics.For` written out by hand in the
    /// edit-mode path would have dropped it — the dials would climb into the
    /// name only while the strip was up, which no PNG case renders.
    private PanelMetrics Metrics(bool editMode) =>
        PanelMetrics.For(_layout, _accountCount, _side, editMode, _planLine);

    private bool _editMode;

    /// While on: every cell is outlined, the status line explains the mode, and
    /// the panel's own drag, resize and dial clicks are suppressed.
    public bool EditMode
    {
        get => _editMode;
        set
        {
            if (_editMode == value) return;
            _editMode = value;
            // Leaving the mode mid-drag would strand the capture: OnEditMouseUp
            // returns before releasing it once the mode is off, and the mode is
            // switched from outside the mouse — the tray item and the toolbar's
            // own Hide button, where nothing drops the capture. (Esc was the
            // planned second route; measured 2026-08-26 as unreachable on this
            // WS_EX_NOACTIVATE window — see the layout-editor-design spec,
            // Leaving.)
            if (!_editMode) CancelDrag();

            // Geometry, not just chrome: the strip is a band the WINDOW grows
            // by, drawn beside the panel rather than inside it, so the rounded
            // border keeps its size and the dials do not move. No BuildGrid —
            // the cells and their hosts are untouched.
            ApplySize(Metrics(_editMode));
            RefreshToolbar();
            RefreshEditChrome();
        }
    }

    /// The bounds the outlines currently drawn were built from. Adding a child
    /// to a panel starts a layout pass, and every pass raises LayoutUpdated
    /// again — an unconditional rebuild from that handler would keep the layout
    /// system running for as long as edit mode is on. Redrawing only when a
    /// cell has actually moved lets it settle after one pass.
    private readonly Dictionary<UIElement, Rect> _chromeBounds = [];

    /// One frozen dash pattern for every outline: the collection is identical
    /// per cell and per rebuild, so allocating it inside the loop only made
    /// garbage.
    private static readonly DoubleCollection OutlineDashes =
        (DoubleCollection)new DoubleCollection { 3, 3 }.GetAsFrozen();

    /// Redraws the overlay from the CURRENT arranged positions of the hosts.
    ///
    /// Called from LayoutUpdated as well as from the property, and that is not
    /// belt and braces: a host has no RenderSize until the panel has been
    /// arranged, so chrome built at the moment the mode is switched on would be
    /// a stack of zero-sized rectangles.
    private void RefreshEditChrome()
    {
        if (!_editMode)
        {
            EditLayer.Children.Clear();
            _chromeBounds.Clear();
            return;
        }

        var wanted = _cells.Keys
            .Select(host => (Host: host, Bounds: BoundsOf(host)))
            .Where(cell => cell.Bounds.Width > 0)
            .ToList();

        if (wanted.Count == _chromeBounds.Count
            && wanted.All(cell => _chromeBounds.TryGetValue(cell.Host, out var drawn) && drawn == cell.Bounds))
            return;

        EditLayer.Children.Clear();
        _chromeBounds.Clear();
        // The ghost and the highlight are children of this layer too, so the
        // clear above took them with it. Forgetting them keeps the drag state
        // honest: a rebuild mid-drag loses the chrome, not the drop, and
        // CancelDrag is never left holding a detached element.
        _ghost = null;
        _target = null;

        foreach (var (host, bounds) in wanted)
        {
            var outline = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stroke = Theme.DimBrush,
                StrokeThickness = 1,
                StrokeDashArray = OutlineDashes,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(outline, bounds.X);
            Canvas.SetTop(outline, bounds.Y);
            EditLayer.Children.Add(outline);
            _chromeBounds[host] = bounds;
        }
    }

    private Rect BoundsOf(UIElement host) =>
        host.RenderSize.Width <= 0
            ? default
            : host.TransformToAncestor(RootGrid).TransformBounds(new Rect(host.RenderSize));

    public WidgetRootView()
    {
        InitializeComponent();

        // The grid is rebuilt on every account change and every resize, and the
        // outlines are positions, not children of what they outline.
        RootGrid.LayoutUpdated += (_, _) => { if (_editMode) RefreshEditChrome(); };

        // One rule in one place. OnEditMouseDown marks the TUNNELLING event
        // handled for a press ON A CELL, and nothing subscribes with
        // handledEventsToo — neither the window's own MouseLeftButtonDown/
        // MouseMove/MouseLeftButtonUp/LostMouseCapture (DesktopWidgetWindow's
        // constructor) nor any handler inside the panel. So while editing, a
        // press on a cell reaches NOTHING but the cell drag: not the panel drag,
        // not StatusDialControl's link to status.claude.com, not the notice's
        // Sign in. A press on the padding, a gap or the toolbar strip's
        // background is NOT handled and moves the panel as it does outside the
        // mode (the user, 2026-09-04); a toolbar button takes its own click.
        RootGrid.PreviewMouseLeftButtonDown += OnEditMouseDown;
        RootGrid.PreviewMouseMove += OnEditMouseMove;
        RootGrid.PreviewMouseLeftButtonUp += OnEditMouseUp;
        RootGrid.LostMouseCapture += (_, _) => CancelDrag();

        BuildToolbar();
        RefreshToolbar();
    }

    /// Glyphs are Segoe MDL2 Assets, the font the deleted eye and lock already
    /// used. A glyph the font does not carry renders as a hollow box — which is
    /// why the toolbar PNGs are LOOKED AT rather than trusted.
    private void BuildToolbar()
    {
        // U+E73E "CheckMark", a literal like the other six. Every glyph here is
        // a private-use code point an editor draws as nothing, so which one a
        // line carries is checked with the PNGs, not read off the source.
        _toolbarButtons[0] = ToolbarButton("", () => EditDoneRequested?.Invoke());

        _toolbarButtons[1] = ToolbarButton("", () =>
            LayoutEdited?.Invoke(_layout with { PanelFlow = LayoutCycle.Next(_layout.PanelFlow) }));

        _toolbarButtons[2] = ToolbarButton("", () =>
            LayoutEdited?.Invoke(_layout with
            {
                Block = _layout.Block with { Flow = LayoutCycle.Next(_layout.Block.Flow) },
            }));

        _toolbarButtons[3] = ToolbarButton("", () =>
            LayoutEdited?.Invoke(_layout with
            {
                Block = _layout.Block with { Name = LayoutCycle.Next(_layout.Block.Name) },
            }));

        // U+E8EC "Ticket" — the subscription plan, beside the Name
        // button because the line it shows or hides is drawn under the name.
        // Confirmed in the toolbar PNGs, never read off a name list: a code
        // point the font does not carry renders as a hollow box and says nothing.
        _toolbarButtons[4] = ToolbarButton("", () =>
            PlanLineSelected?.Invoke(LayoutCycle.Next(_planLine)));

        _toolbarButtons[5] = ToolbarButton("", () =>
            StatusModeSelected?.Invoke(LayoutCycle.Next(_statusMode)));

        // U+EC4A "SpeedHigh" — the gauge of the EC48/EC49/EC4A trio, its
        // needle at the top right. Picked off a rendered sheet of the font
        // rather than a name list, and confirmed in the toolbar PNGs at
        // MinSide, where it is drawn smallest.
        _toolbarButtons[6] = ToolbarButton("", () =>
            ModelDialSelected?.Invoke(LayoutCycle.Next(_modelDial)));

        _toolbarButtons[7] = ToolbarButton("", () => LockToggleRequested?.Invoke());

        _toolbarButtons[8] = ToolbarButton("", () => HideRequested?.Invoke());

        foreach (var button in _toolbarButtons) EditToolbar.Children.Add(button);
    }

    private Button ToolbarButton(string glyph, Action click)
    {
        var button = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = Theme.DimBrush,
            Content = glyph,
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// Tooltips and the lock glyph. A glyph alone cannot say which of three
    /// flows is selected, so every cycling button names its setting AND its
    /// current value.
    private void RefreshToolbar()
    {
        _toolbarButtons[0].ToolTip = "Done — leave edit mode";
        _toolbarButtons[1].ToolTip = $"Accounts on the panel: {_layout.PanelFlow} — click to cycle";
        _toolbarButtons[2].ToolTip = $"Dials in a block: {_layout.Block.Flow} — click to cycle";
        _toolbarButtons[3].ToolTip = $"Account name: {_layout.Block.Name} — click to cycle";
        _toolbarButtons[4].ToolTip = $"Subscription plan: {_planLine} — click to cycle";
        _toolbarButtons[5].ToolTip = $"Service status: {_statusMode} — click to cycle";

        _toolbarButtons[6].ToolTip = $"Model dial: {_modelDial} — click to cycle";

        // Segoe MDL2 Assets: E72E "Lock", E785 "Unlock" — the pair the deleted
        // header used, with the same warning colour when locked.
        _toolbarButtons[7].Content = _positionLocked ? "" : "";
        _toolbarButtons[7].Foreground = _positionLocked ? Theme.WarningBrush : Theme.DimBrush;
        _toolbarButtons[7].ToolTip = _positionLocked
            ? "Position and size are locked — click to unlock"
            : "Click to lock the widget position and size";

        _toolbarButtons[8].ToolTip = "Hide the widget — bring it back from the tray icon";
    }

    /// <summary>
    /// Recomputes the whole geometry for a new panel side length. In Swift this
    /// happens reactively on every render via computed properties
    /// scale/pad/gap/dialSize; here it's called explicitly from
    /// DesktopWidgetWindow — when the window is created and on every size
    /// change during a resize.
    /// </summary>
    /// <param name="accountCount">The number of blocks. Changes at runtime, so
    /// the grid is rebuilt here from scratch.</param>
    public void ApplyLayout(
        WidgetLayout layout, StatusMode statusMode, ModelDial modelDial, PlanLine planLine,
        int accountCount, double side)
    {
        _layout = layout;
        _statusMode = statusMode;
        _modelDial = modelDial;
        _planLine = planLine;
        _accountCount = accountCount;
        _side = side;

        var metrics = Metrics(_editMode);
        var scale = metrics.Scale;
        var corner = Theme.CornerRadius(scale);

        ApplySize(metrics);

        PanelBorder.CornerRadius = new CornerRadius(corner);
        NoticeBorder.CornerRadius = new CornerRadius(corner);

        BuildGrid(layout, accountCount, metrics);
        RefreshToolbar();

        // The 31*scale top padding is gone with the hover header it cleared.
        NoticeStack.Margin = new Thickness(metrics.Padding);
        NoticeTitleText.FontSize = Theme.ValueFontSize(scale);
        NoticeDetailText.FontSize = Theme.CaptionFontSize(scale);
        NoticeDetailText.Margin = new Thickness(0, 5 * scale, 0, 0);
        SignInButton.Margin = new Thickness(0, 8 * scale, 0, 0);
        SignInButton.FontSize = Theme.CaptionFontSize(scale);

        // Margin is not set here: it depends on which side the strip is on, so
        // ApplySize owns it and this would only overwrite it.
        StatusLineText.FontSize = Theme.CaptionFontSize(scale);
    }

    /// Panel size, the toolbar strip, and the band it occupies beside the
    /// panel. Separate from ApplyLayout because entering edit mode changes the
    /// geometry and nothing else: rebuilding the cells there would throw away
    /// the drag hosts and the chrome cache for a strip of buttons.
    private void ApplySize(PanelMetrics metrics)
    {
        // The whole control is the WINDOW, which is the panel plus the strip's
        // band while the mode is on.
        Width = RootGrid.Width = metrics.Width;
        Height = RootGrid.Height = metrics.Height;

        // The band is outside the rounded border, on the side the window chose.
        // Pushing the border away from it by exactly the reserve is what keeps
        // the panel the size it has with the mode off; the window moves its own
        // top by the same number, so the panel does not move on screen either.
        // The user's report (2026-09-04): a panel placed in the corner of the
        // screen looked right in the mode and had drifted at top and bottom by
        // the time the mode was left.
        var above = _stripAbove ? metrics.ToolbarReserve : 0;
        var below = _stripAbove ? 0 : metrics.ToolbarReserve;
        var band = new Thickness(0, above, 0, below);
        PanelBorder.Margin = band;
        NoticeBorder.Margin = band;

        // The status band is the grid's bottom margin, so the grid is flush
        // under the top padding rather than centred in the leftovers — which is
        // what put 13 pt of slack over the names and none under the text.
        // Nothing is left over to centre in: the panel's height is exactly this
        // margin plus the content.
        DialGrid.Margin = new Thickness(
            metrics.Padding, metrics.Padding, metrics.Padding, metrics.StatusBand);

        // Exactly what Core reserved, so the caption cannot be taller or
        // shorter than the band was measured for.
        StatusLineText.Height = metrics.CaptionLine;

        // The status line is a child of RootGrid, not of PanelBorder, so it
        // does not inherit the band and would sit in the strip's space without
        // `below`. BottomGap is the panel's padding: the same clearance the
        // text has above it inside the band, and the same the names have off
        // the top edge.
        StatusLineText.Margin = new Thickness(0, 0, 0, below + metrics.BottomGap);

        LayoutToolbar(metrics);
    }

    /// Places the buttons on Core's row/column counts. A WrapPanel would decide
    /// the wrap itself, and its decision could disagree with the height that was
    /// reserved — the same tracks-from-Core rule the dial grid follows.
    private void LayoutToolbar(PanelMetrics metrics)
    {
        var toolbar = metrics.Toolbar;

        ToolbarPill.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;

        // Flush against the OUTER edge of the band, so the one dial gap the
        // reserve carries falls between the buttons and the panel — on either
        // side. No margin of its own: the band is the whole spacing budget, and
        // a second number here is how the strip and the reserve drift apart.
        ToolbarPill.VerticalAlignment = _stripAbove ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        ToolbarPill.Margin = new Thickness(0);

        // That gap, split three ways: pill padding on the button row's two
        // sides, and the clear space left between the pill and the panel. The
        // pill is paid for out of the band, never by growing it — so the window
        // and the panel are the size they were without it.
        var inset = metrics.Gap / 3;
        ToolbarPill.Padding = new Thickness(inset);

        // The panel's own radius, capped at half the pill's height: past that a
        // Border stops reading as a rounded strip. At every side that ships the
        // cap is what applies, which is what makes it a pill and not a box.
        var pillHeight = toolbar.Rows * toolbar.ButtonSize
                         + (toolbar.Rows - 1) * toolbar.Gap + inset * 2;
        ToolbarPill.CornerRadius = new CornerRadius(
            Math.Min(Theme.CornerRadius(metrics.Scale), pillHeight / 2));

        EditToolbar.RowDefinitions.Clear();
        EditToolbar.ColumnDefinitions.Clear();
        foreach (var track in GridTracks.Build(toolbar.Rows, toolbar.ButtonSize, toolbar.Gap))
            EditToolbar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(track) });
        foreach (var track in GridTracks.Build(toolbar.Columns, toolbar.ButtonSize, toolbar.Gap))
            EditToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(track) });

        for (var i = 0; i < _toolbarButtons.Length; i++)
        {
            var button = _toolbarButtons[i];
            button.Width = button.Height = toolbar.ButtonSize;
            // 9*scale — the font size the header's eye and lock were given.
            button.FontSize = 9 * metrics.Scale;
            Grid.SetRow(button, GridTracks.TrackOf(i / toolbar.Columns));
            Grid.SetColumn(button, GridTracks.TrackOf(i % toolbar.Columns));
        }
    }

    /// What's drawn for a single account. Dials are addressed by an index into
    /// DialModel.All (0 = 5H, 1 = 7D, 2 = per-model), not by their position in
    /// the grid: the position is set by a setting and changes, the meaning
    /// doesn't.
    private sealed class BlockVisual
    {
        public TextBlock? Name;

        /// The plan line under the name. Null whenever the setting is off — the
        /// TextBlock is not built at all, rather than built and left empty, so
        /// nothing invisible can take part in a layout pass.
        public TextBlock? Plan;

        public StatusDialControl? Status;
        public readonly DialControl?[] Dials = new DialControl?[3];
    }

    private readonly List<BlockVisual> _blocks = [];

    /// The layout this grid was built from — what a swap is applied to.
    private WidgetLayout _layout = WidgetLayout.Default;

    /// What ApplyLayout was last called with. EditMode re-measures the panel
    /// from these without rebuilding the grid.
    private StatusMode _statusMode = StatusMode.Line;
    private ModelDial _modelDial = ModelDial.Shown;
    private PlanLine _planLine = PlanLine.Hidden;
    private int _accountCount;
    private double _side = WidgetSettings.DefaultSide;

    /// Done, accounts, dials, name, plan, status, model, lock, hide — left to
    /// right, Done first because leaving the mode is the one thing every visit
    /// ends with, and plan beside name because it draws under it.
    /// Built once; only geometry and tooltips change afterwards.
    private readonly Button[] _toolbarButtons = new Button[ToolbarMetrics.ButtonCount];

    /// Which block, and which position in `layout.Block.LaidOut`.
    private readonly record struct CellHit(int Block, int Cell);

    /// Host border -> the cell it carries. Rebuilt whenever the grid is.
    ///
    /// Hit testing goes through WPF rather than through arithmetic in Core on
    /// purpose: where a cell sits depends on the status band under the grid,
    /// the strip's band outside the panel, and whether the name is a row of its
    /// own — three facts that live in the markup and in ApplySize. A pure
    /// function would have to duplicate them and would drift the moment either
    /// changes; it already would have, twice.
    private readonly Dictionary<UIElement, CellHit> _cells = [];

    /// The cell under a point in RootGrid coordinates, or null for the padding,
    /// the gaps, the holes and everything outside the grid.
    private CellHit? HitCell(Point point)
    {
        var hit = RootGrid.InputHitTest(point) as DependencyObject;
        while (hit is not null)
        {
            if (hit is UIElement element && _cells.TryGetValue(element, out var cell)) return cell;
            // Both trees, like IsInToolbar: a point over an account name hits the
            // implicit Run inside its TextBlock, a ContentElement the visual
            // walk cannot take (2026-09-04).
            hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
        }

        return null;
    }

    /// Walks up from the hit element to the toolbar.
    ///
    /// BOTH trees, and that is the whole point: a press over the glyph itself
    /// hit-tests to the implicit `Run` inside the button's TextBlock, which is
    /// a ContentElement — `VisualTreeHelper.GetParent` cannot take it, and a
    /// visual-only walk would exit at once and swallow the click that matters
    /// most. The logical parent gets from the Run to the TextBlock, and the
    /// visual walk carries on from there.
    private bool IsInToolbar(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, EditToolbar)) return true;
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    /// The cell the button went down on — set on every press in edit mode, and
    /// null once the drop or a cancel has consumed it.
    private CellHit? _dragSource;

    private Point _dragStart;
    private bool _dragging;

    /// The translucent copy under the cursor, and the outline around the cell
    /// it would land on. Both live in EditLayer beside the dashed outlines.
    private Rectangle? _ghost;
    private Rectangle? _target;

    private void OnEditMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;

        // The toolbar is the one thing in edit mode that is NOT a cell. This
        // handler marks the TUNNELLING event handled for everything else, and a
        // Button never sees a click whose preview was handled above it. Only
        // Down needs the exemption: Move and Up bail on a null _dragSource.
        if (IsInToolbar(e.OriginalSource as DependencyObject)) return;

        // A press starts from nothing: whatever the previous gesture left
        // behind — a _dragging that never got its release, a ghost still in the
        // layer — is dropped here rather than inherited into this drag.
        CancelDrag();

        _dragStart = e.GetPosition(RootGrid);
        _dragSource = HitCell(_dragStart);

        // Only a press ON A CELL is taken. The padding, the gaps and the
        // toolbar strip's own background are left to the window, so the panel
        // can still be moved (and resized at its edge) while the layout is
        // being edited — the user's ask of 2026-09-04, reversing the 08-26
        // ruling that a press anywhere in the mode reached nothing but the
        // cell drag. A cell press is still handled: its release must not fall
        // through to the window's drag, and the status dial's link must not
        // open on a swap.
        if (_dragSource is null) return;
        e.Handled = true;
    }

    private void OnEditMouseMove(object sender, MouseEventArgs e)
    {
        if (!_editMode || _dragSource is not { } source) return;

        // The same belt DesktopWidgetWindow.OnWindowMouseMove wears, and for
        // the reason its comment gives: a move arrives whether or not the mouse
        // is captured, so a _dragging left set by a release we never saw would
        // make the next bare hover drag a cell. LostMouseCapture covers capture
        // LOST; it cannot fire for capture never taken, which is exactly what
        // an ignored CaptureMouse() failure would leave behind. This guard is
        // what makes that return value not worth asserting on.
        if (_dragging && e.LeftButton != MouseButtonState.Pressed)
        {
            CancelDrag();
            return;
        }

        var point = e.GetPosition(RootGrid);

        if (!_dragging)
        {
            // A click is not a half-swap: below the system's own threshold this
            // is somebody pressing a dial, not moving it.
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _dragging = true;
            RootGrid.CaptureMouse();
            StartGhost(source);
        }

        MoveGhost(point);
        HighlightTarget(HitCell(point), source);
    }

    private void OnEditMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;

        var source = _dragSource;
        var dropped = _dragging ? HitCell(e.GetPosition(RootGrid)) : null;
        CancelDrag();

        if (source is not { } from || dropped is not { } to) return;
        // A drag inside one cell, or across blocks: both are the same swap,
        // because every block shares one BlockLayout. Landing back where it
        // started is not an edit.
        if (from.Cell == to.Cell) return;

        LayoutEdited?.Invoke(_layout with { Block = _layout.Block.SwapCells(from.Cell, to.Cell) });
    }

    private void CancelDrag()
    {
        if (_dragging) RootGrid.ReleaseMouseCapture();
        _dragging = false;
        _dragSource = null;

        if (_ghost is not null) EditLayer.Children.Remove(_ghost);
        if (_target is not null) EditLayer.Children.Remove(_target);
        _ghost = null;
        _target = null;
    }

    private void StartGhost(CellHit source)
    {
        var host = HostOf(source);
        if (host is null) return;

        var bounds = BoundsOf(host);
        _ghost = new Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            // Stretch.None keeps the copy at its own size: the brush paints the
            // live visual, so the ghost updates with the dial behind it.
            Fill = new VisualBrush(host) { Stretch = Stretch.None },
            Opacity = 0.6,
            IsHitTestVisible = false,
        };
        EditLayer.Children.Add(_ghost);
    }

    private void MoveGhost(Point point)
    {
        if (_ghost is null) return;
        Canvas.SetLeft(_ghost, point.X - _ghost.Width / 2);
        Canvas.SetTop(_ghost, point.Y - _ghost.Height / 2);
    }

    private void HighlightTarget(CellHit? hit, CellHit source)
    {
        if (_target is not null) EditLayer.Children.Remove(_target);
        _target = null;

        if (hit is not { } cell || cell.Cell == source.Cell) return;

        var host = HostOf(cell);
        if (host is null) return;

        var bounds = BoundsOf(host);
        _target = new Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Stroke = Theme.TextBrush,
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_target, bounds.X);
        Canvas.SetTop(_target, bounds.Y);
        EditLayer.Children.Add(_target);
    }

    private UIElement? HostOf(CellHit cell)
    {
        foreach (var pair in _cells)
            if (pair.Value.Equals(cell)) return pair.Key;

        return null;
    }

    /// Parks the drag chrome as if a cell were halfway to another, so the
    /// offscreen preview can show it. The mouse is what is missing here, not
    /// the drag: the ghost, the highlight and the geometry are the same calls
    /// OnEditMouseMove makes.
    internal void PreviewDrag(int block, int from, int to)
    {
        var source = new CellHit(block, from);
        var target = new CellHit(block, to);
        if (HostOf(source) is not { } sourceHost || HostOf(target) is not { } targetHost) return;

        var a = BoundsOf(sourceHost);
        var b = BoundsOf(targetHost);

        StartGhost(source);
        MoveGhost(new Point(
            (a.X + a.Width / 2 + b.X + b.Width / 2) / 2,
            (a.Y + a.Height / 2 + b.Y + b.Height / 2) / 2));
        HighlightTarget(target, source);
    }

    private void BuildGrid(WidgetLayout layout, int accountCount, PanelMetrics metrics)
    {
        DialGrid.Children.Clear();
        DialGrid.RowDefinitions.Clear();
        DialGrid.ColumnDefinitions.Clear();
        _blocks.Clear();
        _cells.Clear();

        foreach (var track in GridTracks.Build(metrics.Rows, metrics.Block.Height, metrics.BlockGap))
            DialGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(track) });
        foreach (var track in GridTracks.Build(metrics.Columns, metrics.Block.Width, metrics.BlockGap))
            DialGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(track) });

        for (var i = 0; i < accountCount; i++)
        {
            var visual = new BlockVisual();
            var block = BuildBlock(layout, metrics, visual, i);
            Grid.SetRow(block, GridTracks.TrackOf(i / Math.Max(metrics.Columns, 1)));
            Grid.SetColumn(block, GridTracks.TrackOf(i % Math.Max(metrics.Columns, 1)));
            DialGrid.Children.Add(block);
            _blocks.Add(visual);
        }
    }

    /// The name above/below — a separate line centered in the block; the name
    /// in a cell — one of the flow's items, in the place of the former STATUS.
    private Grid BuildBlock(WidgetLayout layout, PanelMetrics metrics, BlockVisual visual, int blockIndex)
    {
        var cells = BuildCells(layout, metrics, visual, blockIndex);

        if (layout.Block.Name is not (NamePlacement.Above or NamePlacement.Below))
            return cells;

        // The name and the plan are one header, and its total height is what
        // Core reserved: NameHeight already counts the plan's caption line when
        // the setting is on, so what is drawn and what the panel was sized for
        // are the same number.
        var header = NameStack(metrics, visual, width: null);
        header.Height = metrics.Block.NameHeight;

        var above = layout.Block.Name == NamePlacement.Above;
        header.Margin = above
            ? new Thickness(0, 0, 0, metrics.Gap)
            : new Thickness(0, metrics.Gap, 0, 0);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(header, above ? 0 : 1);
        Grid.SetRow(cells, above ? 1 : 0);
        root.Children.Add(header);
        root.Children.Add(cells);
        return root;
    }

    private Grid BuildCells(WidgetLayout layout, PanelMetrics metrics, BlockVisual visual, int blockIndex)
    {
        var grid = new Grid();
        foreach (var track in GridTracks.Build(metrics.Block.Rows, metrics.DialSize, metrics.Gap))
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(track) });
        foreach (var track in GridTracks.Build(metrics.Block.Columns, metrics.DialSize, metrics.Gap))
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(track) });

        var items = layout.Block.LaidOut;
        for (var i = 0; i < items.Count; i++)
        {
            UIElement cell;
            if (items[i] == BlockItem.Name)
            {
                // No reserve to honour here: the name cell is a whole dial
                // square, and two short lines centred in it fit with room to
                // spare. That is why BlockMetrics adds nothing for Cell.
                cell = NameStack(metrics, visual, metrics.DialSize);
            }
            else if (items[i] == BlockItem.Status)
            {
                var status = new StatusDialControl { Width = metrics.DialSize, Height = metrics.DialSize };
                visual.Status = status;
                cell = status;
            }
            else
            {
                var dial = new DialControl { Width = metrics.DialSize, Height = metrics.DialSize };
                visual.Dials[DialIndex(items[i])] = dial;
                cell = dial;
            }

            // A transparent background, not an absent one: a Border with no
            // Background does not hit-test on its own empty pixels, and the gap
            // between a dial's ring and the corner of its square is exactly
            // where a drag will be aimed. The host adds no thickness and no
            // padding, so it cannot move what it wraps by a pixel.
            var host = new Border { Background = Brushes.Transparent, Child = cell };
            _cells[host] = new CellHit(blockIndex, i);

            Grid.SetRow(host, GridTracks.TrackOf(i / Math.Max(metrics.Block.Columns, 1)));
            Grid.SetColumn(host, GridTracks.TrackOf(i % Math.Max(metrics.Block.Columns, 1)));
            grid.Children.Add(host);
        }

        return grid;
    }

    /// The order in which DialModel.All always returns its three models.
    private static int DialIndex(BlockItem item) => item switch
    {
        BlockItem.FiveHour => 0,
        BlockItem.SevenDay => 1,
        BlockItem.Model => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    /// The account name, with the subscription plan under it when the setting
    /// is on: "the account text on top, and the subscription tier below it" (the user,
    /// 2026-09-04).
    ///
    /// One builder for both placements, because the pair must read the same in
    /// a cell as it does in a row — only the width differs. The plan takes the
    /// caption size and the dim brush, the same pair the status line uses, so it
    /// reads as a subtitle and never competes with the name.
    ///
    /// The TextBlock is given exactly the line height Core reserved for it: what
    /// the panel was sized for and what WPF draws cannot then drift, which is
    /// the same rule the status caption follows.
    private StackPanel NameStack(PanelMetrics metrics, BlockVisual visual, double? width)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (width is { } w) stack.Width = w;

        var name = NewLabel(metrics.Scale, 11 * metrics.Scale, Theme.TextBrush);
        name.TextAlignment = TextAlignment.Center;
        visual.Name = name;
        stack.Children.Add(name);

        if (metrics.PlanLine == PlanLine.Shown)
        {
            var plan = NewLabel(metrics.Scale, Theme.CaptionFontSize(metrics.Scale), Theme.DimBrush);
            plan.TextAlignment = TextAlignment.Center;
            plan.Height = metrics.CaptionLine;
            visual.Plan = plan;
            stack.Children.Add(plan);
        }

        return stack;
    }

    private static TextBlock NewLabel(double scale, double fontSize, System.Windows.Media.Brush brush) => new()
    {
        FontFamily = Theme.FontFamily,
        FontWeight = Theme.ValueWeight,
        FontSize = fontSize,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// Populates the rows, the status line and the notice — port of the body of
    /// WidgetRootView.swift:46-96, one row per account.
    public void SetContent(IReadOnlyList<AccountRow> rows, ServiceStatus status, bool dimmed, string? statusLine, WidgetNotice? notice)
    {
        // A mismatch means ApplyLayout wasn't called for this list —
        // drawing further would silently lose or duplicate an account.
        if (rows.Count != _blocks.Count)
            throw new InvalidOperationException(
                $"SetContent got {rows.Count} rows for a panel built for {_blocks.Count}.");

        for (var i = 0; i < rows.Count; i++)
        {
            var visual = _blocks[i];
            if (visual.Name is { } name)
            {
                name.Text = rows[i].DisplayName;
                // The full name in the tooltip: in a cell as wide as a dial a
                // long name gets truncated with an ellipsis.
                name.ToolTip = rows[i].DisplayName;
            }

            // Empty, not collapsed, when the plan is not known yet: the line's
            // height is reserved either way, and collapsing it would move the
            // dials the moment the first poll answered. Null is "not fetched" —
            // Core keeps that apart from a free plan, and neither is invented
            // here.
            if (visual.Plan is { } plan) plan.Text = rows[i].PlanLabel ?? "";

            for (var d = 0; d < rows[i].Dials.Count; d++)
                if (visual.Dials[d] is { } dial)
                    ApplyDial(dial, rows[i].Dials[d], dimmed);

            // The service is one service, so every block's dial shows the same
            // thing — the cell exists per block because the grid does.
            if (visual.Status is { } serviceDial)
            {
                serviceDial.Status = status;
                serviceDial.Dimmed = dimmed;
            }
        }

        // The service state is one fact about one service. In Cell mode a dial
        // in every block says it and the line only reports trouble; in Line
        // mode this is the only place it appears, so it speaks even when the
        // service is fine — a silent line reads as a lost dial.
        //
        // The hint is one more part of the line, not a replacement for it:
        // substituting it would hide an outage or a rate-limit notice for as
        // long as the mode is on, and last in the list means the thing the user
        // did not already know leads. Written here rather than when the mode is
        // entered because SetContent runs on every poll, so a hint assigned
        // once would be erased a minute later; the window redraws the cached
        // frame on the toggle so it does not have to wait for that poll.
        var editHint = _editMode ? "drag a cell to swap · Done finishes" : null;
        var text = string.Join(" · ",
            new[] { ServiceStatusText.Line(status, _statusMode), statusLine, editHint }
                .Where(part => !string.IsNullOrEmpty(part)));

        StatusLineText.Text = text;
        StatusLineText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (notice is null)
        {
            NoticeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        NoticeTitleText.Text = notice.Title;
        NoticeDetailText.Text = notice.Detail;
        SignInButton.Visibility = notice.ShowSignIn ? Visibility.Visible : Visibility.Collapsed;
        NoticeBorder.Visibility = Visibility.Visible;
    }

    private static void ApplyDial(DialControl dial, DialModel model, bool dimmed)
    {
        dial.Title = model.Title;
        dial.Fraction = model.Fraction;
        dial.Remaining = model.Remaining;
        dial.Dimmed = dimmed;
    }

    private void SignInButton_Click(object sender, RoutedEventArgs e) => SignInRequested?.Invoke();
}
