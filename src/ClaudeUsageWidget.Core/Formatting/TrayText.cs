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

    /// The dial the tray ICON draws: the one chosen in "Tray shows", unless
    /// that account has no such limit. The icon is one number and "∞" is not
    /// one, so a 5H choice falls back to the week, which such an account always
    /// has. The setting is left alone: another account may have the window.
    public static int IconIndex(int chosen, IReadOnlyList<DialModel> dials) =>
        chosen < dials.Count && dials[chosen].NoLimit ? WeekIndex : chosen;

    /// `DialModel.All` order: 5H, 7D, the model.
    private const int WeekIndex = 1;

    /// "42%", "∞" for a window the account does not have, "—" for no data.
    internal static string ValueFor(DialModel model) =>
        model.NoLimit ? "∞" : model.Fraction is { } fraction ? UsageMath.PercentText(fraction) : "—";

    public static IReadOnlyList<TrayMetric> Metrics(IReadOnlyList<DialModel> models) =>
        models
            .Where(model => model.Title.Length > 0)
            .Select(model => new TrayMetric(LabelFor(model.Title), ValueFor(model)))
            .ToList();
}

/// One account's group in the taskbar band.
public sealed record BandEntry(string Name, string Percent, string? ResetsIn);

/// The taskbar band shows every account, but only its 5H figure: adding 7D
/// doubles the width and starts colliding with the tray icons.
public static class BandText
{
    /// The tray ICON budgets four characters because the icon is 16 px wide
    /// (<see cref="TrayText.LabelFor"/>); the band is a line of text and has
    /// no such limit. Four turned "default" into "defa" on screen. Eight fits
    /// the names people actually use — "personal", "work-alt" — and the column
    /// is only as wide as its widest line anyway.
    private const int MaxNameLength = 8;

    public static IReadOnlyList<BandEntry> Entries(IReadOnlyList<AccountRow> rows) =>
        rows.Select(row => new BandEntry(
            row.DisplayName.Length <= MaxNameLength ? row.DisplayName : row.DisplayName[..MaxNameLength],
            TrayText.ValueFor(row.Dials[0]),
            row.SessionResetsIn)).ToList();

    /// The band as it was before accounts existed: the tray account's three
    /// columns — 5H, 7D, the model — label over percentage, no reset times.
    /// The same columns the tray tooltip lists, so the two never disagree.
    public static IReadOnlyList<BandEntry> MetricEntries(AccountRow row) =>
        TrayText.Metrics(row.Dials)
            .Select(metric => new BandEntry(metric.Label, metric.Value, null))
            .ToList();

    // The band used to flatten an entry into a TrayMetric — name and reset time
    // sharing the top line, percentage below — because the renderer drew
    // exactly two lines of one size each. That put the name beside a number it
    // does not belong to. The renderer now draws the percentage and the time as
    // one bottom line of two sizes, so the entry reaches it whole.
}
