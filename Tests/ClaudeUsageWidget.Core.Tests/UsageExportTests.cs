using System.Text.Json;

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

    [Fact]
    public void ExportsAvailableModelFamiliesInPreferenceOrder()
    {
        // Fable then Sonnet: the order ModelBuckets.Available returns, seven_day_
        // stripped and lower-cased. A window bucket keeps the payload non-null.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day_fable"] = new(15, Now.AddDays(6)),
            ["seven_day_sonnet"] = new(4, Now.AddDays(6)),
        });

        var payload = UsageExport.Payload(snapshot, Now, account: "low");

        Assert.NotNull(payload);
        Assert.Equal(new[] { "fable", "sonnet" }, payload!.AvailableModels);
    }

    [Fact]
    public void NoModelBucketExportsEmptyNotNull()
    {
        // Both windows, no per-model bucket (e.g. a Team account): the field is
        // an empty list, never null, whenever a snapshot exists.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });

        var payload = UsageExport.Payload(snapshot, Now, account: "team");

        Assert.NotNull(payload!.AvailableModels);
        Assert.Empty(payload.AvailableModels!);
    }

    [Fact]
    public void SerialisedJsonUsesCamelCaseKeysIncludingAvailableModels()
    {
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
            ["seven_day_fable"] = new(15, Now.AddDays(6)),
            ["seven_day_sonnet"] = new(4, Now.AddDays(6)),
        });

        var json = JsonSerializer.Serialize(
            UsageExport.Payload(snapshot, Now, account: "low", preferredBucket: null, plan: "Max 20x")!, UsageExport.JsonOptions);

        // The new key carries the actual families, not null — this is what the
        // launcher reads, and what the null mutation of Payload breaks.
        Assert.Contains("\"availableModels\":[\"fable\",\"sonnet\"]", json);
        // Every v1 field name (camelCase) still present.
        foreach (var name in new[] { "\"fiveHour\"", "\"sevenDay\"", "\"atMs\"", "\"fiveResetAt\"", "\"sevenResetAt\"" })
            Assert.Contains(name, json);
        // Every v2 field name still present.
        foreach (var name in new[] { "\"account\"", "\"modelKey\"", "\"modelLabel\"", "\"modelSevenDay\"", "\"modelResetAt\"" })
            Assert.Contains(name, json);
        // v3's second field, the one the launcher's plan filter reads.
        Assert.Contains("\"plan\":\"Max 20x\"", json);
    }

    [Fact]
    public void JsonRoundTripKeepsV1NamesAndUnits()
    {
        // Extends the v1 name/unit contract through the serializer: atMs stays
        // milliseconds, the reset stamps stay seconds, after a full round-trip.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });
        var original = UsageExport.Payload(
            snapshot, Now, account: "work", preferredBucket: null, plan: "Team")!;

        var json = JsonSerializer.Serialize(original, UsageExport.JsonOptions);
        var back = JsonSerializer.Deserialize<UsageExportPayload>(json, UsageExport.JsonOptions)!;

        Assert.Equal(62, back.FiveHour);
        Assert.Equal(30, back.SevenDay);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), back.AtMs);
        Assert.Equal(Now.AddHours(1).ToUnixTimeSeconds(), back.FiveResetAt);
        Assert.Equal(Now.AddDays(3).ToUnixTimeSeconds(), back.SevenResetAt);
        // v2 and both v3 fields survive the same round-trip.
        Assert.Equal("work", back.Account);
        Assert.NotNull(back.AvailableModels);
        Assert.Equal("Team", back.Plan);
    }

    [Fact]
    public void ThePlanKeyIsPresentInTheExportedJson()
    {
        // v3 (plan): the launcher reads `plan` off the widget record. The key
        // must exist even when the account has no plan yet — see the null pin
        // below for why it is written as null rather than omitted.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });

        var json = JsonSerializer.Serialize(UsageExport.Payload(snapshot, Now, account: "work")!, UsageExport.JsonOptions);

        Assert.Contains("\"plan\":", json);
    }

    [Fact]
    public void ExportsThePlanLabelItIsGiven()
    {
        // The label itself is SubscriptionTier's job; Payload's job is to put it
        // in the record under the key the launcher reads.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });

        var payload = UsageExport.Payload(snapshot, Now, account: "work", preferredBucket: null, plan: "Team");

        Assert.Equal("Team", payload!.Plan);
        Assert.Contains("\"plan\":\"Team\"", JsonSerializer.Serialize(payload, UsageExport.JsonOptions));
    }

    [Theory]
    // The two cases the launcher's filter turns on, end to end: the raw fields
    // as `/api/organizations` carries them, through SubscriptionTier.Label,
    // into the exported record. SubscriptionTierTests owns the label rules —
    // this pins that the export carries THAT label and not a second opinion.
    [InlineData("raven", "default_raven", "team", "Team")]
    [InlineData("claude_max", "default_claude_max_20x", null, "Max 20x")]
    public void ThePlanTravelsFromTheRawSubscriptionFields(
        string capability, string tier, string? ravenType, string expected)
    {
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });
        var label = SubscriptionTier.Label([capability, "chat"], tier, ravenType);

        var payload = UsageExport.Payload(snapshot, Now, account: "work", preferredBucket: null, plan: label);

        Assert.Equal(expected, payload!.Plan);
        Assert.Contains($"\"plan\":\"{expected}\"", JsonSerializer.Serialize(payload, UsageExport.JsonOptions));
    }

    [Fact]
    public void NoPlanIsWrittenAsNullNotOmitted()
    {
        // The writer sets no ignore condition, so the key is always there and an
        // account whose subscription fields were never fetched exports
        // `"plan":null`. The launcher treats null — and an absent key from a
        // pre-v3 writer — as "plan unknown" and hides nothing.
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(62, Now.AddHours(1)),
            ["seven_day"] = new(30, Now.AddDays(3)),
        });

        var payload = UsageExport.Payload(snapshot, Now, account: "work", preferredBucket: null, plan: null);

        Assert.Null(payload!.Plan);
        Assert.Contains("\"plan\":null", JsonSerializer.Serialize(payload, UsageExport.JsonOptions));
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
