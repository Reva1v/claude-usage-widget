namespace ClaudeUsageWidget.Core;

/// How alarming the usage fraction is. Kept separate from the color palette
/// so the thresholds can be tested without touching the UI.
public enum ThresholdLevel
{
    Ok,
    Warning,
    Danger,
}

public static class Thresholds
{
    public static ThresholdLevel Level(double fraction) =>
        fraction < 0.6 ? ThresholdLevel.Ok
        : fraction < 0.85 ? ThresholdLevel.Warning
        : ThresholdLevel.Danger;
}
