import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@MainActor
@Suite("UsageStore")
struct UsageStoreTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

    private static func snapshot(_ utilization: Double) -> UsageSnapshot {
        UsageSnapshot(buckets: ["five_hour": UsageBucket(utilization: utilization, resetsAt: nil)])
    }

    private func store(
        token: String? = "token",
        fetch: @escaping @Sendable (String) async throws -> UsageSnapshot
    ) -> UsageStore {
        let fixedNow = Self.now
        return UsageStore(fetch: fetch, tokenProvider: { token }, now: { fixedNow })
    }

    @Test("starts out loading")
    func startsLoading() {
        let store = store { _ in await Self.snapshot(1) }
        #expect(store.state == .loading)
    }

    @Test("a successful load publishes the snapshot and the fetch time")
    func publishesSnapshot() async {
        let store = store { _ in await Self.snapshot(42) }
        await store.load()
        #expect(store.state == .ok(Self.snapshot(42), fetchedAt: Self.now))
        #expect(store.lastSnapshot == Self.snapshot(42))
    }

    @Test("a missing token fails without calling the endpoint")
    func failsWithoutToken() async {
        let store = store(token: nil) { _ in
            Issue.record("fetch must not run without a token")
            return await Self.snapshot(1)
        }
        await store.load()
        #expect(store.state == .failed(.noCredentials))
    }

    @Test("a rejected token surfaces as unauthorized")
    func surfacesUnauthorized() async {
        let store = store { _ in throw UsageError.unauthorized }
        await store.load()
        #expect(store.state == .failed(.unauthorized))
    }

    @Test("an unexpected error surfaces as a network error")
    func wrapsUnknownErrors() async {
        struct Boom: Error {}
        let store = store { _ in throw Boom() }
        await store.load()
        if case .failed(.network) = store.state {} else {
            Issue.record("expected a network failure, got \(store.state)")
        }
    }

    @Test("a failure after a success keeps the last snapshot on screen")
    func keepsLastSnapshotOnFailure() async {
        final class Box: @unchecked Sendable { var shouldFail = false }
        let box = Box()
        let store = store { _ in
            if box.shouldFail { throw UsageError.unauthorized }
            return await Self.snapshot(42)
        }

        await store.load()
        box.shouldFail = true
        await store.load()

        #expect(store.state == .failed(.unauthorized))
        #expect(store.lastSnapshot == Self.snapshot(42))
    }

    @Test("the token is passed through to the fetch")
    func passesToken() async {
        final class Box: @unchecked Sendable { var seen: String? }
        let box = Box()
        let store = store(token: "sk-ant-oat01-example") { token in
            box.seen = token
            return await Self.snapshot(1)
        }
        await store.load()
        #expect(box.seen == "sk-ant-oat01-example")
    }

    @Test("a rate limit with a Retry-After pauses polling until the deadline")
    func pausesOnRateLimit() async {
        final class Box: @unchecked Sendable { var calls = 0 }
        let box = Box()
        let store = store { _ in
            box.calls += 1
            throw UsageError.rateLimited(retryAfterSeconds: 600)
        }

        await store.load()
        #expect(store.state == .failed(.rateLimited(retryAfterSeconds: 600)))
        #expect(store.retryPausedUntil == Self.now.addingTimeInterval(600))

        await store.load()
        #expect(box.calls == 1)
    }

    @Test("polling resumes once the rate-limit deadline has passed")
    func resumesAfterRateLimitDeadline() async {
        final class Clock: @unchecked Sendable { var now = Date.distantPast }
        final class Box: @unchecked Sendable { var calls = 0 }
        let clock = Clock()
        clock.now = Self.now
        let box = Box()
        let store = UsageStore(
            fetch: { _ in
                box.calls += 1
                if box.calls == 1 { throw UsageError.rateLimited(retryAfterSeconds: 600) }
                return await Self.snapshot(7)
            },
            tokenProvider: { "token" },
            now: { clock.now }
        )

        await store.load()
        clock.now = Self.now.addingTimeInterval(601)
        await store.load()

        #expect(box.calls == 2)
        #expect(store.state == .ok(Self.snapshot(7), fetchedAt: clock.now))
        #expect(store.retryPausedUntil == nil)
    }

    @Test("a rate limit without a Retry-After keeps the normal polling cadence")
    func rateLimitWithoutRetryAfterDoesNotPause() async {
        final class Box: @unchecked Sendable { var calls = 0 }
        let box = Box()
        let store = store { _ in
            box.calls += 1
            throw UsageError.rateLimited(retryAfterSeconds: nil)
        }

        await store.load()
        #expect(store.retryPausedUntil == nil)

        await store.load()
        #expect(box.calls == 2)
    }

    @Test("a refresh while one is already in flight does not start a second fetch")
    func coalescesOverlappingRefreshes() async {
        final class Box: @unchecked Sendable { var calls = 0 }
        let box = Box()
        let store = store { _ in
            box.calls += 1
            try? await Task.sleep(for: .milliseconds(50))
            return await Self.snapshot(1)
        }

        async let first: Void = store.load()
        async let second: Void = store.load()
        _ = await (first, second)

        #expect(box.calls == 1)
    }
}
