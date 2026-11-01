namespace ClaudeUsageWidget.Core.Tests;

public class TrayTextTests
{
    private static DialModel Model(string title, double? fraction) => new(title, fraction, null);

    [Fact]
    public void BuildsMetrics()
    {
        var metrics = TrayText.Metrics(new[]
        {
            Model("5H", 0.57),
            Model("7D", 0.38),
            Model("FABLE", 0.08),
        });
        Assert.Equal(new[]
        {
            new TrayMetric("5H", "57%"),
            new TrayMetric("7D", "38%"),
            new TrayMetric("FAB", "8%"),
        }, metrics);
    }

    [Fact]
    public void RoundsRatherThanTruncates()
    {
        var metrics = TrayText.Metrics(new[] { Model("5H", 0.575) });
        Assert.Equal("58%", metrics[0].Value);
    }

    [Fact]
    public void MissingFractionShowsDash()
    {
        var metrics = TrayText.Metrics(new[] { Model("MODEL", null) });
        Assert.Equal(new[] { new TrayMetric("MOD", "—") }, metrics);
    }

    [Fact]
    public void NoLimitShowsInfinityRatherThanADash()
    {
        // A dash means "no data"; an account without a five-hour window has
        // its answer, and it is "unlimited".
        var metrics = TrayText.Metrics(new[] { new DialModel("5H", null, "no limit", NoLimit: true) });
        Assert.Equal(new[] { new TrayMetric("5H", "∞") }, metrics);
    }

    [Theory]
    // The icon is one number. "∞" is not one, so a 5H choice on an account
    // without that window shows the week instead; any other choice stays.
    [InlineData(0, true, 1)]
    [InlineData(0, false, 0)]
    [InlineData(1, true, 1)]
    [InlineData(2, true, 2)]
    public void TheIconFallsBackToTheWeekWhenTheChosenDialHasNoLimit(int chosen, bool noLimit, int expected)
    {
        var dials = new[]
        {
            new DialModel("5H", noLimit ? null : 0.4, noLimit ? "no limit" : "1h 0m", NoLimit: noLimit),
            Model("7D", 0.77),
            Model("FABLE", 0.01),
        };
        Assert.Equal(expected, TrayText.IconIndex(chosen, dials));
    }

    [Fact]
    public void ShortTitlesSurviveIntact()
    {
        Assert.Equal("OPUS", TrayText.Metrics(new[] { Model("OPUS", 0.1) })[0].Label);
        Assert.Equal("W", TrayText.Metrics(new[] { Model("W", 0.1) })[0].Label);
    }

    [Fact]
    public void LongerTitlesAreCutToThree()
    {
        Assert.Equal("SON", TrayText.Metrics(new[] { Model("SONNET", 0.1) })[0].Label);
        Assert.Equal("FIVE", TrayText.Metrics(new[] { Model("FIVE", 0.1) })[0].Label);
        Assert.Equal("FIV", TrayText.Metrics(new[] { Model("FIVER", 0.1) })[0].Label);
    }

    [Fact]
    public void EmptyTitleYieldsNoMetric()
    {
        Assert.Empty(TrayText.Metrics(new[] { Model("", 0.5) }));
    }

    [Fact]
    public void NoModelsYieldsNoMetrics()
    {
        Assert.Empty(TrayText.Metrics(Array.Empty<DialModel>()));
    }

    [Fact]
    public void LabelForKeepsFourCharacterTitleWhole() =>
        Assert.Equal("FIVE", TrayText.LabelFor("FIVE"));

    [Fact]
    public void LabelForCutsLongerTitleToThree() =>
        Assert.Equal("SON", TrayText.LabelFor("SONNET"));
}
