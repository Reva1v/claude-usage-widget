import Testing
@testable import ClaudeUsageWidgetCore

@Suite("CoreInfo")
struct CoreInfoTests {
    @Test("version is a non-empty dotted string")
    func versionIsPresent() {
        #expect(!CoreInfo.version.isEmpty)
        #expect(CoreInfo.version.contains("."))
    }
}
