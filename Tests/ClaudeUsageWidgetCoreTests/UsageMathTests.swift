import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageMath")
struct UsageMathTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)
    static let fiveHours: TimeInterval = 5 * 3600
    static let sevenDays: TimeInterval = 7 * 24 * 3600

    // MARK: windowLength

    @Test("the session bucket is a five hour window")
    func sessionWindow() {
        #expect(UsageMath.windowLength(forKey: "five_hour") == Self.fiveHours)
    }

    @Test("every seven_day bucket is a seven day window")
    func weeklyWindow() {
        #expect(UsageMath.windowLength(forKey: "seven_day") == Self.sevenDays)
        #expect(UsageMath.windowLength(forKey: "seven_day_fable") == Self.sevenDays)
        #expect(UsageMath.windowLength(forKey: "seven_day_opus") == Self.sevenDays)
    }

    @Test("an unrecognised key has no known window")
    func unknownWindow() {
        #expect(UsageMath.windowLength(forKey: "extra_usage") == nil)
    }

    // MARK: elapsedFraction

    @Test("halfway through the window reads as 0.5")
    func midWindow() {
        let resetsAt = Self.now.addingTimeInterval(Self.fiveHours / 2)
        let fraction = UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now)
        #expect(fraction == 0.5)
    }

    @Test("a window that just opened reads as 0")
    func freshWindow() {
        let resetsAt = Self.now.addingTimeInterval(Self.fiveHours)
        #expect(UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now) == 0)
    }

    @Test("a reset time longer away than the window clamps to 0")
    func overlongWindow() {
        let resetsAt = Self.now.addingTimeInterval(Self.fiveHours * 2)
        #expect(UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now) == 0)
    }

    @Test("a reset time in the past yields nil rather than a guessed angle")
    func expiredWindow() {
        let resetsAt = Self.now.addingTimeInterval(-60)
        #expect(UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now) == nil)
    }

    @Test("a missing reset time or window yields nil")
    func missingInputs() {
        #expect(UsageMath.elapsedFraction(resetsAt: nil, window: Self.fiveHours, now: Self.now) == nil)
        #expect(UsageMath.elapsedFraction(resetsAt: Self.now.addingTimeInterval(60), window: nil, now: Self.now) == nil)
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
