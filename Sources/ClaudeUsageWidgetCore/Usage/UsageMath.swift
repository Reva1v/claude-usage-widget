import Foundation

/// Pure arithmetic behind the dials. No I/O, no clock reads — `now` is always
/// passed in so every case is reproducible in a test.
public enum UsageMath {
    /// How long the window behind a bucket key runs.
    ///
    /// The API returns only `resets_at`, never the length of the window, so the
    /// length is derived from the key: `five_hour` is a rolling five hours and
    /// every `seven_day*` bucket is a rolling seven days.
    public static func windowLength(forKey key: String) -> TimeInterval? {
        if key == "five_hour" { return 5 * 3600 }
        if key.hasPrefix("seven_day") { return 7 * 24 * 3600 }
        return nil
    }

    /// How far the current window has already run, as 0...1 — the angle of the
    /// dial's hand.
    ///
    /// Returns nil when the answer is unknowable: no reset time, no window
    /// length, or a reset time that has already passed. A stale snapshot must
    /// hide the hand rather than draw it at a guessed angle.
    public static func elapsedFraction(resetsAt: Date?, window: TimeInterval?, now: Date) -> Double? {
        guard let resetsAt, let window, window > 0 else { return nil }
        let remaining = resetsAt.timeIntervalSince(now)
        guard remaining > 0 else { return nil }
        return min(max((window - remaining) / window, 0), 1)
    }

    /// Time left in the window: "45s", "10m", "1h 0m", "1d 1h".
    /// Nil when there is no reset time or it has already passed.
    public static func remainingText(resetsAt: Date?, now: Date) -> String? {
        guard let resetsAt else { return nil }
        let seconds = Int(resetsAt.timeIntervalSince(now).rounded(.down))
        guard seconds > 0 else { return nil }

        let days = seconds / 86_400
        let hours = (seconds % 86_400) / 3600
        let minutes = (seconds % 3600) / 60

        if days > 0 { return "\(days)d \(hours)h" }
        if hours > 0 { return "\(hours)h \(minutes)m" }
        if minutes > 0 { return "\(minutes)m" }
        return "\(seconds)s"
    }

    /// The server reports utilization on a 0...100 scale; everything inside the
    /// widget works in 0...1.
    public static func fraction(_ utilization: Double) -> Double {
        min(max(utilization / 100, 0), 1)
    }
}
