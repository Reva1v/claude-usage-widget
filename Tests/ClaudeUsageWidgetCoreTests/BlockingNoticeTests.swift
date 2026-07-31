import Testing
@testable import ClaudeUsageWidgetCore
import Foundation

@Suite("BlockingNotice")
struct BlockingNoticeTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)
    static let snapshot = UsageSnapshot(buckets: ["five_hour": UsageBucket(utilization: 1, resetsAt: nil)])

    @Test("an expired token points at Claude Code, which is the only thing that can refresh it")
    func coversUnauthorized() {
        let notice = BlockingNotice.make(for: .failed(.unauthorized))
        #expect(notice == BlockingNotice(title: "Session expired", detail: "Open Claude Code to refresh"))
    }

    @Test("no credentials at all asks for a sign-in")
    func coversNoCredentials() {
        let notice = BlockingNotice.make(for: .failed(.noCredentials))
        #expect(notice == BlockingNotice(title: "Not signed in", detail: "Sign in to Claude Code"))
    }

    @Test("states whose dials still mean something stay uncovered")
    func leavesUsableStatesAlone() {
        #expect(BlockingNotice.make(for: .loading) == nil)
        #expect(BlockingNotice.make(for: .ok(Self.snapshot, fetchedAt: Self.now)) == nil)
        #expect(BlockingNotice.make(for: .failed(.network("HTTP 500"))) == nil)
        #expect(BlockingNotice.make(for: .failed(.malformedResponse)) == nil)
    }

    @Test("the notice and the status line never speak at once")
    func doesNotDoubleUpWithTheStatusLine() {
        let states: [UsageStore.State] = [
            .loading,
            .ok(Self.snapshot, fetchedAt: Self.now),
            .failed(.noCredentials),
            .failed(.unauthorized),
            .failed(.malformedResponse),
            .failed(.network("HTTP 500")),
        ]
        for state in states {
            let notice = BlockingNotice.make(for: state)
            let line = StatusLine.text(for: state, now: Self.now)
            #expect(notice == nil || line == nil, "both spoke for \(state)")
        }
    }
}
