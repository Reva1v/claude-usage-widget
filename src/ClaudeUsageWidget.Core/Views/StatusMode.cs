namespace ClaudeUsageWidget.Core;

/// Where the claude.ai service state is shown. Two values and no "off": the
/// state is always visible one way or the other, because the complaint that
/// started this was three dials repeating one fact, not the fact itself.
public enum StatusMode
{
    /// A STATUS dial in every block, in the cell grid, draggable like any other.
    Cell,

    /// One line at the bottom of the panel, for all blocks, always visible.
    Line,
}

public static class StatusModes
{
    /// The mode a settings file means, including one written before the setting
    /// existed.
    ///
    /// `StatusMode` is the master and `Order` follows it — but an old file only
    /// carries the order, so the FIRST read takes the mode off it and every
    /// read after that is the saved value (the first save writes it back).
    /// Defaulting to `Line` unconditionally would delete the status cell of
    /// anyone who had switched it on from the tray item this design removes.
    /// Null-safe the whole way down: `IsUsable` already anticipates an `Order`
    /// that deserialised to null, and this runs BEFORE `Sanitize` on every
    /// settings read — a `"Order": null` in the file would otherwise crash the
    /// startup path that used to survive it.
    public static StatusMode Resolve(StatusMode? saved, WidgetLayout? layout) =>
        saved ?? (layout?.Block?.Order?.Contains(BlockItem.Status) == true
            ? StatusMode.Cell
            : StatusMode.Line);
}
