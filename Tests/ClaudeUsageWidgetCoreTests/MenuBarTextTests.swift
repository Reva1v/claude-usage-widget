import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("MenuBarText")
struct MenuBarTextTests {
    private func model(_ title: String, _ fraction: Double?) -> DialModel {
        DialModel(title: title, fraction: fraction, remaining: nil)
    }

    @Test("each dial becomes an initial and a rounded percentage")
    func buildsMetrics() {
        let metrics = MenuBarText.metrics(for: [
            model("SESSION", 0.57),
            model("WEEK", 0.38),
            model("FABLE", 0.08),
        ])
        #expect(metrics == [
            MenuBarMetric(label: "S", value: "57%"),
            MenuBarMetric(label: "W", value: "38%"),
            MenuBarMetric(label: "F", value: "8%"),
        ])
    }

    @Test("percentages round rather than truncate")
    func rounds() {
        let metrics = MenuBarText.metrics(for: [model("SESSION", 0.575)])
        #expect(metrics.first?.value == "58%")
    }

    @Test("a dial with no figure shows a dash, keeping the column in place")
    func missingFraction() {
        let metrics = MenuBarText.metrics(for: [model("MODEL", nil)])
        #expect(metrics == [MenuBarMetric(label: "M", value: "—")])
    }

    @Test("an empty title yields no metric rather than a blank column")
    func emptyTitle() {
        #expect(MenuBarText.metrics(for: [model("", 0.5)]).isEmpty)
    }

    @Test("no dials means nothing to draw")
    func noModels() {
        #expect(MenuBarText.metrics(for: []).isEmpty)
    }
}
