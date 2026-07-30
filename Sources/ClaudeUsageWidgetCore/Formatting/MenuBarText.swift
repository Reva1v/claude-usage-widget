import Foundation

/// One menu bar column: a small label over a larger figure.
public struct MenuBarMetric: Equatable, Sendable {
    public let label: String
    public let value: String

    public init(label: String, value: String) {
        self.label = label
        self.value = value
    }
}

/// Builds the menu bar's columns from the same dials the panel shows.
///
/// Labels are a single initial — the menu bar is crowded and three spelled-out
/// words would crowd it further. Taking them from `DialModel.title` means the
/// third column follows whichever model the dial is showing.
public enum MenuBarText {
    public static func metrics(for models: [DialModel]) -> [MenuBarMetric] {
        models.compactMap { model in
            guard let initial = model.title.first else { return nil }
            return MenuBarMetric(label: String(initial), value: model.fraction.map(percentText) ?? "—")
        }
    }

    /// `Double` can't represent decimal fractions like 0.575 exactly — it lands
    /// a hair below it (0.57499999999999995...), so a plain `(fraction *
    /// 100).rounded()` truncates 57.5% down to 57 instead of up to 58. The
    /// epsilon nudges past that representation error without affecting any
    /// genuine value, which is many orders of magnitude larger.
    private static func percentText(_ fraction: Double) -> String {
        let epsilon = 1e-9
        return "\(Int((fraction * 100 + epsilon).rounded()))%"
    }
}
