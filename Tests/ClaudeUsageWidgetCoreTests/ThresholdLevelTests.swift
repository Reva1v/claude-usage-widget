import Testing
@testable import ClaudeUsageWidgetCore

@Suite("ThresholdLevel")
struct ThresholdLevelTests {
    @Test("below 60 percent is ok")
    func okBand() {
        #expect(ThresholdLevel.level(for: 0) == .ok)
        #expect(ThresholdLevel.level(for: 0.59) == .ok)
    }

    @Test("60 percent up to 85 is a warning")
    func warningBand() {
        #expect(ThresholdLevel.level(for: 0.6) == .warning)
        #expect(ThresholdLevel.level(for: 0.84) == .warning)
    }

    @Test("85 percent and above is danger")
    func dangerBand() {
        #expect(ThresholdLevel.level(for: 0.85) == .danger)
        #expect(ThresholdLevel.level(for: 1) == .danger)
    }
}
