import Foundation

/// Reads the rate-limit snapshot Claude Code supplies to statusline commands.
///
/// Claude Code already obtains these figures as part of its normal traffic.
/// Using its local snapshot avoids a second client polling the undocumented
/// `/api/oauth/usage` endpoint and tripping Cloudflare's abuse protection.
public enum StatuslineUsageCache {
    public static let fileName = "claude-usage-widget.json"

    public static var defaultURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appending(path: ".claude/cache", directoryHint: .isDirectory)
            .appending(path: fileName)
    }

    /// A missing or partially-written cache is a cache miss, not an app error;
    /// the caller can fall back to the network path.
    public static func snapshot(at url: URL = defaultURL) -> UsageSnapshot? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? snapshot(from: data)
    }

    static func snapshot(from data: Data) throws -> UsageSnapshot {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let rateLimits = root["rate_limits"] as? [String: Any]
        else { throw UsageError.malformedResponse }

        var buckets: [String: UsageBucket] = [:]
        addBucket(named: "five_hour", from: rateLimits["five_hour"], to: &buckets)
        addBucket(named: "seven_day", from: rateLimits["seven_day"], to: &buckets)

        guard !buckets.isEmpty else { throw UsageError.malformedResponse }
        return UsageSnapshot(
            buckets: buckets,
            sourceUpdatedAt: number(root["updated_at"]).map(Date.init(timeIntervalSince1970:))
        )
    }

    private static func addBucket(named name: String, from value: Any?, to buckets: inout [String: UsageBucket]) {
        guard let object = value as? [String: Any],
              let used = number(object["used_percentage"])
        else { return }

        buckets[name] = UsageBucket(
            utilization: used,
            resetsAt: number(object["resets_at"]).map(Date.init(timeIntervalSince1970:))
        )
    }

    private static func number(_ value: Any?) -> Double? {
        guard let number = value as? NSNumber,
              CFGetTypeID(number as CFTypeRef) != CFBooleanGetTypeID()
        else { return nil }
        return number.doubleValue
    }
}
