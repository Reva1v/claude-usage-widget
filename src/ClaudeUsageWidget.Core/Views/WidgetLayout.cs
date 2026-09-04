namespace ClaudeUsageWidget.Core;

/// How a set of things is arranged. Same three shapes at both levels — the
/// dials inside one account's block, and the account blocks on the panel.
public enum LayoutFlow
{
    /// Two columns, as many rows as needed. One item is 1x1, four is 2x2.
    Grid,
    Row,
    Column,
}

/// Where the account name goes. `Cell` puts it in the flow as one more item —
/// that is what fills the fourth square when there are three dials.
public enum NamePlacement
{
    Cell,
    Above,
    Below,
    Hidden,
}

public enum BlockItem
{
    Name,
    FiveHour,
    SevenDay,

    /// The per-model dial. Optional — ON by default, and switched off by
    /// <see cref="ModelDial"/> for an account whose model bucket is not worth
    /// a square.
    Model,

    /// The claude.ai service dial. Optional — off by default, because most of
    /// the time it says "operational" and costs a cell.
    Status,
}

/// Rows and columns for `count` items under a flow. Placement itself is WPF's
/// job — this only decides the shape, which is what the size depends on.
public static class FlowGrid
{
    /// Grid is TWO columns rather than a square root: at four items that is the
    /// original 2x2, and at three it is the same shape with one hole, so adding
    /// the fourth account never reshapes the panel.
    public static (int Rows, int Columns) Shape(LayoutFlow flow, int count)
    {
        if (count <= 0) return (0, 0);

        return flow switch
        {
            LayoutFlow.Row => (1, count),
            LayoutFlow.Column => (count, 1),
            LayoutFlow.Grid => ((count + 1) / 2, Math.Min(count, 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(flow)),
        };
    }
}

/// Track sizes for one axis of a grid: the gap between items is a track of its
/// own, so no item track carries a trailing gap.
///
/// Tracks of `size + gap` are the obvious shape and they are wrong: the last one
/// adds a gap nobody asked for, the grid stops matching the metrics the window
/// is sized from, and anything centred in such a track sits half a gap off the
/// items beside it. Both were visible on the panel — blocks separated by
/// `blockGap + gap`, and the account name 5 px below the dials it labels.
public static class GridTracks
{
    public static IReadOnlyList<double> Build(int count, double size, double gap)
    {
        if (count <= 0) return [];

        var tracks = new List<double>(count * 2 - 1);
        for (var i = 0; i < count; i++)
        {
            if (i > 0) tracks.Add(gap);
            tracks.Add(size);
        }

        return tracks;
    }

    /// Where item `index` sits, given that every other track is a gap.
    public static int TrackOf(int index) => index * 2;
}

/// The inside of one account's block.
public sealed record BlockLayout(
    LayoutFlow Flow,
    NamePlacement Name,
    IReadOnlyList<BlockItem> Order)
{
    public static readonly IReadOnlyList<BlockItem> DefaultOrder =
        [BlockItem.Name, BlockItem.FiveHour, BlockItem.SevenDay, BlockItem.Model];

    /// The two windows the widget exists for. Every layout carries both; the
    /// name, the model dial and the service dial are the optional extras, each
    /// at most once.
    public static readonly IReadOnlyList<BlockItem> RequiredItems =
        [BlockItem.FiveHour, BlockItem.SevenDay];

    public static BlockLayout Default => new(LayoutFlow.Grid, NamePlacement.Cell, DefaultOrder);

    /// Adds an optional item at the end, or takes it out. The editor moves
    /// cells; this is how one starts existing at all.
    public BlockLayout Toggle(BlockItem item) =>
        this with
        {
            Order = Order.Contains(item)
                ? Order.Where(other => other != item).ToList()
                : Order.Append(item).ToList(),
        };

    /// `Order` with two laid-out cells exchanged. Positions index `LaidOut`,
    /// because that is what the eye sees and what a drag produces.
    ///
    /// The translation is by IDENTITY — find each item in `Order` and exchange
    /// those slots — not by index arithmetic. `LaidOut` drops the name whenever
    /// its placement is not `Cell`, so with the name Above the first cell on
    /// screen is `Order[1]`, and arithmetic would have to be re-derived for
    /// every placement and again for every item added later.
    public BlockLayout SwapCells(int a, int b)
    {
        var laid = LaidOut;
        if (a == b || a < 0 || b < 0 || a >= laid.Count || b >= laid.Count) return this;

        var order = Order.ToList();
        var indexA = order.IndexOf(laid[a]);
        var indexB = order.IndexOf(laid[b]);
        (order[indexA], order[indexB]) = (order[indexB], order[indexA]);

        return this with { Order = order };
    }

    /// The items actually laid out, in order. The name is one of them only when
    /// it is placed as a cell.
    public IReadOnlyList<BlockItem> LaidOut =>
        Name == NamePlacement.Cell
            ? Order
            : Order.Where(item => item != BlockItem.Name).ToList();
}

public sealed record WidgetLayout(LayoutFlow PanelFlow, BlockLayout Block)
{
    public static WidgetLayout Default => new(LayoutFlow.Grid, BlockLayout.Default);

    /// A hand-edited settings file must not be able to produce a panel that
    /// cannot be drawn. `Order` carries the two window dials exactly once and
    /// the three optional items at most once, so anything else — a repeat, a
    /// missing window dial, an unknown name that deserialised to the default
    /// enum value — falls back to the default order rather than being patched
    /// up into something nobody asked for.
    ///
    /// Then `Status` and `Model` are made to agree with their settings, in that
    /// order: a corrupt order falls back to the default and the default then
    /// gains or loses its optional cells. The other way round, the fallback
    /// would throw those decisions away.
    public static WidgetLayout Sanitize(WidgetLayout? raw, StatusMode mode, ModelDial modelDial)
    {
        var layout = raw ?? Default;
        var order = IsUsable(layout.Block.Order) ? layout.Block.Order : BlockLayout.DefaultOrder;

        order = With(order, BlockItem.Status, mode == StatusMode.Cell);
        order = With(order, BlockItem.Model, modelDial == ModelDial.Shown);

        return layout with { Block = layout.Block with { Order = order } };
    }

    /// An optional item present exactly when it is wanted, appended at the end
    /// and removed wherever it sits. Returns the SAME list when nothing
    /// changes, which is what makes Sanitize idempotent and leaves a swapped
    /// order untouched.
    private static IReadOnlyList<BlockItem> With(
        IReadOnlyList<BlockItem> order, BlockItem item, bool wanted)
    {
        if (order.Contains(item) == wanted) return order;

        return wanted
            ? order.Append(item).ToList()
            : order.Where(other => other != item).ToList();
    }

    private static bool IsUsable(IReadOnlyList<BlockItem>? order) =>
        order is not null &&
        order.Distinct().Count() == order.Count &&
        BlockLayout.RequiredItems.All(order.Contains);
}
