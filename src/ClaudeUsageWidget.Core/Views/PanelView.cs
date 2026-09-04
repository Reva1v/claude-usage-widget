namespace ClaudeUsageWidget.Core;

/// The two named shapes of the panel. Each is a layout plus a status mode;
/// the editor can still move cells inside either, and a hand-arranged panel
/// simply matches neither name.
public enum PanelView
{
    /// The panel as it was before accounts existed: a 2x2 grid of dials with
    /// no account name and the service status as a dial of its own. There is
    /// no caption band under the grid, so the panel is a plain rounded square.
    Classic,

    /// One row per account: the account name in a cell, dials beside it, and
    /// the service status as one line under all the rows.
    Accounts,
}

public static class PanelViews
{
    public static readonly IReadOnlyList<BlockItem> ClassicOrder =
        [BlockItem.FiveHour, BlockItem.SevenDay, BlockItem.Model, BlockItem.Status];

    public static WidgetLayout LayoutFor(PanelView view) => view switch
    {
        PanelView.Classic => new WidgetLayout(
            LayoutFlow.Grid, new BlockLayout(LayoutFlow.Grid, NamePlacement.Hidden, ClassicOrder)),
        _ => WidgetLayout.Default,
    };

    public static StatusMode StatusFor(PanelView view) =>
        view == PanelView.Classic ? StatusMode.Cell : StatusMode.Line;

    /// The view a settings file that never chose one gets. One account is the
    /// panel people already know; a second account is what the rows exist for.
    public static PanelView DefaultFor(int accountCount) =>
        accountCount <= 1 ? PanelView.Classic : PanelView.Accounts;

    /// Which named view the current layout is, or null when the editor has
    /// taken it somewhere between the two (a hidden name with a status line,
    /// say). The tray ticks the match and ticks nothing for null.
    public static PanelView? Current(WidgetLayout layout, StatusMode mode) =>
        (layout.Block.Name, mode) switch
        {
            (NamePlacement.Hidden, StatusMode.Cell) => PanelView.Classic,
            (not NamePlacement.Hidden, StatusMode.Line) => PanelView.Accounts,
            _ => null,
        };
}

/// Everything the panel's geometry and paint depend on, resolved once from
/// the settings file. The layout is already sanitized against the two switches.
public sealed record ResolvedLayout(
    WidgetLayout Layout,
    StatusMode Status,
    ModelDial ModelDial,
    PlanLine PlanLine);

public static class LayoutResolution
{
    /// The one place that turns a settings file into a drawable layout.
    ///
    /// A file with neither a layout nor a status mode — a fresh install, or one
    /// written before either setting existed — takes the named view for its
    /// account count, so a single-account upgrade keeps the panel it had. The
    /// first layout save writes both fields and the choice stops depending on
    /// the count.
    public static ResolvedLayout Resolve(WidgetSettingsData data, int accountCount)
    {
        var modelDial = ModelDials.Resolve(data.ModelDial);
        var planLine = PlanLines.Resolve(data.PlanLine);

        WidgetLayout? layout;
        StatusMode mode;
        if (data.Layout is null && data.StatusMode is null)
        {
            var view = PanelViews.DefaultFor(accountCount);
            layout = PanelViews.LayoutFor(view);
            mode = PanelViews.StatusFor(view);
        }
        else
        {
            layout = data.Layout;
            mode = StatusModes.Resolve(data.StatusMode, data.Layout);
        }

        return new ResolvedLayout(WidgetLayout.Sanitize(layout, mode, modelDial), mode, modelDial, planLine);
    }

    /// The settings for a named view, written on top of the current file so
    /// the model-dial and plan-line switches survive the change.
    public static WidgetSettingsData Apply(WidgetSettingsData data, PanelView view)
    {
        var mode = PanelViews.StatusFor(view);
        var modelDial = ModelDials.Resolve(data.ModelDial);
        return data with
        {
            Layout = WidgetLayout.Sanitize(PanelViews.LayoutFor(view), mode, modelDial),
            StatusMode = mode,
            ModelDial = modelDial,
        };
    }
}
