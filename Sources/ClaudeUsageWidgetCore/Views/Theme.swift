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

    public static func color(for level: ThresholdLevel) -> Color {
        switch level {
        case .ok: accent
        case .warning: warning
        case .danger: danger
        }
    }

    /// Monospaced digits everywhere, so numbers do not jitter as they tick.
    public static let label = Font.system(size: 8, weight: .semibold).monospacedDigit()
    public static let value = Font.system(size: 14, weight: .semibold).monospacedDigit()
    public static let caption = Font.system(size: 8, weight: .medium).monospacedDigit()
}
