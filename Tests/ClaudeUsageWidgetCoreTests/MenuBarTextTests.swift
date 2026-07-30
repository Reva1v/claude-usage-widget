import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("MenuBarText")
struct MenuBarTextTests {
    private func model(_ title: String, _ fraction: Double?) -> DialModel {
        DialModel(title: title, fraction: fraction, remaining: nil)
    }

    @Test("each dial becomes a short word and a rounded percentage")
    func buildsMetrics() {
        let metrics = MenuBarText.metrics(for: [
            model("SESSION", 0.57),
            model("WEEK", 0.38),
            model("FABLE", 0.08),
        ])
        #expect(metrics == [
            MenuBarMetric(label: "SES", value: "57%"),
            MenuBarMetric(label: "WEEK", value: "38%"),
            MenuBarMetric(label: "FAB", value: "8%"),
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
        #expect(metrics == [MenuBarMetric(label: "MOD", value: "—")])
    }

    @Test("a title of four characters or fewer is kept whole")
    func shortTitlesSurviveIntact() {
        #expect(MenuBarText.metrics(for: [model("OPUS", 0.1)]).first?.label == "OPUS")
        #expect(MenuBarText.metrics(for: [model("W", 0.1)]).first?.label == "W")
    }

    @Test("a longer title is cut to three characters")
    func longTitlesAreCut() {
        #expect(MenuBarText.metrics(for: [model("SONNET", 0.1)]).first?.label == "SON")
        #expect(MenuBarText.metrics(for: [model("FIVE", 0.1)]).first?.label == "FIVE")
        #expect(MenuBarText.metrics(for: [model("FIVER", 0.1)]).first?.label == "FIV")
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
