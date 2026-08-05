namespace ClaudeUsageWidget.Core.Tests;

public class ModelBucketsTests
{
    private static UsageSnapshot Snapshot(IEnumerable<string> keys) =>
        new(keys.ToDictionary(key => key, _ => new UsageBucket(1, null)));

    [Fact]
    public void FiltersToModelBuckets()
    {
        var keys = ModelBuckets.Available(Snapshot(new[]
        {
            "five_hour", "seven_day", "seven_day_opus", "seven_day_sonnet",
            "seven_day_oauth_apps", "seven_day_overage_included", "extra_usage",
        }));
        Assert.Equal(new[] { "seven_day_opus", "seven_day_sonnet" }, keys);
    }

    [Fact]
    public void OrdersKnownModelsFableOpusSonnet()
    {
        var keys = ModelBuckets.Available(Snapshot(new[]
        {
            "seven_day_sonnet", "seven_day_fable", "seven_day_opus",
        }));
        Assert.Equal(new[] { "seven_day_fable", "seven_day_opus", "seven_day_sonnet" }, keys);
    }

    [Fact]
    public void AppendsUnknownModelsAfterKnownAlphabetically()
    {
        var keys = ModelBuckets.Available(Snapshot(new[]
        {
            "seven_day_zebra", "seven_day_opus", "seven_day_aardvark",
        }));
        Assert.Equal(new[] { "seven_day_opus", "seven_day_aardvark", "seven_day_zebra" }, keys);
    }

    [Fact]
    public void AvailableOrdersKnownFirstThenAlphabetical()
    {
        var s = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["seven_day_zeta"] = new(1, null),
            ["seven_day_opus"] = new(1, null),
            ["seven_day_fable"] = new(1, null),
            ["seven_day_oauth_apps"] = new(1, null),
            ["five_hour"] = new(1, null),
        });
        Assert.Equal(new[] { "seven_day_fable", "seven_day_opus", "seven_day_zeta" },
            ModelBuckets.Available(s));
    }

    [Fact]
    public void DefaultsToFableWhenItExists()
    {
        var key = ModelBuckets.Resolve(null, Snapshot(new[]
        {
            "seven_day_opus", "seven_day_fable",
        }));
        Assert.Equal("seven_day_fable", key);
    }

    [Fact]
    public void FallsBackToFirstAvailableWhenFableAbsent()
    {
        var key = ModelBuckets.Resolve(null, Snapshot(new[]
        {
            "seven_day_sonnet", "seven_day_opus",
        }));
        Assert.Equal("seven_day_opus", key);
    }

    [Fact]
    public void HonoursPreferenceWhileStillPresent()
    {
        var key = ModelBuckets.Resolve("seven_day_sonnet", Snapshot(new[]
        {
            "seven_day_fable", "seven_day_sonnet",
        }));
        Assert.Equal("seven_day_sonnet", key);
    }

    [Fact]
    public void DropsStalePreference()
    {
        var key = ModelBuckets.Resolve("seven_day_haiku", Snapshot(new[]
        {
            "seven_day_opus",
        }));
        Assert.Equal("seven_day_opus", key);
    }

    [Fact]
    public void ResolveHonoursPreferredWhileServerReturnsIt()
    {
        var s = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["seven_day_fable"] = new(1, null), ["seven_day_opus"] = new(1, null),
        });
        Assert.Equal("seven_day_opus", ModelBuckets.Resolve("seven_day_opus", s));
        Assert.Equal("seven_day_fable", ModelBuckets.Resolve("seven_day_gone", s));
        Assert.Equal("seven_day_fable", ModelBuckets.Resolve(null, s));
    }

    [Fact]
    public void ResolvesToNullWhenNoModelBucketAtAll()
    {
        Assert.Null(ModelBuckets.Resolve(null, Snapshot(new[] { "five_hour", "seven_day" })));
    }

    [Fact]
    public void LabelsStripThePrefixAndUpcase()
    {
        Assert.Equal("FABLE", ModelBuckets.Label("seven_day_fable"));
        Assert.Equal("OPUS", ModelBuckets.Label("seven_day_opus"));
    }

    [Theory]
    [InlineData("seven_day_fable", true)]
    [InlineData("seven_day_opus", true)]
    [InlineData("seven_day_oauth_apps", false)]
    [InlineData("seven_day_overage_included", false)]
    [InlineData("five_hour", false)]
    [InlineData("seven_day", false)]
    public void IsModelKeyMatchesPrefixExcludingSpecialKeys(string key, bool expected) =>
        Assert.Equal(expected, ModelBuckets.IsModelKey(key));
}
