import SwiftUI

/// The widget palette: a dark glass panel with pastel dials, in the spirit of
/// the sibling mole-widget.
public enum Theme {
    public static let panel = Color(red: 0.118, green: 0.133, blue: 0.188)
    public static let track = Color(red: 0.250, green: 0.270, blue: 0.340)
    public static let text = Color(red: 0.780, green: 0.800, blue: 0.870)
    public static let dim = Color(red: 0.450, green: 0.470, blue: 0.550)

    public static let accent = Color(red: 0.651, green: 0.820, blue: 0.537)
    public static let warning = Color(red: 0.898, green: 0.784, blue: 0.565)
    public static let danger = Color(red: 0.906, green: 0.510, blue: 0.518)
    /// Scheduled maintenance: informational, not alarming.
    public static let info = Color(red: 0.541, green: 0.706, blue: 0.902)

    public static func color(for level: ThresholdLevel) -> Color {
        switch level {
        case .ok: accent
        case .warning: warning
        case .danger: danger
        }
    }

    public static func color(for status: ServiceStatus) -> Color {
        switch status {
        case .operational: accent
        case .degraded, .partialOutage: warning
        case .majorOutage: danger
        case .maintenance: info
        case .unknown: dim
        }
    }

    /// Fonts are defined at the 170 pt design size and scale with the panel.
    /// Monospaced digits everywhere, so numbers do not jitter as they tick.
    public static func label(scale: Double) -> Font {
        .system(size: 8 * scale, weight: .semibold).monospacedDigit()
    }

    public static func value(scale: Double) -> Font {
        .system(size: 14 * scale, weight: .semibold).monospacedDigit()
    }

    public static func caption(scale: Double) -> Font {
        .system(size: 8 * scale, weight: .medium).monospacedDigit()
    }
}
