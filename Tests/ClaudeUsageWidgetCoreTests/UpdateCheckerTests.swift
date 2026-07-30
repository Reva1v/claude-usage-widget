import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UpdateChecker")
struct UpdateCheckerTests {
    @Test("a higher component means newer")
    func higherComponent() {
        #expect(UpdateChecker.isNewer("0.2.0", than: "0.1.0"))
        #expect(UpdateChecker.isNewer("1.0.0", than: "0.9.9"))
        #expect(UpdateChecker.isNewer("0.1.1", than: "0.1.0"))
    }

    @Test("the same version is not newer")
    func sameVersion() {
        #expect(!UpdateChecker.isNewer("0.1.0", than: "0.1.0"))
    }

    @Test("an older version is not newer")
    func olderVersion() {
        #expect(!UpdateChecker.isNewer("0.1.0", than: "0.2.0"))
        #expect(!UpdateChecker.isNewer("0.9.9", than: "1.0.0"))
    }

    @Test("a leading v is ignored, as GitHub tags carry one")
    func stripsTagPrefix() {
        #expect(UpdateChecker.isNewer("v0.2.0", than: "0.1.0"))
        #expect(!UpdateChecker.isNewer("v0.1.0", than: "0.1.0"))
    }

    @Test("missing trailing components count as zero")
    func differingLengths() {
        #expect(UpdateChecker.isNewer("0.2", than: "0.1.9"))
        #expect(!UpdateChecker.isNewer("0.1", than: "0.1.0"))
        #expect(UpdateChecker.isNewer("0.1.0.1", than: "0.1.0"))
    }

    @Test("numeric comparison, not lexicographic")
    func numericNotLexicographic() {
        #expect(UpdateChecker.isNewer("0.10.0", than: "0.9.0"))
        #expect(!UpdateChecker.isNewer("0.9.0", than: "0.10.0"))
    }

    @Test("an unparseable version is never newer")
    func garbageIsNeverNewer() {
        #expect(!UpdateChecker.isNewer("", than: "0.1.0"))
        #expect(!UpdateChecker.isNewer("banana", than: "0.1.0"))
    }
}
