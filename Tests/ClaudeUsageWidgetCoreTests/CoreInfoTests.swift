import Testing
@testable import ClaudeUsageWidgetCore

@Suite("CoreInfo")
struct CoreInfoTests {
    @Test("the version comes from the bundle Sparkle also reads")
    func readsBundleVersion() {
        #expect(CoreInfo.version(fromInfoDictionary: ["CFBundleShortVersionString": "1.2.3"]) == "1.2.3")
    }

    @Test("a bundle without a version string says so instead of inventing one")
    func missingVersion() {
        #expect(CoreInfo.version(fromInfoDictionary: nil) == "unknown")
        #expect(CoreInfo.version(fromInfoDictionary: [:]) == "unknown")
        #expect(CoreInfo.version(fromInfoDictionary: ["CFBundleShortVersionString": ""]) == "unknown")
    }

    @Test("version is never empty, whatever the host bundle looks like")
    func versionIsPresent() {
        #expect(!CoreInfo.version.isEmpty)
    }
}
