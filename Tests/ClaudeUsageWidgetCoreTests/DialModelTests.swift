import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("DialModel")
struct DialModelTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

    private static func snapshot(_ pairs: [String: (Double, TimeInterval?)]) -> UsageSnapshot {
        UsageSnapshot(buckets: pairs.mapValues { utilization, offset in
            UsageBucket(utilization: utilization, resetsAt: offset.map { now.addingTimeInterval($0) })
        })
    }

    @Test("builds a dial from a bucket")
    func buildsFromBucket() {
        // The offset is a Double literal: `2 * 3600` would be inferred as
        // Int and rejected by older Swift versions.
        // The reset lands two hours out; asserting the exact "2h 0m" text
        // is safe because `now` is injected here rather than read from the
        // system clock, so the result is deterministic.
        let model = DialModel.make(
            key: "five_hour",
            title: "SESSION",
            snapshot: Self.snapshot(["five_hour": (42, 7200)]),
            now: Self.now
        )
        #expect(model.title == "SESSION")
        #expect(model.fraction == 0.42)
        #expect(model.remaining == "2h 0m")
    }

    @Test("a missing bucket yields an empty dial rather than nothing")
    func missingBucket() {
        let model = DialModel.make(key: "five_hour", title: "SESSION", snapshot: Self.snapshot([:]), now: Self.now)
        #expect(model.fraction == nil)
        #expect(model.remaining == nil)
    }

    @Test("a nil snapshot yields an empty dial")
    func noSnapshot() {
        let model = DialModel.make(key: "five_hour", title: "SESSION", snapshot: nil, now: Self.now)
        #expect(model.fraction == nil)
    }

    @Test("always produces exactly three dials, session then week then model")
    func alwaysThreeDials() {
        let models = DialModel.all(
            snapshot: Self.snapshot([
                "five_hour": (10, 3600),
                "seven_day": (20, 86_400),
                "seven_day_fable": (30, 86_400),
            ]),
            preferredModelKey: nil,
            now: Self.now
        )
        #expect(models.count == 3)
        #expect(models.map(\.title) == ["SESSION", "WEEK", "FABLE"])
        #expect(models[2].fraction == 0.3)
    }

    @Test("the third dial reads MODEL and is empty when no model bucket exists")
    func noModelBucket() {
        let models = DialModel.all(
            snapshot: Self.snapshot(["five_hour": (10, 3600), "seven_day": (20, 86_400)]),
            preferredModelKey: nil,
            now: Self.now
        )
        #expect(models[2].title == "MODEL")
        #expect(models[2].fraction == nil)
    }

    @Test("the third dial honours the pinned bucket")
    func honoursPinnedBucket() {
        let models = DialModel.all(
            snapshot: Self.snapshot([
                "seven_day_fable": (30, 86_400),
                "seven_day_opus": (60, 86_400),
            ]),
            preferredModelKey: "seven_day_opus",
            now: Self.now
        )
        #expect(models[2].title == "OPUS")
        #expect(models[2].fraction == 0.6)
    }
}

@Suite("StatusLine")
struct StatusLineTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

    @Test("a fresh success shows no status line")
    func silentWhenHealthy() {
        let snapshot = UsageSnapshot(buckets: ["five_hour": UsageBucket(utilization: 1, resetsAt: nil)])
        #expect(StatusLine.text(for: .ok(snapshot, fetchedAt: Self.now), now: Self.now) == nil)
    }

    @Test("a stale success says how long ago it was fetched")
    func reportsStaleness() {
        let snapshot = UsageSnapshot(buckets: ["five_hour": UsageBucket(utilization: 1, resetsAt: nil)])
        let fetchedAt = Self.now.addingTimeInterval(-2 * 3600)
        #expect(StatusLine.text(for: .ok(snapshot, fetchedAt: fetchedAt), now: Self.now) == "updated 2h 0m ago")
    }

    @Test("each failure has its own wording")
    func reportsFailures() {
        #expect(StatusLine.text(for: .failed(.malformedResponse), now: Self.now) == "unexpected response from the API")
        #expect(StatusLine.text(for: .failed(.network("HTTP 500")), now: Self.now) == "HTTP 500")
    }

    @Test("the credential failures stay silent — BlockingNotice covers the panel for those")
    func defersToTheBlockingNotice() {
        #expect(StatusLine.text(for: .failed(.noCredentials), now: Self.now) == nil)
        #expect(StatusLine.text(for: .failed(.unauthorized), now: Self.now) == nil)
    }

    @Test("the first load says so")
    func reportsLoading() {
        #expect(StatusLine.text(for: .loading, now: Self.now) == "loading…")
    }
}
