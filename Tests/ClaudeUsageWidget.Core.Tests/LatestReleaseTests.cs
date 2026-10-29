namespace ClaudeUsageWidget.Core.Tests;

public class LatestReleaseTests
{
    private const string Page = "https://github.com/Reva1v/claude-usage-widget/releases/tag/v0.2.9";

    [Fact]
    public void PicksTheWinX64Zip()
    {
        var json = $$"""
            {
              "tag_name": "v0.2.9",
              "html_url": "{{Page}}",
              "assets": [
                { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt" },
                { "name": "ClaudeUsageWidget-v0.2.9-win-x64.zip", "browser_download_url": "https://example.com/w.zip" }
              ]
            }
            """;

        var release = LatestRelease.Parse(json);

        Assert.NotNull(release);
        Assert.Equal("0.2.9", release.Version);
        Assert.Equal(new Uri("https://example.com/w.zip"), release.AssetUrl);
        Assert.Equal(new Uri(Page), release.PageUrl);
    }

    [Fact]
    public void ReleaseWithoutZipStillParses()
    {
        var release = LatestRelease.Parse($$"""{ "tag_name": "v1.0.0", "html_url": "{{Page}}", "assets": [] }""");

        Assert.NotNull(release);
        Assert.Null(release.AssetUrl);
    }

    [Theory]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("""{ "tag_name": "", "html_url": "https://x.y" }""")]
    [InlineData("""{ "tag_name": "v1.0.0" }""")]
    [InlineData("[]")]
    public void NotAReleaseIsNull(string json) => Assert.Null(LatestRelease.Parse(json));

    [Theory]
    [InlineData("0.2.9", "0.2.8", true)]
    [InlineData("v0.3.0", "0.2.10", true)]
    [InlineData("0.2.10", "0.2.9", true)]
    [InlineData("0.2.8", "0.2.8", false)]
    [InlineData("0.2.7", "0.2.8", false)]
    [InlineData("0.2.9", "unknown", false)]
    [InlineData("garbage", "0.2.8", false)]
    public void ComparesVersionsNumerically(string latest, string current, bool expected) =>
        Assert.Equal(expected, LatestRelease.IsNewer(latest, current));
}
