namespace ClaudeUsageWidget.Core.Tests;

public class UsageDecoderTests
{
    /// Shaped after the real /api/oauth/usage payload: a flat object whose
    /// values are usage buckets, plus scalar members that are not buckets.
    private const string Payload = """
    {
      "five_hour":            { "utilization": 42,   "resets_at": "2026-07-29T18:00:00Z" },
      "seven_day":            { "utilization": 17.5, "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_opus":       { "utilization": 3,    "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_oauth_apps": { "utilization": 0,    "resets_at": null },
      "currency":             "EUR"
    }
    """;

    /// The post-Fable shape: legacy per-model fields arrive null and the real
    /// figures live in `limits` as weekly_scoped entries.
    private const string ScopedPayload = """
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
    """;

    [Fact]
    public void DecodesBuckets()
    {
        var s = UsageDecoder.Snapshot(Payload);
        Assert.Equal(4, s.Buckets.Count);
        Assert.Equal(42, s["five_hour"]!.Utilization);
        Assert.Equal(17.5, s["seven_day"]!.Utilization);
    }

    [Fact]
    public void SkipsScalars()
    {
        var s = UsageDecoder.Snapshot(Payload);
        Assert.Null(s["currency"]);
    }

    [Fact]
    public void KeepsUnknownKeys()
    {
        var s = UsageDecoder.Snapshot("""{"seven_day_fable": {"utilization": 8, "resets_at": null}}""");
        Assert.Equal(8, s["seven_day_fable"]!.Utilization);
    }

    [Fact]
    public void ParsesDates()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "a": { "utilization": 1, "resets_at": "2026-07-29T18:00:00Z" },
          "b": { "utilization": 1, "resets_at": "2026-07-29T18:00:00.123Z" }
        }
        """);
        var expected = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);
        Assert.Equal(expected, s["a"]!.ResetsAt);
        Assert.True((s["b"]!.ResetsAt!.Value - expected).TotalSeconds < 0.5);
    }

    [Fact]
    public void ParsesIsoDates()
    {
        var s = UsageDecoder.Snapshot(Payload);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_348_000), s["five_hour"]!.ResetsAt);
    }

    [Fact]
    public void ParsesEpochDates()
    {
        var s = UsageDecoder.Snapshot("""{"a": {"utilization": 1, "resets_at": 1785348000}}""");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_348_000), s["a"]!.ResetsAt);
    }

    [Fact]
    public void ToleratesMissingResetTime()
    {
        var s = UsageDecoder.Snapshot("""{"a": {"utilization": 1}}""");
        Assert.Null(s["a"]!.ResetsAt);
    }

    [Fact]
    public void RejectsBucketlessBody() =>
        Assert.Equal(UsageError.MalformedResponse,
            Assert.Throws<UsageException>(() => UsageDecoder.Snapshot("""{"currency": "EUR"}""")).Error);

    [Fact]
    public void RejectsGarbage() =>
        Assert.Equal(UsageError.MalformedResponse,
            Assert.Throws<UsageException>(() => UsageDecoder.Snapshot("<html>Just a moment</html>")).Error);

    [Fact]
    public void RejectsBooleanUtilization() =>
        Assert.Equal(UsageError.MalformedResponse,
            Assert.Throws<UsageException>(() => UsageDecoder.Snapshot("""{"a": {"utilization": true}}""")).Error);

    [Fact]
    public void RejectsBooleanResetTime()
    {
        var s = UsageDecoder.Snapshot("""{"a": {"utilization": 1, "resets_at": true}}""");
        Assert.Null(s["a"]!.ResetsAt);
    }

    [Fact]
    public void FoldsScopedLimits()
    {
        var s = UsageDecoder.Snapshot(ScopedPayload);
        Assert.Equal(8, s["seven_day_fable"]!.Utilization);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_639_540), s["seven_day_fable"]!.ResetsAt);
        Assert.Null(s["seven_day_five_hour"]);
    }

    [Fact]
    public void SlugsDisplayNames()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "five_hour": { "utilization": 1 },
          "limits": [ { "kind": "weekly_scoped", "percent": 3,
              "scope": { "model": { "display_name": "Claude Opus 4.5" } } } ]
        }
        """);
        Assert.Equal(3, s["seven_day_claude_opus_4_5"]!.Utilization);
    }

    [Fact]
    public void RealBucketWins()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "seven_day_fable": { "utilization": 42, "resets_at": null },
          "limits": [
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """);
        Assert.Equal(42, s["seven_day_fable"]!.Utilization);
    }

    [Fact]
    public void SkipsIncompleteScopedLimits()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "five_hour": { "utilization": 1 },
          "limits": [
            { "kind": "weekly_scoped", "scope": { "model": { "display_name": "NoPercent" } } },
            { "kind": "weekly_scoped", "percent": 5 },
            { "kind": "weekly_scoped", "percent": 5, "scope": { "model": {} } }
          ]
        }
        """);
        Assert.Single(s.Buckets);
    }

    [Fact]
    public void ScopedLimitsAloneAreEnough()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "limits": [
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """);
        Assert.Equal(8, s["seven_day_fable"]!.Utilization);
    }

    [Fact]
    public void ToleratesMalformedLimitEntries()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "limits": [
            "not an object",
            42,
            null,
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "Fable" } } }
          ]
        }
        """);
        Assert.Equal(8, s["seven_day_fable"]!.Utilization);
    }

    [Fact]
    public void SkipsUnsluggableDisplayNames()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "five_hour": { "utilization": 1 },
          "limits": [
            { "kind": "weekly_scoped", "percent": 8,
              "scope": { "model": { "display_name": "!!!" } } }
          ]
        }
        """);
        Assert.Null(s["seven_day_"]);
        Assert.Single(s.Buckets);
    }
}
