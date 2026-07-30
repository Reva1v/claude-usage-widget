import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageMath")
struct UsageMathTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

    // MARK: clockFraction

    /// Calendar pinned to UTC so the assertions do not depend on the machine's zone.
    static let utc: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar
    }()

    @Test("a reset at 20:00 sits at the 8 o'clock position")
    func eightPM() {
        // 2026-07-29 20:00:00 UTC
        let resetsAt = Date(timeIntervalSince1970: 1_785_355_200)
        let fraction = UsageMath.clockFraction(resetsAt: resetsAt, now: Self.now, calendar: Self.utc)
        #expect(fraction == 8.0 / 12.0)
    }

    @Test("a reset at midnight or noon sits at twelve")
    func twelvePositions() {
        // 2026-07-30 00:00:00 UTC
        let midnight = Date(timeIntervalSince1970: 1_785_369_600)
        #expect(UsageMath.clockFraction(resetsAt: midnight, now: Self.now, calendar: Self.utc) == 0)
    }

    @Test("half hours land between the numerals")
    func halfHour() {
        // 2026-07-29 18:30:00 UTC
        let resetsAt = Date(timeIntervalSince1970: 1_785_349_800)
        let fraction = UsageMath.clockFraction(resetsAt: resetsAt, now: Self.now, calendar: Self.utc)
        #expect(fraction == 6.5 / 12.0)
    }

    @Test("a reset in the past or absent yields no hand")
    func hiddenHand() {
        #expect(UsageMath.clockFraction(resetsAt: Self.now.addingTimeInterval(-60), now: Self.now, calendar: Self.utc) == nil)
        #expect(UsageMath.clockFraction(resetsAt: nil, now: Self.now, calendar: Self.utc) == nil)
    }

    // MARK: remainingText

    @Test("under a minute reads in seconds")
    func remainingSeconds() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(59), now: Self.now) == "59s")
    }

    @Test("under an hour reads in minutes")
    func remainingMinutes() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(600), now: Self.now) == "10m")
    }

    @Test("exactly one hour reads as hours and minutes")
    func remainingExactHour() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(3600), now: Self.now) == "1h 0m")
    }

    @Test("over a day reads as days and hours")
    func remainingDays() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(90_000), now: Self.now) == "1d 1h")
    }

    @Test("a reset time in the past has no remaining text")
    func remainingExpired() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(-1), now: Self.now) == nil as String?)
        #expect(UsageMath.remainingText(resetsAt: nil as Date?, now: Self.now) == nil as String?)
    }

    // MARK: fraction

    @Test("utilization converts from the server's 0-100 scale and clamps")
    func fractionScaling() {
        #expect(UsageMath.fraction(0) == 0)
        #expect(UsageMath.fraction(42) == 0.42)
        #expect(UsageMath.fraction(100) == 1)
        #expect(UsageMath.fraction(140) == 1)
        #expect(UsageMath.fraction(-5) == 0)
    }
}
