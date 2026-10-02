namespace ClaudeUsageWidget.Core.Tests;

public class PanelOpacityTests
{
    [Theory]
    [InlineData(null, PanelOpacities.Default)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(-0.2, 0.0)]
    [InlineData(1.7, 1.0)]
    [InlineData(double.NaN, PanelOpacities.Default)]
    public void ResolveDefaultsNullAndClampsToUnit(double? saved, double expected) =>
        Assert.Equal(expected, PanelOpacities.Resolve(saved));

    [Fact]
    public void PresetsAreDescendingAndContainTheDefault()
    {
        Assert.Contains(PanelOpacities.Default, PanelOpacities.Presets);
        Assert.Equal(PanelOpacities.Presets.OrderByDescending(p => p), PanelOpacities.Presets);
        Assert.All(PanelOpacities.Presets, p => Assert.InRange(p, 0, 1));
    }

    [Fact]
    public void PanelOpacityRoundTripsThroughSettingsAndReadsAsNullFromAnOldFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cuw-opacity-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.Null(store.Load().PanelOpacity);

            store.Save(new WidgetSettingsData { PanelOpacity = 0.4 });
            Assert.Equal(0.4, store.Load().PanelOpacity);
            Assert.Contains("\"PanelOpacity\": 0.4", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
