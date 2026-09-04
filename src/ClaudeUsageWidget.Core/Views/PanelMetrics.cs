namespace ClaudeUsageWidget.Core;

/// One account's block: the dial grid, plus the name line when it is not a cell.
/// <param name="NameHeight">The whole name row — the name, plus the plan line
/// under it when that is switched on. Zero when the name is a cell or hidden.</param>
public sealed record BlockMetrics(
    double Width,
    double Height,
    int Rows,
    int Columns,
    double NameHeight)
{
    /// Height of the name line at scale 1 — the 11 pt name plus its leading.
    public const double NameLineHeight = 13;

    public static BlockMetrics For(
        BlockLayout layout, double dial, double gap, double scale,
        PlanLine planLine = PlanLine.Hidden)
    {
        var (rows, columns) = FlowGrid.Shape(layout.Flow, layout.LaidOut.Count);
        var width = columns * dial + Math.Max(0, columns - 1) * gap;
        var height = rows * dial + Math.Max(0, rows - 1) * gap;

        // Above/Below cost a line and a gap — the same gap that separates the
        // dials, so the block keeps one rhythm instead of two.
        //
        // The plan is a second line under the name, in the caption's size, and
        // it is RESERVED here rather than left to WPF: the panel is sized from
        // these numbers, and a row that grows itself at arrange time would push
        // the dials into the status band. Reserved from the SETTING, never from
        // whether a label happens to have arrived — otherwise the panel would
        // change height the first time a poll answered.
        //
        // The name in a cell needs nothing: it already owns a whole dial square,
        // and two short lines fit inside one with room to spare.
        var nameHeight = layout.Name is NamePlacement.Above or NamePlacement.Below
            ? NameLineHeight * scale + (planLine == PlanLine.Shown ? PanelMetrics.CaptionLineHeight * scale : 0)
            : 0;
        if (nameHeight > 0) height += nameHeight + gap;

        return new BlockMetrics(width, height, rows, columns, nameHeight);
    }
}

/// The edit toolbar's geometry. The strip floats OUTSIDE the panel: the window
/// grows by `Height` when the mode is entered and the strip is drawn in the
/// extra band, so the rounded panel keeps the size — and, once the window's top
/// moves with it, the screen position — it has with the mode off.
public sealed record ToolbarMetrics(
    int Rows,
    int Columns,
    double ButtonSize,
    double Gap,
    double Height)
{
    /// Was the 16 pt the deleted hover header gave its eye and its lock, and had
    /// to shrink when the plan button made nine: 9*16 + 8*2 = 160 against the
    /// default panel's 146 pt of inner width. 14 is what fits — 9*14 + 8*2 = 142.
    /// The GLYPH did not shrink with it: the view still draws it at `9 * scale`,
    /// so the icons are exactly as legible as before and only their box is
    /// tighter — checked by eye on `toolbar-min-above.png` at 9x, where nothing
    /// clips. The eight-button note this replaces called 16 the ceiling and a
    /// ninth button impossible at any pitch: it was the ceiling for the SIZE,
    /// not for the count, and the pitch it would have taken is zero.
    public const double BaseButtonSize = 14;

    /// Between buttons at scale 1. Not derived from the dial gap: at 3 the nine
    /// buttons need 150 and wrap, at 2 they need 142 and fit. Both numbers scale
    /// from the same side as the panel, so the fit holds across the whole range.
    public const double BaseButtonGap = 2;

    /// Done, accounts, dials, name, plan, status, model, lock, hide.
    public const int ButtonCount = 9;

    /// `inner` is the panel width minus its padding — all the room the toolbar
    /// has, because it is not allowed to widen the panel: the panel is sized
    /// from the dials, and a wider toolbar would make it jump sideways the
    /// moment the mode is entered.
    public static ToolbarMetrics For(double inner, double gap, double scale)
    {
        var button = BaseButtonSize * scale;
        var pitch = BaseButtonGap * scale;

        var columns = ButtonCount;
        var rows = 1;
        if (columns * button + (columns - 1) * pitch > inner)
        {
            // The arithmetic above says this cannot happen for a panel sized
            // from a full block of dials — it is the fallback for a panel
            // narrower than any that ships, and two balanced rows beat one row
            // and an orphan. It does not promise a fit: nothing widens the
            // panel to hold the strip.
            columns = (ButtonCount + 1) / 2;
            rows = 2;
        }

        // Plus one full dial gap between the strip and the panel, so the band
        // outside keeps the rhythm of the dials inside.
        var height = rows * button + (rows - 1) * pitch + gap;

        return new ToolbarMetrics(rows, columns, button, pitch, height);
    }
}

