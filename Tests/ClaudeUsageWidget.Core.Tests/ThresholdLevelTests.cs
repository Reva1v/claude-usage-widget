namespace ClaudeUsageWidget.Core.Tests;

public class ThresholdLevelTests
{
    [Theory]
    [InlineData(0, ThresholdLevel.Ok)] [InlineData(0.59, ThresholdLevel.Ok)]
    [InlineData(0.6, ThresholdLevel.Warning)] [InlineData(0.84, ThresholdLevel.Warning)]
    [InlineData(0.85, ThresholdLevel.Danger)] [InlineData(1, ThresholdLevel.Danger)]
    public void LevelsMatchThresholds(double fraction, ThresholdLevel expected) =>
        Assert.Equal(expected, Thresholds.Level(fraction));
}
