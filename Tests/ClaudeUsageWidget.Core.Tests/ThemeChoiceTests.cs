namespace ClaudeUsageWidget.Core.Tests;

public class ThemeChoiceTests
{
    [Theory]
    [InlineData(null, ThemeKind.Dark, ThemeKind.Dark)]
    [InlineData(null, ThemeKind.Light, ThemeKind.Light)]
    [InlineData(ThemeChoice.System, ThemeKind.Light, ThemeKind.Light)]
    [InlineData(ThemeChoice.Dark, ThemeKind.Light, ThemeKind.Dark)]
    [InlineData(ThemeChoice.Light, ThemeKind.Dark, ThemeKind.Light)]
    [InlineData(ThemeChoice.Light, ThemeKind.Light, ThemeKind.Light)]
    public void ResolveFollowsTheSystemOnlyForSystem(ThemeChoice? saved, ThemeKind system, ThemeKind expected) =>
        Assert.Equal(expected, ThemeChoices.Resolve(saved, system));

    [Fact]
    public void ThemeRoundTripsThroughSettingsAndReadsAsNullFromAnOldFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cuw-theme-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.Null(store.Load().Theme);

            store.Save(new WidgetSettingsData { Theme = ThemeChoice.Light });
            Assert.Equal(ThemeChoice.Light, store.Load().Theme);
            Assert.Contains("\"Theme\": \"Light\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
