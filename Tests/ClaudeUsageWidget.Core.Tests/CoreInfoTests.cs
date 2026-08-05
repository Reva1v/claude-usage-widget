namespace ClaudeUsageWidget.Core.Tests;

public class CoreInfoTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abcdef1234", "1.2.3")]
    public void StripsBuildMetadataSuffix(string raw, string expected) =>
        Assert.Equal(expected, CoreInfo.ParseVersion(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+abcdef")]
    public void EmptyOrMissingIsUnknown(string? raw) =>
        Assert.Equal("unknown", CoreInfo.ParseVersion(raw));

    [Fact]
    public void PublicVersionIsNeverEmpty() =>
        Assert.False(string.IsNullOrEmpty(CoreInfo.Version));
}
