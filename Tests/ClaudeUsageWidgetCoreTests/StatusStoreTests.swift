import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@MainActor
@Suite("StatusStore")
struct StatusStoreTests {
    @Test("starts out unknown")
    func startsUnknown() {
        let store = StatusStore { .operational }
        #expect(store.status == .unknown)
    }

    @Test("a successful load publishes the status")
    func publishesStatus() async {
        let store = StatusStore { .degraded }
        await store.load()
        #expect(store.status == .degraded)
    }

    @Test("a failed load leaves the last known status alone")
    func keepsLastStatus() async {
        final class Box: @unchecked Sendable { var shouldFail = false }
        let box = Box()
        let store = StatusStore {
            if box.shouldFail { throw UsageError.network("offline") }
            return .operational
        }

        await store.load()
        box.shouldFail = true
        await store.load()

        #expect(store.status == .operational)
    }
}
