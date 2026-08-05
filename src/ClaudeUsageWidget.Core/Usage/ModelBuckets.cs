namespace ClaudeUsageWidget.Core;

/// Decides which per-model weekly limit the third dial shows.
public static class ModelBuckets
{
    /// Known per-model keys in display preference order. Fable first: it is the
    /// limit this widget was built to watch.
    private static readonly string[] Preference = ["seven_day_fable", "seven_day_opus", "seven_day_sonnet"];

    /// `seven_day_*` keys that are not per-model limits and must never land on
    /// the dial.
    private static readonly HashSet<string> Excluded = ["seven_day_oauth_apps", "seven_day_overage_included"];

    /// Every per-model bucket in a snapshot: the known ones in preference
    /// order, then any unrecognised model key alphabetically.
    public static IReadOnlyList<string> Available(UsageSnapshot snapshot)
    {
        var all = snapshot.Buckets.Keys.Where(IsModelKey).ToList();
        var known = Preference.Where(all.Contains);
        var rest = all.Where(key => !Preference.Contains(key)).OrderBy(key => key, StringComparer.Ordinal);
        return known.Concat(rest).ToList();
    }

    internal static bool IsModelKey(string key) =>
        key.StartsWith("seven_day_", StringComparison.Ordinal) && !Excluded.Contains(key);

    /// The bucket the dial should show: the user's pick while the server still
    /// returns it, otherwise the first available model bucket.
    public static string? Resolve(string? preferred, UsageSnapshot snapshot)
    {
        var available = Available(snapshot);
        if (preferred is not null && available.Contains(preferred)) return preferred;
        return available.Count > 0 ? available[0] : null;
    }

    /// "seven_day_fable" -> "FABLE"
    public static string Label(string key) =>
        key.StartsWith("seven_day_", StringComparison.Ordinal)
            ? key["seven_day_".Length..].ToUpperInvariant()
            : key.ToUpperInvariant();
}
