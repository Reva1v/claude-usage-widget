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

    @Test("a boolean utilization is not a bucket")
    func rejectsBooleanUtilization() {
        #expect(throws: UsageError.malformedResponse) {
            try UsageDecoder.snapshot(from: Data(#"{"a": {"utilization": true}}"#.utf8))
        }
    }

    @Test("a boolean reset time decodes as nil, not as the epoch")
    func rejectsBooleanResetTime() throws {
        let data = Data(#"{"a": {"utilization": 1, "resets_at": true}}"#.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["a"]?.resetsAt == nil)
    }

    /// The post-Fable shape: legacy per-model fields arrive null and the real
    /// figures live in `limits` as weekly_scoped entries.
    static let scopedPayload = Data("""
    {
      "five_hour":      { "utilization": 57, "resets_at": "2026-07-29T20:00:00Z" },
      "seven_day":      { "utilization": 38, "resets_at": "2026-08-02T03:00:00Z" },
      "seven_day_opus": null,
      "limits": [
        { "kind": "weekly_scoped", "percent": 8, "resets_at": "2026-08-02T02:59:00Z",
          "scope": { "model": { "display_name": "Fable" } } },
        { "kind": "five_hour", "percent": 57 }
      ]
    }
    """.utf8)

    @Test("a weekly_scoped limit becomes a synthetic per-model bucket")
    func foldsScopedLimits() throws {
        let snapshot = try UsageDecoder.snapshot(from: Self.scopedPayload)
        #expect(snapshot["seven_day_fable"]?.utilization == 8)
        #expect(snapshot["seven_day_fable"]?.resetsAt == Date(timeIntervalSince1970: 1_785_639_540))
    }

    @Test("limits entries that are not weekly_scoped are ignored")
    func ignoresOtherLimitKinds() throws {
        let snapshot = try UsageDecoder.snapshot(from: Self.scopedPayload)
        #expect(snapshot["seven_day_five_hour"] == nil)
        #expect(snapshot.buckets.keys.filter { $0.hasPrefix("seven_day_") } == ["seven_day_fable"])
    }

    @Test("a display name is slugged into the bucket key")
    func slugsDisplayNames() throws {
        let data = Data("""
        {
          "five_hour": { "utilization": 1 },
          "limits": [
            { "kind": "weekly_scoped", "percent": 3,
              "scope": { "model": { "display_name": "Claude Opus 4.5" } } }
          ]
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["seven_day_claude_opus_4_5"]?.utilization == 3)
    }

    @Test("a real top-level bucket wins over a synthetic one")
    func realBucketWins() throws {
        let data = Data("""
        {
          "seven_day_fable": { "utilization": 42, "resets_at": null },
          "limits": [
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["seven_day_fable"]?.utilization == 42)
    }

    @Test("a scoped limit without a percent or a display name is skipped")
    func skipsIncompleteScopedLimits() throws {
        let data = Data("""
        {
          "five_hour": { "utilization": 1 },
          "limits": [
            { "kind": "weekly_scoped", "scope": { "model": { "display_name": "NoPercent" } } },
            { "kind": "weekly_scoped", "percent": 5 },
            { "kind": "weekly_scoped", "percent": 5, "scope": { "model": {} } }
          ]
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot.buckets.count == 1)
    }

    @Test("a body carrying only scoped limits still decodes")
    func scopedLimitsAloneAreEnough() throws {
        let data = Data("""
        {
          "limits": [
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["seven_day_fable"]?.utilization == 8)
    }

    @Test("a malformed limits entry does not discard its well-formed siblings")
    func toleratesMalformedLimitEntries() throws {
        let data = Data("""
        {
          "limits": [
            "not an object",
            42,
            null,
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["seven_day_fable"]?.utilization == 8)
    }

    @Test("a display name with no letters or digits is skipped, not turned into a bare key")
    func skipsUnsluggableDisplayNames() throws {
        let data = Data("""
        {
          "five_hour": { "utilization": 1 },
          "limits": [
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "!!!" } } }
          ]
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["seven_day_"] == nil)
        #expect(snapshot.buckets.count == 1)
    }
}
