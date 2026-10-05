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

    // A layered WPF window passes the mouse through a pixel whose alpha is 0, so a fully
    // transparent panel stopped showing its hover toolbar: the painted alpha never drops
    // below one step out of 255, while the setting itself stays 0.
    [Theory]
    [InlineData(0.0, 1.0 / 255)]
    [InlineData(0.001, 1.0 / 255)]
    [InlineData(0.2, 0.2)]
    [InlineData(1.0, 1.0)]
    public void PaintedKeepsTheBackgroundHitTestable(double opacity, double expected)
    {
        Assert.Equal(expected, PanelOpacities.Painted(opacity), 10);
        Assert.True(Math.Round(PanelOpacities.Painted(opacity) * 255) >= 1, "the painted alpha byte must not be 0");
    }

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
