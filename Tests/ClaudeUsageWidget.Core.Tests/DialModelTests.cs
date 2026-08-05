namespace ClaudeUsageWidget.Core.Tests;

public class DialModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    private static UsageSnapshot Snapshot(Dictionary<string, (double Utilization, double? OffsetSeconds)> pairs) =>
        new(pairs.ToDictionary(
            pair => pair.Key,
            pair => new UsageBucket(pair.Value.Utilization, pair.Value.OffsetSeconds is { } offset ? Now.AddSeconds(offset) : null)));

    [Fact]
    public void BuildsFromBucket()
    {
        // The reset lands two hours out; asserting the exact "2h 0m" text is
        // safe because `now` is injected here rather than read from the
        // system clock, so the result is deterministic.
        var model = DialModel.Make(
            "five_hour",
            "SESSION",
            Snapshot(new() { ["five_hour"] = (42, 7200) }),
            Now);
        Assert.Equal("SESSION", model.Title);
        Assert.Equal(0.42, model.Fraction!.Value, 10);
        Assert.Equal("2h 0m", model.Remaining);
    }

    [Fact]
    public void MissingBucketYieldsEmptyDial()
    {
        var model = DialModel.Make("five_hour", "SESSION", Snapshot(new()), Now);
        Assert.Null(model.Fraction);
        Assert.Null(model.Remaining);
    }

    [Fact]
    public void NoSnapshotYieldsEmptyDial()
    {
        var model = DialModel.Make("five_hour", "SESSION", null, Now);
        Assert.Null(model.Fraction);
    }

    [Fact]
    public void AllProducesThreeDialsInFixedOrder()
    {
        var s = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(42, null),
            ["seven_day"] = new(17.5, null),
            ["seven_day_fable"] = new(8, null),
        });
        var dials = DialModel.All(s, null, Now);
        Assert.Equal(3, dials.Count);
        Assert.Equal(new[] { "SESSION", "WEEK", "FABLE" }, dials.Select(d => d.Title));
        Assert.Equal(0.42, dials[0].Fraction!.Value, 10);
    }

    [Fact]
    public void NoModelBucketReadsModelAndIsEmpty()
    {
        var dials = DialModel.All(
            Snapshot(new() { ["five_hour"] = (10, 3600), ["seven_day"] = (20, 86_400) }),
            null,
            Now);
        Assert.Equal("MODEL", dials[2].Title);
        Assert.Null(dials[2].Fraction);
    }

    [Fact]
    public void ThirdDialHonoursPinnedBucket()
    {
        var dials = DialModel.All(
            Snapshot(new()
            {
                ["seven_day_fable"] = (30, 86_400),
                ["seven_day_opus"] = (60, 86_400),
            }),
            "seven_day_opus",
            Now);
        Assert.Equal("OPUS", dials[2].Title);
        Assert.Equal(0.6, dials[2].Fraction!.Value, 10);
    }

    [Fact]
    public void MissingSnapshotYieldsEmptyDials()
    {
        var dials = DialModel.All(null, null, Now);
        Assert.Equal(new[] { "SESSION", "WEEK", "MODEL" }, dials.Select(d => d.Title));
        Assert.All(dials, d => Assert.Null(d.Fraction));
    }
}

public class StatusLineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    [Fact]
    public void SilentWhenHealthy()
    {
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket> { ["five_hour"] = new(1, null) });
        Assert.Null(StatusLine.Text(new UsageState.Ok(snapshot, Now), Now));
    }

    [Fact]
    public void StaleSnapshotGetsUpdatedAgoLine()
    {
        var state = new UsageState.Ok(
            new UsageSnapshot(new Dictionary<string, UsageBucket>()), Now.AddMinutes(-16));
        Assert.Equal("updated 16m ago", StatusLine.Text(state, Now));
    }

    [Fact]
    public void ReportsFailures()
    {
        Assert.Equal("unexpected response from the API",
            StatusLine.Text(new UsageState.Failed(UsageError.MalformedResponse), Now));
        Assert.Equal("HTTP 500",
            StatusLine.Text(new UsageState.Failed(UsageError.Network("HTTP 500")), Now));
    }

    [Fact]
    public void DefersToTheBlockingNoticeForCredentialFailures()
    {
        Assert.Null(StatusLine.Text(new UsageState.Failed(UsageError.NoCredentials), Now));
        Assert.Null(StatusLine.Text(new UsageState.Failed(UsageError.Unauthorized), Now));
    }

    [Fact]
    public void ReportsLoading()
    {
        Assert.Equal("loading…", StatusLine.Text(new UsageState.Loading(), Now));
    }

    [Fact]
    public void RateLimitWithDeadlineCountsDown() =>
        Assert.Equal("rate limited · retry in 10m",
            StatusLine.Text(new UsageState.Failed(UsageError.RateLimited(600)), Now, Now.AddSeconds(600)));

    [Fact]
    public void ReportsRateLimitDeadline()
    {
        var until = Now.AddMinutes(22);
        Assert.Equal("rate limited · retry in 22m",
            StatusLine.Text(new UsageState.Failed(UsageError.RateLimited(1320)), Now, until));
    }

    [Fact]
    public void ReportsRateLimitWithoutDeadline() =>
        Assert.Equal("rate limited",
            StatusLine.Text(new UsageState.Failed(UsageError.RateLimited(null)), Now));

    [Fact]
    public void DropsExpiredRateLimitCountdown() =>
        Assert.Equal("rate limited",
            StatusLine.Text(new UsageState.Failed(UsageError.RateLimited(60)), Now, Now.AddSeconds(-1)));
}
