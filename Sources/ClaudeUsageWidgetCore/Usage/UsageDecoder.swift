import Foundation

/// Turns a raw /api/oauth/usage body into a snapshot.
///
/// Uses JSONSerialization rather than Codable because the response is an open
/// map: unknown keys must survive, and members that are not buckets (a plain
/// currency string, for instance) must be ignored instead of failing the parse.
public enum UsageDecoder {
    public static func snapshot(from data: Data) throws -> UsageSnapshot {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw UsageError.malformedResponse
        }

        var buckets: [String: UsageBucket] = [:]
        for (key, value) in root {
            guard let object = value as? [String: Any],
                  let utilization = object["utilization"] as? Double,
                  !isJSONBoolean(object["utilization"]) else { continue }
            buckets[key] = UsageBucket(
                utilization: utilization,
                resetsAt: resetDate(from: object["resets_at"])
            )
        }

        foldScopedLimits(from: root, into: &buckets)

        guard !buckets.isEmpty else { throw UsageError.malformedResponse }
        return UsageSnapshot(buckets: buckets)
    }

    /// Accepts both an ISO 8601 string and a unix timestamp. The exact wire
    /// format could not be observed while writing this, so both are handled.
    private static func resetDate(from value: Any?) -> Date? {
        if let string = value as? String {
            return fractionalFormatter.date(from: string) ?? plainFormatter.date(from: string)
        }
        if let seconds = value as? Double,
           seconds > 0,
           !isJSONBoolean(value) {
            return Date(timeIntervalSince1970: seconds)
        }
        return nil
    }

    /// Distinguishes JSON boolean values from numeric values. JSONSerialization
    /// creates different CF types for booleans (__NSCFBoolean) and numbers
    /// (__NSCFNumber), so we can check using CFGetTypeID().
    private static func isJSONBoolean(_ value: Any?) -> Bool {
        guard let value = value as? NSNumber else { return false }
        return CFGetTypeID(value as CFTypeRef) == CFBooleanGetTypeID()
    }

    /// Folds per-model weekly limits from the `limits` array into synthetic
    /// `seven_day_<model>` buckets.
    ///
    /// Since the Fable launch the server leaves the legacy top-level
    /// `seven_day_<model>` fields null and reports scoped models only here, so
    /// without this the per-model dial has nothing to show. Synthesising the
    /// same key shape the flat fields used means every consumer downstream —
    /// bucket selection, the dial, the menu picker — keeps working unchanged,
    /// and a per-model limit added later appears on its own.
    private static func foldScopedLimits(from root: [String: Any], into buckets: inout [String: UsageBucket]) {
        guard let limits = root["limits"] as? [[String: Any]] else { return }

        for limit in limits {
            guard limit["kind"] as? String == "weekly_scoped",
                  let percent = limit["percent"] as? Double,
                  !isJSONBoolean(limit["percent"]),
                  let scope = limit["scope"] as? [String: Any],
                  let model = scope["model"] as? [String: Any],
                  let displayName = model["display_name"] as? String
            else { continue }

            let key = "seven_day_" + slug(displayName)
            // A real top-level bucket is authoritative; this only fills gaps.
            guard buckets[key] == nil else { continue }
            buckets[key] = UsageBucket(
                utilization: percent,
                resetsAt: resetDate(from: limit["resets_at"])
            )
        }
    }

    /// "Claude Opus 4.5" -> "claude_opus_4_5"
    private static func slug(_ displayName: String) -> String {
        let lowered = displayName.lowercased()
        let parts = lowered.split(whereSeparator: { !$0.isLetter && !$0.isNumber })
        return parts.joined(separator: "_")
    }

    private static let plainFormatter = ISO8601DateFormatter()

    private static let fractionalFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()
}
