import CoreGraphics
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("DialGeometry")
struct DialGeometryTests {
    @Test("zero points at twelve o'clock")
    func startsAtTop() {
        #expect(DialGeometry.angle(forFraction: 0).degrees == -90)
    }

    @Test("a quarter turn points at three o'clock")
    func quarterTurn() {
        #expect(DialGeometry.angle(forFraction: 0.25).degrees == 0)
    }

    @Test("a full turn comes back to twelve o'clock")
    func fullTurn() {
        #expect(DialGeometry.angle(forFraction: 1).degrees == 270)
    }
}
