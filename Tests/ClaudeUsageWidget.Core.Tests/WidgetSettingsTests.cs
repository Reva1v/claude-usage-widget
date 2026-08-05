namespace ClaudeUsageWidget.Core.Tests;

public class WidgetSettingsTests
{
    [Theory]
    [InlineData(100, 150)]
    [InlineData(170, 170)]
    [InlineData(500, 340)]
    public void ClampsSide(double raw, double expected) =>
        Assert.Equal(expected, WidgetSettings.ClampSide(raw));

    [Fact]
    public void MissingFileLoadsDefaults()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json"));
        var data = store.Load();
        Assert.True(data.WidgetVisible);
        Assert.Equal(170, data.WidgetSide);
        Assert.Equal("five_hour", data.TrayMetricKey);
    }

    [Fact]
    public void RoundTripsAllFields()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        var store = new SettingsStore(path);
        var data = new WidgetSettingsData
        {
            WidgetVisible = false, PositionLocked = true, ModelBucket = "seven_day_fable",
            WidgetSide = 200, WidgetX = 10, WidgetY = 20, TaskbarBandEnabled = true,
            OrganizationId = "org-1", ConsecutiveRateLimits = 2,
            RetryPausedUntil = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000),
            Accounts = [new AccountProfile("default", "Main", "profiles/default")],
        };
        store.Save(data);
        Assert.Equal(data, new SettingsStore(path).Load() with { Accounts = data.Accounts });
        Assert.Equal("default", new SettingsStore(path).Load().Accounts[0].Id);
    }

    [Fact]
    public void CorruptFileLoadsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json");
        Assert.True(new SettingsStore(path).Load().WidgetVisible);
    }
}
