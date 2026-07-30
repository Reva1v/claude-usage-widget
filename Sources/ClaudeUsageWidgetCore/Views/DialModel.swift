import Foundation

/// Everything one dial needs, derived from a snapshot. A plain value so the
/// derivation is testable without SwiftUI.
public struct DialModel: Equatable, Sendable {
    public let title: String
    public let fraction: Double?
    public let elapsed: Double?
    public let remaining: String?

    public static func make(key: String, title: String, snapshot: UsageSnapshot?, now: Date) -> DialModel {
        guard let bucket = snapshot?[key] else {
            return DialModel(title: title, fraction: nil, elapsed: nil, remaining: nil)
        }
        return DialModel(
            title: title,
            fraction: UsageMath.fraction(bucket.utilization),
            elapsed: UsageMath.elapsedFraction(
                resetsAt: bucket.resetsAt,
                window: UsageMath.windowLength(forKey: key),
                now: now
            ),
            remaining: UsageMath.remainingText(resetsAt: bucket.resetsAt, now: now)
        )
    }

    /// The three dials, always in the same order and always all three present —
    /// a dial with no data reads as `n/a` rather than disappearing and shifting
    /// the layout.
    public static func all(snapshot: UsageSnapshot?, preferredModelKey: String?, now: Date) -> [DialModel] {
        let modelKey = snapshot.flatMap { ModelBuckets.resolve(preferred: preferredModelKey, in: $0) }
        return [
            make(key: "five_hour", title: "SESSION", snapshot: snapshot, now: now),
            make(key: "seven_day", title: "WEEK", snapshot: snapshot, now: now),
            make(
                key: modelKey ?? "",
                title: modelKey.map(ModelBuckets.label(for:)) ?? "MODEL",
                snapshot: snapshot,
                now: now
            ),
        ]
    }
}

/// The line under the dials. Nil means everything is fine and the widget should
/// stay quiet.
public enum StatusLine {
    /// A snapshot older than three missed refreshes is worth calling out.
    static let staleAfter: TimeInterval = 15 * 60

    public static func text(for state: UsageStore.State, now: Date) -> String? {
        switch state {
        case .loading:
            return "loading…"
        case let .ok(_, fetchedAt):
            let age = now.timeIntervalSince(fetchedAt)
            guard age >= staleAfter else { return nil }
            let ago = UsageMath.remainingText(resetsAt: now, now: fetchedAt)
            return ago.map { "updated \($0) ago" }
        case .failed(.noCredentials):
            return "no Claude Code credentials found"
        case .failed(.unauthorized):
            return "token rejected — sign in to Claude Code"
        case .failed(.malformedResponse):
            return "unexpected response from the API"
        case let .failed(.network(message)):
            return message
        }
    }
}
