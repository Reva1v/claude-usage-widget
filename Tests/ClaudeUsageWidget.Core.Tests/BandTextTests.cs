namespace ClaudeUsageWidget.Core.Tests;

public class BandTextTests
{
    private static AccountRow Row(string name, double? fraction, string? resetsIn) =>
        new("id-" + name, name,
            [
                new DialModel("5H", fraction, resetsIn),
                new DialModel("7D", 0.3, null),
                new DialModel("OPUS", 0.1, null),
            ],
            resetsIn);

    [Fact]
    public void ShowsFiveHourAndTheResetTimeForEveryAccount()
    {
        var entries = BandText.Entries([Row("work", 0.62, "1h 12m"), Row("personal", 0.18, "2h 4m")]);

        Assert.Equal(2, entries.Count);
        Assert.Equal("work", entries[0].Name);
        Assert.Equal("62%", entries[0].Percent);
        Assert.Equal("1h 12m", entries[0].ResetsIn);
    }

    [Fact]
    public void ShortensALongAccountNameSoTheBandStaysNarrow()
    {
        Assert.Equal("personal", Assert.Single(BandText.Entries([Row("personal-low", 0.18, null)])).Name);
    }

    [Fact]
    public void KeepsAnEightCharacterNameWhole()
    {
        // "default" is what the migrated account is called, and cutting it to
        // "defa" is what this budget existed to prevent — caught on screen.
        Assert.Equal("default", Assert.Single(BandText.Entries([Row("default", 0.02, "47m")])).Name);
    }

    [Fact]
    public void AnAccountWithNoDataReadsAsADashRatherThanZero()
    {
        // A zero percent and "no answer yet" mean opposite things; showing 0%
        // for an account the widget could not reach is a lie.
        Assert.Equal("—", Assert.Single(BandText.Entries([Row("work", null, null)])).Percent);
    }

    [Fact]
    public void TheNameThePercentAndTheTimeStaySeparateFields()
    {
        // The band draws the name on its own line and the percentage with the
        // time under it, so flattening them into one string here would put the
        // decision in the wrong layer — and it once put the name beside a
        // number it does not belong to.
        var entry = Assert.Single(BandText.Entries([Row("work", 0.62, "1h 12m")]));

        Assert.Equal("work", entry.Name);
        Assert.Equal("62%", entry.Percent);
        Assert.Equal("1h 12m", entry.ResetsIn);
    }

    [Fact]
    public void NoResetTimeIsNullRatherThanAnEmptyString()
    {
        Assert.Null(Assert.Single(BandText.Entries([Row("work", 0.62, null)])).ResetsIn);
    }

    [Fact]
    public void NoAccountsIsNoEntriesNotACrash()
    {
        Assert.Empty(BandText.Entries([]));
    }
}
