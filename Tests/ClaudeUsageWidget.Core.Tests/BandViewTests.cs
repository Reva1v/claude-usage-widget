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
}
