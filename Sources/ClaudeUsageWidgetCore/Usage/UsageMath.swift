import Foundation

/// Pure arithmetic behind the dials. No I/O, no clock reads — `now` is always
/// passed in so every case is reproducible in a test.
public enum UsageMath {
    /// Where the hand points: the wall-clock position of the reset time on a
    /// 12-hour dial, as 0...1 of a full revolution from twelve o'clock.
    /// A reset at 20:00 local time puts the hand where a watch's hour hand
    /// would sit at 8 o'clock.
    ///
    /// Nil when there is no reset time or it has already passed — a stale
    /// snapshot hides the hand rather than pointing at a wrong time.
    public static func clockFraction(resetsAt: Date?, now: Date, calendar: Calendar = .current) -> Double? {
        guard let resetsAt, resetsAt > now else { return nil }
        let parts = calendar.dateComponents([.hour, .minute, .second], from: resetsAt)
        let seconds = Double((parts.hour ?? 0) % 12) * 3600
            + Double(parts.minute ?? 0) * 60
            + Double(parts.second ?? 0)
        return seconds / (12 * 3600)
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
