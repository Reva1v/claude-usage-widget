namespace ClaudeUsageWidget.Core.Tests;

public class BandViewTests
{
    [Theory]
    [InlineData(null, 1, BandView.Metrics)]
    [InlineData(null, 2, BandView.Accounts)]
    [InlineData(BandView.Accounts, 1, BandView.Accounts)]
    [InlineData(BandView.Metrics, 3, BandView.Metrics)]
    public void ResolveFollowsTheSavedValueThenTheAccountCount(BandView? saved, int accounts, BandView expected) =>
        Assert.Equal(expected, BandViews.Resolve(saved, accounts));

    [Fact]
    public void MetricEntriesAreTheTrayColumnsWithoutResetTimes()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_787_748_000);
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(3, now.AddHours(4)),
            ["seven_day"] = new(68, now.AddDays(2)),
            ["seven_day_fable"] = new(58, now.AddDays(3)),
        });
        var account = new AccountProfile("a", "personal", null, null, 0);
        var row = AccountRow.ForAll([account], new Dictionary<string, UsageSnapshot?> { ["a"] = snapshot }, null, now)[0];

        var entries = BandText.MetricEntries(row);

        Assert.Equal(["5H", "7D", "FAB"], entries.Select(e => e.Name));
        Assert.Equal(["3%", "68%", "58%"], entries.Select(e => e.Percent));
        Assert.All(entries, e => Assert.Null(e.ResetsIn));
    }

    [Fact]
    public void AnAccountWithoutAFiveHourWindowShowsNoLimitInBothBandViews()
    {
        // Ultra: the week only. The band reads "∞" with no reset time for the
        // five-hour slot rather than a dash that looks like a failed fetch.
        var now = DateTimeOffset.FromUnixTimeSeconds(1_787_748_000);
        var snapshot = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["seven_day"] = new(77, now.AddDays(2)),
            ["seven_day_fable"] = new(1, now.AddDays(2)),
        });
        var account = new AccountProfile("a", "personal", null, null, 0,
            Capabilities: ["claude_max", "chat"], RateLimitTier: "default_claude_max_20x");
        var row = AccountRow.ForAll([account], new Dictionary<string, UsageSnapshot?> { ["a"] = snapshot }, null, now)[0];

        Assert.Equal(["∞", "77%", "1%"], BandText.MetricEntries(row).Select(e => e.Percent));
        var entry = Assert.Single(BandText.Entries([row]));
        Assert.Equal("∞", entry.Percent);
        Assert.Null(entry.ResetsIn);
    }
}
