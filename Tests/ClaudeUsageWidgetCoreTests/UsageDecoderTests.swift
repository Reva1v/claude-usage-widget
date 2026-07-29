import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageDecoder")
struct UsageDecoderTests {
    /// Shaped after the real /api/oauth/usage payload: a flat object whose
    /// values are usage buckets, plus scalar members that are not buckets.
    static let payload = Data("""
    {
      "five_hour":            { "utilization": 42,   "resets_at": "2026-07-29T18:00:00Z" },
      "seven_day":            { "utilization": 17.5, "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_opus":       { "utilization": 3,    "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_oauth_apps": { "utilization": 0,    "resets_at": null },
      "currency":             "EUR"
    }
    """.utf8)

    @Test("keeps every object member that carries a utilization")
    func decodesBuckets() throws {
        let snapshot = try UsageDecoder.snapshot(from: Self.payload)
        #expect(snapshot.buckets.count == 4)
        #expect(snapshot["five_hour"]?.utilization == 42)
        #expect(snapshot["seven_day"]?.utilization == 17.5)
    }

    @Test("drops members that are not usage buckets")
    func skipsScalars() throws {
        let snapshot = try UsageDecoder.snapshot(from: Self.payload)
        #expect(snapshot["currency"] == nil)
    }

    @Test("keeps an unknown bucket key")
    func keepsUnknownKeys() throws {
        let data = Data(#"{"seven_day_fable": {"utilization": 8, "resets_at": null}}"#.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["seven_day_fable"]?.utilization == 8)
    }

    @Test("parses an ISO 8601 reset time, with or without fractional seconds")
    func parsesDates() throws {
        let data = Data("""
        {
          "a": { "utilization": 1, "resets_at": "2026-07-29T18:00:00Z" },
          "b": { "utilization": 1, "resets_at": "2026-07-29T18:00:00.123Z" }
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        let expected = Date(timeIntervalSince1970: 1_785_348_000)
        #expect(snapshot["a"]?.resetsAt == expected)
        #expect(snapshot["b"]?.resetsAt?.timeIntervalSince(expected) ?? 1 < 0.5)
    }

    @Test("parses a numeric reset time as a unix timestamp")
    func parsesEpochDates() throws {
        let data = Data(#"{"a": {"utilization": 1, "resets_at": 1785348000}}"#.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["a"]?.resetsAt == Date(timeIntervalSince1970: 1_785_348_000))
    }

    @Test("a missing reset time decodes as nil, not as an error")
    func toleratesMissingResetTime() throws {
        let data = Data(#"{"a": {"utilization": 1}}"#.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["a"]?.resetsAt == nil)
    }

    @Test("a body with no buckets is malformed")
    func rejectsBucketlessBody() {
        #expect(throws: UsageError.malformedResponse) {
            try UsageDecoder.snapshot(from: Data(#"{"currency": "EUR"}"#.utf8))
        }
    }

    @Test("a non-JSON body is malformed")
    func rejectsGarbage() {
        #expect(throws: UsageError.malformedResponse) {
            try UsageDecoder.snapshot(from: Data("<html>Just a moment</html>".utf8))
        }
    }
}
