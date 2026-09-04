namespace ClaudeUsageWidget.Core.Tests;

public class WidgetSettingsAccountTests
{
    private static readonly WidgetSettingsData Two = new()
    {
        Accounts =
        [
            new AccountProfile("a", "Personal", "org-a", null, 0),
            new AccountProfile("b", "Work", "org-b", null, 1),
        ],
    };

    [Fact]
    public void AccountFindsById()
    {
        Assert.Equal("Work", Two.Account("b")?.DisplayName);
        Assert.Null(Two.Account("missing"));
    }

    [Fact]
    public void WithAccountRewritesOnlyThatAccount()
    {
        var updated = Two.WithAccount("a", a => a with { DisplayName = "Renamed" });

        Assert.Equal("Renamed", updated.Account("a")?.DisplayName);
        Assert.Equal(Two.Account("b"), updated.Account("b"));
        Assert.Equal(2, updated.Accounts.Count);
    }

    [Fact]
    public void WithAccountForUnknownIdChangesNothing()
    {
        var updated = Two.WithAccount("missing", a => a with { DisplayName = "Ghost" });

        Assert.Equal(Two.Accounts, updated.Accounts);
    }
}
