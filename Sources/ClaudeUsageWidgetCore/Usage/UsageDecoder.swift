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

    private static let plainFormatter = ISO8601DateFormatter()

    private static let fractionalFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()
}
