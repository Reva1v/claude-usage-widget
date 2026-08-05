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
    ///
    /// SESSION/WEEK — особый случай: пользователь выбрал подписи временных
    /// окон ("5H"/"7D"), а не имён лимитов, так что колонка сразу понятна
    /// без предварительного знания, что "SESSION" — это 5-часовое окно, а
    /// "WEEK" — 7-дневное. Общее правило (≤4 символов целиком / длиннее —
    /// первые три) остаётся для остальных заголовков, включая имена моделей.
    internal static string LabelFor(string title) => title switch
    {
        "SESSION" => "5H",
        "WEEK" => "7D",
        _ => title.Length <= 4 ? title : title[..3],
    };

    public static IReadOnlyList<TrayMetric> Metrics(IReadOnlyList<DialModel> models) =>
        models
            .Where(model => model.Title.Length > 0)
            .Select(model => new TrayMetric(
                LabelFor(model.Title),
                model.Fraction is { } fraction ? UsageMath.PercentText(fraction) : "—"))
            .ToList();
}