/// Panel dimensions for a layout and an account count.
///
/// The panel used to be a square, with `WidgetSide` driving both axes so a
/// resize could not distort it. Rows of accounts broke the square, but the
/// invariant survives: `side` is still ONE number, and both axes are derived
/// from it here — the shape is a property of the layout, not of the drag.
///
/// Dial size, gap and padding are deliberately NOT configurable: they come from
/// the theme and scale together. Letting them be set independently is how a
/// panel ends up unreadable with no way back.
/// <param name="ToolbarReserve">`Toolbar.Height` while edit mode is on, 0
/// otherwise. It is a band OUTSIDE the rounded panel: the view pushes the
/// border away from the strip by exactly this and the window moves its top by
/// the same number, so one number decides the window's height, which side the
/// strip is on, and that the panel does not move at all.</param>
/// <param name="StatusBand">The bottom of the panel, from the last content down
/// to the edge: the status caption with one <see cref="Padding"/> above it and
/// one below. Reserved whether or not the line has anything to say, so the panel
/// does not change height when the service goes down or the edit hint
/// appears.</param>
/// <param name="PlanLine">Whether the plan is drawn as a second line under the
/// account name. Carried here rather than read separately by the view, so what
/// the panel was SIZED for and what it draws are one value.</param>
public sealed record PanelMetrics(
    double Width,
    double Height,
    double DialSize,
    double Padding,
    double Gap,
    double BlockGap,
    double Scale,
    int Rows,
    int Columns,
    BlockMetrics Block,
    ToolbarMetrics Toolbar,
    double ToolbarReserve,
    double StatusBand,
    PlanLine PlanLine)
{
    /// One line of the status caption at scale 1: Consolas at
    /// Theme.CaptionFontSize (8) has a line spacing of 1.1709 em — measured
    /// through FormattedText, not assumed, because the panel's whole vertical
    /// rhythm is built around it and a wrong number tilts the band.
    ///
    /// Public because the plan line under the account name is the same caption
    /// in the same font: <see cref="BlockMetrics"/> reserves one of these, and
    /// the tests compare against it rather than restating the number.
    public const double CaptionLineHeight = 8 * 1.1708984375;

    /// The caption's line box at this scale. The view gives the status
    /// TextBlock exactly this height, so what Core reserved and what WPF draws
    /// cannot drift apart.
    public double CaptionLine => CaptionLineHeight * Scale;

    /// Panel top edge to the first content — the name row when the name is
    /// above, the first dial otherwise.
    public double TopGap => Padding;

    /// Last content to the status text, and the status text to the bottom edge.
    /// Derived from the band rather than restated, so asserting that it equals
    /// <see cref="TopGap"/> is a check on how the band was built.
    public double BottomGap => (StatusBand - CaptionLine) / 2;

    /// At the design size a dial is this wide: (170 - 12*2 - 10) / 2.
    private const double BaseDialSize = 68;

    private const double BasePadding = 12;

    /// Same value as Theme.Gap — the panel and the dials must share one rhythm.
    private const double BaseGap = 10;

    public static PanelMetrics For(
        WidgetLayout layout, int accountCount, double side, bool editMode = false,
        PlanLine planLine = PlanLine.Hidden)
    {
        var scale = WidgetSettings.ClampSide(side) / WidgetSettings.DefaultSide;

        var padding = BasePadding * scale;
        var gap = BaseGap * scale;
        var dial = BaseDialSize * scale;

        // Blocks are separated by TWICE the gap between the dials inside one.
        // At equal gaps the grouping disappears: four 2x2 squares read as one
        // 4x4 grid, and the name stops looking like it belongs to its own
        // dials. Seen on an offscreen render, not deduced.
        var blockGap = gap * 2;

        var block = BlockMetrics.For(layout.Block, dial, gap, scale, planLine);

        // A panel with no accounts still draws its frame and the "add an
        // account" notice, so it is sized for one block rather than collapsing.
        var count = Math.Max(accountCount, 1);
        var (rows, columns) = FlowGrid.Shape(layout.PanelFlow, count);

        var width = padding * 2 + columns * block.Width + Math.Max(0, columns - 1) * blockGap;

        // Measured against the width the dials already decided: the toolbar
        // wraps to fit the panel, the panel never widens to fit the toolbar.
        var toolbar = ToolbarMetrics.For(width - padding * 2, gap, scale);
        var reserve = editMode ? toolbar.Height : 0;

        // One padding above the caption and one below it. The panel used to
        // reserve a flat 16 pt band and centre the dial grid in everything but
        // the padding, which put 13 pt of slack over the names, 15 under the
        // last dial and 2 under the text: "the top and bottom padding look
        // visually inconsistent; service operational is stuck right at the
        // bottom" (the user, 2026-09-04). With the caption's own line height
        // as the middle term, the top edge, the gap over the text and the gap
        // under it are one number — the padding — and the panel reads the
        // same on all four sides.
        var statusBand = padding * 2 + CaptionLineHeight * scale;

        // Top padding, the content, then the band: no slack anywhere else, so
        // the grid sits flush under the padding and nothing is centred into a
        // gap it does not own.
        var height = padding + rows * block.Height + Math.Max(0, rows - 1) * blockGap
                     + statusBand + reserve;

        return new PanelMetrics(
            width, height, dial, padding, gap, blockGap, scale, rows, columns, block, toolbar,
            reserve, statusBand, planLine);
    }
}
