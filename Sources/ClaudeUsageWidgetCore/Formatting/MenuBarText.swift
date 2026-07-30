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
            return MenuBarMetric(label: String(initial), value: model.fraction.map(UsageMath.percentText) ?? "—")
        }
    }
}
