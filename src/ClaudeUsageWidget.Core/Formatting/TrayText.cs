namespace ClaudeUsageWidget.Core;

/// One tray column: a small label over a larger figure.
public sealed record TrayMetric(string Label, string Value);

/// Builds the tray's columns from the same dials the panel shows.
///
/// Labels are short words — the tray has room for them. A column is as wide
/// as the wider of its two rows, and the percentage below is drawn larger
/// than the label, so "100%" already sets the width for any label of four
/// characters or fewer.
public static class TrayText
{
    /// A tray label short enough to cost nothing: a column is as wide as the
    /// wider of its two rows, and the percentage below is drawn larger than
    /// the label, so "100%" already sets the width for any label of four
    /// characters or fewer. Longer titles are cut to three.
    internal static string LabelFor(string title) =>
        title.Length <= 4 ? title : title[..3];

    public static IReadOnlyList<TrayMetric> Metrics(IReadOnlyList<DialModel> models) =>
        models
            .Where(model => model.Title.Length > 0)
            .Select(model => new TrayMetric(
                LabelFor(model.Title),
                model.Fraction is { } fraction ? UsageMath.PercentText(fraction) : "—"))
            .ToList();
}
