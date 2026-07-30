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

    @Test("the hand at zero sits above the centre")
    func handAtTop() {
        let rect = CGRect(x: 0, y: 0, width: 100, height: 100)
        let point = DialGeometry.handPoint(forFraction: 0, in: rect, inset: 20)
        #expect(abs(point.x - 50) < 0.001)
        #expect(abs(point.y - 20) < 0.001)
    }

    @Test("the hand at a quarter turn sits right of the centre")
    func handAtRight() {
        let rect = CGRect(x: 0, y: 0, width: 100, height: 100)
        let point = DialGeometry.handPoint(forFraction: 0.25, in: rect, inset: 20)
        #expect(abs(point.x - 80) < 0.001)
        #expect(abs(point.y - 50) < 0.001)
    }
}
