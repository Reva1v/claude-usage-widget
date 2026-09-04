namespace ClaudeUsageWidget.Core.Tests;

public class UsageExportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    [Fact]
    public void CarriesBothWindowsWithTheirResetStamps()
    {
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });

        var payload = UsageExport.Payload(snapshot, Now);

        Assert.NotNull(payload);
        Assert.Equal(62, payload!.FiveHour);
        Assert.Equal(30, payload.SevenDay);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), payload.AtMs);
        Assert.Equal(Now.AddHours(1).ToUnixTimeSeconds(), payload.FiveResetAt);
        Assert.Equal(Now.AddDays(3).ToUnixTimeSeconds(), payload.SevenResetAt);
    }

    [Fact]
    public void TheTimestampIsMillisecondsAndTheResetsAreSeconds()
    {
        // The consumer's units, not ours. Getting these the same way round
        // produces a number that is wrong by a factor of 1000 and looks fine.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(1, Now),
        });

        var payload = UsageExport.Payload(snapshot, Now)!;

        Assert.Equal(payload.AtMs, payload.FiveResetAt * 1000);
    }

    [Fact]
    public void NoSnapshotWritesNothing()
    {
        Assert.Null(UsageExport.Payload(null, Now));
    }

    [Fact]
    public void ASnapshotWithNeitherWindowWritesNothing()
    {
        // A per-token account has no session or weekly limit. Zeros here would
        // read downstream as "0% used", which is the opposite of the truth.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["seven_day_opus"] = new(12, Now.AddDays(3)),
        });

        Assert.Null(UsageExport.Payload(snapshot, Now));
    }

    [Fact]
    public void OneWindowIsEnoughAndTheOtherStaysNull()
    {
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, null),
        });

        var payload = UsageExport.Payload(snapshot, Now);

        Assert.NotNull(payload);
        Assert.Equal(62, payload!.FiveHour);
        Assert.Null(payload.SevenDay);
        Assert.Null(payload.FiveResetAt);
    }

    [Fact]
    public void CarriesTheModelBucketTheDialShows()
    {
        // The reader wants the same per-model limit the third dial shows, not
        // "whichever seven_day_* key came first in the response".
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
            ["seven_day_fable"] = new(15, Now.AddDays(6)),
            ["seven_day_opus"] = new(3, Now.AddDays(6)),
        });

        var payload = UsageExport.Payload(snapshot, Now, account: "low", preferredBucket: null);

        Assert.NotNull(payload);
        Assert.Equal("low", payload!.Account);
        Assert.Equal("seven_day_fable", payload.ModelKey);
        Assert.Equal("FABLE", payload.ModelLabel);
        Assert.Equal(15, payload.ModelSevenDay);
        Assert.Equal(Now.AddDays(6).ToUnixTimeSeconds(), payload.ModelResetAt);
    }

    [Fact]
    public void PreferredBucketWins()
    {
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day_fable"] = new(15, Now.AddDays(6)),
            ["seven_day_opus"] = new(3, Now.AddDays(6)),
        });

        var payload = UsageExport.Payload(snapshot, Now, account: "low", preferredBucket: "seven_day_opus");

        Assert.Equal("seven_day_opus", payload!.ModelKey);
        Assert.Equal("OPUS", payload.ModelLabel);
        Assert.Equal(3, payload.ModelSevenDay);
    }

    [Fact]
    public void NoModelBucketLeavesTheFourNull()
    {
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });

        var payload = UsageExport.Payload(snapshot, Now, account: "work", preferredBucket: "seven_day_fable");

        Assert.Equal("work", payload!.Account);
        Assert.Null(payload.ModelKey);
        Assert.Null(payload.ModelLabel);
        Assert.Null(payload.ModelSevenDay);
        Assert.Null(payload.ModelResetAt);
    }

    [Theory]
    [InlineData("personal", "x", "personal")]
    [InlineData(" Work ", "x", "work")]
    [InlineData("work.shared@example", "x", "work-shared-example")]
    [InlineData("--", "id1", "id1")]
    [InlineData("", "id1", "id1")]
    public void FileNameForSlugifiesTheDisplayName(string displayName, string fallbackId, string expected) =>
        Assert.Equal(expected, UsageExport.FileNameFor(displayName, fallbackId));
}
