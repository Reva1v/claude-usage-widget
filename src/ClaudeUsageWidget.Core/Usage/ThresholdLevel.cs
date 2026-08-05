namespace ClaudeUsageWidget.Core;

/// Насколько тревожна доля использования. Отделено от палитры цветов,
/// чтобы пороги можно было протестировать без обращения к UI.
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
