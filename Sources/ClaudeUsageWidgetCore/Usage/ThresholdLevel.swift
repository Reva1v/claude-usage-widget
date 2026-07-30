/// How alarming a utilization fraction is. Kept separate from the palette so
/// the thresholds are testable without touching SwiftUI.
public enum ThresholdLevel: Equatable, Sendable {
    case ok
    case warning
    case danger

    public static func level(for fraction: Double) -> ThresholdLevel {
        switch fraction {
        case ..<0.6: .ok
        case ..<0.85: .warning
        default: .danger
        }
    }
}
