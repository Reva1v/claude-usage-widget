namespace ClaudeUsageWidget.Core.Tests;

public class UsageMathTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    [Theory]
    [InlineData(59, "59s")]
    [InlineData(600, "10m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(90_000, "1d 1h")]
    public void RemainingTextFormatsByMagnitude(int seconds, string expected) =>
        Assert.Equal(expected, UsageMath.RemainingText(Now.AddSeconds(seconds), Now));

    [Fact]
    public void PastOrMissingResetHasNoText()
    {
        Assert.Null(UsageMath.RemainingText(Now.AddSeconds(-1), Now));
        Assert.Null(UsageMath.RemainingText(null, Now));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(42, 0.42)] [InlineData(100, 1)]
    [InlineData(140, 1)] [InlineData(-5, 0)]
    public void FractionScalesAndClamps(double utilization, double expected) =>
        Assert.Equal(expected, UsageMath.Fraction(utilization), 10);

    [Theory]
    [InlineData(0.575, "58%")]   // 0.575*100 = 57.4999... в double — эпсилон обязателен
    [InlineData(0.574, "57%")]
    [InlineData(0, "0%")] [InlineData(1, "100%")]
    public void PercentTextRoundsAtBoundary(double fraction, string expected) =>
        Assert.Equal(expected, UsageMath.PercentText(fraction));
}
