namespace ClaudeUsageWidget.Core.Tests;

public class DialGeometryTests
{
    [Fact]
    public void ZeroFractionPointsAtTwelve() => Assert.Equal(-90, DialGeometry.AngleDegrees(0));

    [Fact]
    public void QuarterFractionPointsAtThree() => Assert.Equal(0, DialGeometry.AngleDegrees(0.25));

    [Fact]
    public void FullFractionWrapsAround() => Assert.Equal(270, DialGeometry.AngleDegrees(1));
}
