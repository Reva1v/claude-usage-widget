import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageMath")
struct UsageMathTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

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

    // MARK: percentText

    @Test("percent text rounds at the boundary rather than falling to binary error")
    func percentTextRounding() {
        #expect(UsageMath.percentText(0.575) == "58%")
        #expect(UsageMath.percentText(0.574) == "57%")
        #expect(UsageMath.percentText(0) == "0%")
        #expect(UsageMath.percentText(1) == "100%")
    }
}
