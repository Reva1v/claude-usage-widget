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
/// Labels are short words — the menu bar has room for them. A column is as wide
/// as the wider of its two rows, and the percentage below is drawn at 14 pt
/// against the label's 10 pt, so `100%` already sets the width for any label
/// of four characters or fewer.
public enum MenuBarText {
    /// A menu bar label short enough to cost nothing: a column is as wide as
    /// the wider of its two rows, and the percentage below is drawn at 14 pt
    /// against the label's 10 pt, so `100%` already sets the width for any
    /// label of four characters or fewer. Longer titles are cut to three.
    static func label(for title: String) -> String {
        title.count <= 4 ? title : String(title.prefix(3))
    }

    public static func metrics(for models: [DialModel]) -> [MenuBarMetric] {
        models.compactMap { model in
            guard !model.title.isEmpty else { return nil }
            return MenuBarMetric(label: label(for: model.title), value: model.fraction.map(UsageMath.percentText) ?? "—")
        }
    }
}
