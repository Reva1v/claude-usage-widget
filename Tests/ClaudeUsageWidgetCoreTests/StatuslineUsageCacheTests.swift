import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("StatuslineUsageCache")
struct StatuslineUsageCacheTests {
    static let payload = Data("""
    {
      "updated_at": 1785700800,
      "rate_limits": {
        "five_hour": { "used_percentage": 42, "resets_at": 1785708000 },
        "seven_day": { "used_percentage": 17.5, "resets_at": 1786132800 }
      }
    }
    """.utf8)

    @Test("decodes the official Claude Code statusline rate-limit fields")
    func decodesRateLimits() throws {
        let snapshot = try StatuslineUsageCache.snapshot(from: Self.payload)
        #expect(snapshot["five_hour"]?.utilization == 42)
        #expect(snapshot["seven_day"]?.utilization == 17.5)
        #expect(snapshot["five_hour"]?.resetsAt == Date(timeIntervalSince1970: 1_785_708_000))
        #expect(snapshot.sourceUpdatedAt == Date(timeIntervalSince1970: 1_785_700_800))
    }

    @Test("accepts a partial snapshot while Claude Code is still populating fields")
    func acceptsOneBucket() throws {
        let data = Data(#"{"updated_at":1785700800,"rate_limits":{"five_hour":{"used_percentage":7}}}"#.utf8)
        let snapshot = try StatuslineUsageCache.snapshot(from: data)
        #expect(snapshot.buckets.count == 1)
        #expect(snapshot["five_hour"]?.utilization == 7)
    }

    @Test("rejects cache files without usable rate limits")
    func rejectsEmptyCache() {
        #expect(throws: UsageError.malformedResponse) {
            try StatuslineUsageCache.snapshot(from: Data(#"{"rate_limits":{}}"#.utf8))
        }
    }

    @Test("a missing file is a cache miss")
    func missingFile() {
        let url = URL(fileURLWithPath: "/tmp/claude-usage-widget-does-not-exist")
        #expect(StatuslineUsageCache.snapshot(at: url) == nil)
    }
}
