namespace ClaudeUsageWidget.Core.Tests;

public class PollScheduleTests
{
    [Fact]
    public void FourAccountsAreSpacedEvenlyAcrossTheCycle()
    {
        var offsets = Enumerable.Range(0, 4)
            .Select(i => PollSchedule.OffsetFor(i, 4).TotalSeconds)
            .ToArray();

        Assert.Equal([0d, 75d, 150d, 225d], offsets);
    }

    [Fact]
    public void ASingleAccountPollsImmediately()
    {
        Assert.Equal(TimeSpan.Zero, PollSchedule.OffsetFor(0, 1));
    }

    [Fact]
    public void EveryOffsetIsInsideTheCycle()
    {
        for (var count = 1; count <= 8; count++)
            for (var i = 0; i < count; i++)
                Assert.InRange(
                    PollSchedule.OffsetFor(i, count).TotalSeconds,
                    0,
                    UsageStore.RefreshIntervalSeconds - 1);
    }

    [Fact]
    public void OffsetsAreDistinctSoTheRequestsNeverCoincide()
    {
        var offsets = Enumerable.Range(0, 4).Select(i => PollSchedule.OffsetFor(i, 4)).ToArray();
        Assert.Equal(4, offsets.Distinct().Count());
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(4, 4)]
    [InlineData(0, 0)]
    public void RejectsAnIndexOutsideTheAccountList(int index, int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PollSchedule.OffsetFor(index, count));
}
