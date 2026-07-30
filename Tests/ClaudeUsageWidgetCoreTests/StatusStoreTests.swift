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

    @Test("a load while one is already in flight does not start a second fetch")
    func coalescesOverlappingLoads() async {
        final class Box: @unchecked Sendable { var calls = 0 }
        let box = Box()
        let store = StatusStore {
            box.calls += 1
            try? await Task.sleep(for: .milliseconds(50))
            return .operational
        }

        async let first: Void = store.load()
        async let second: Void = store.load()
        _ = await (first, second)

        #expect(box.calls == 1)
    }
}
