import Foundation

/// Decides which per-model weekly limit the third dial shows.
public enum ModelBuckets {
    /// Known per-model keys in display preference order. Fable first: it is the
    /// limit this widget was built to watch.
    private static let preference = ["seven_day_fable", "seven_day_opus", "seven_day_sonnet"]

    /// `seven_day_*` keys that are not per-model limits and must never land on
    /// the dial.
    private static let excluded: Set<String> = ["seven_day_oauth_apps", "seven_day_overage_included"]

    /// Every per-model bucket in a snapshot: the known ones in preference
    /// order, then any unrecognised model key alphabetically.
    public static func available(in snapshot: UsageSnapshot) -> [String] {
        let all = snapshot.buckets.keys.filter(isModelKey)
        let known = preference.filter(all.contains)
        let rest = all.filter { !preference.contains($0) }.sorted()
        return known + rest
    }

    static func isModelKey(_ key: String) -> Bool {
        key.hasPrefix("seven_day_") && !excluded.contains(key)
    }

    /// The bucket the dial should show: the user's pick while the server still
    /// returns it, otherwise the first available model bucket.
    public static func resolve(preferred: String?, in snapshot: UsageSnapshot) -> String? {
        let available = available(in: snapshot)
        if let preferred, available.contains(preferred) { return preferred }
        return available.first
    }

    /// "seven_day_fable" -> "FABLE"
    public static func label(for key: String) -> String {
        key.hasPrefix("seven_day_")
            ? String(key.dropFirst("seven_day_".count)).uppercased()
            : key.uppercased()
    }
}
