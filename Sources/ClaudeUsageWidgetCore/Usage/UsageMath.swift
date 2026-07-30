import Foundation

/// Pure arithmetic behind the dials. No I/O, no clock reads — `now` is always
/// passed in so every case is reproducible in a test.
public enum UsageMath {
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

    /// A 0...1 fraction as a whole-percent string, e.g. "57%".
    ///
    /// Nudged by a tiny epsilon before rounding: a fraction that came from
    /// dividing an exact server percentage by 100 can land just under the .5
    /// boundary in binary — 0.575 * 100 is 57.49999999999999 — and would round
    /// down against every expectation. The nudge is far smaller than any real
    /// difference in the data.
    public static func percentText(_ fraction: Double) -> String {
        "\(Int((fraction * 100 + 1e-9).rounded()))%"
    }
}
