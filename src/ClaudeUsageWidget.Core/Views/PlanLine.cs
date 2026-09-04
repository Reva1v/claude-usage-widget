namespace ClaudeUsageWidget.Core;

/// Whether the subscription plan is drawn as a second line under the account
/// name. Off unless asked for: the widget is about the limits, and a panel that
/// grew a row on upgrade would be a surprise nobody chose. The user turns it on
/// from the toolbar ("I want it to show the subscription tier, a separate
/// button to show/hide it", 2026-09-04).
public enum PlanLine
{
    Hidden,
    Shown,
}

public static class PlanLines
{
    /// The value a settings file means. Null is a file written before the
    /// setting, and it reads as Hidden — the opposite of
    /// <see cref="ModelDials.Resolve"/>, and for the opposite reason: nothing is
    /// taken away by defaulting off, because the line has never been drawn
    /// before. There is nothing to read off the layout either; the plan has
    /// never been a cell.
    public static PlanLine Resolve(PlanLine? saved) => saved ?? PlanLine.Hidden;
}
