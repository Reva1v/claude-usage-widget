namespace ClaudeUsageWidget.Core.Tests;

public class SettingsMigrationTests
{
    [Fact]
    public void CarriesLegacySingleAccountIntoAccountsList()
    {
        var legacy = new WidgetSettingsData
        {
            OrganizationId = "org-1",
            RetryPausedUntil = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000),
            ConsecutiveRateLimits = 2,
        };

        var migrated = SettingsMigration.Apply(legacy);

        var account = Assert.Single(migrated.Accounts);
        Assert.Equal(SettingsMigration.LegacyAccountId, account.Id);
        Assert.Equal("org-1", account.OrganizationId);
        Assert.Equal(2, account.ConsecutiveRateLimits);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_348_000), account.RetryPausedUntil);
        Assert.Equal(SettingsMigration.LegacyAccountId, migrated.TrayAccountId);
    }

    [Fact]
    public void ClearsLegacyFieldsSoTheyAreNeverWrittenAgain()
    {
        var migrated = SettingsMigration.Apply(new WidgetSettingsData { OrganizationId = "org-1" });

        Assert.Null(migrated.OrganizationId);
        Assert.Null(migrated.RetryPausedUntil);
        Assert.Equal(0, migrated.ConsecutiveRateLimits);
    }

    [Fact]
    public void MigratesASignedInAccountThatNeverResolvedItsOrganization()
    {
        // A user who signed in but whose first fetch never completed has a
        // cookie on disk and no organization id. Dropping that account would
        // silently sign them out.
        var migrated = SettingsMigration.Apply(new WidgetSettingsData());

        var account = Assert.Single(migrated.Accounts);
        Assert.Equal(SettingsMigration.LegacyAccountId, account.Id);
        Assert.Null(account.OrganizationId);
    }

    [Fact]
    public void CapsTheAccountList()
    {
        // A hand-edited settings file must not be able to push the panel past
        // what it can draw. Extra accounts are dropped from the end, so the
        // ones already on screen keep their rows.
        var many = new WidgetSettingsData
        {
            Accounts = Enumerable.Range(0, 7)
                .Select(i => new AccountProfile($"a{i}", $"A{i}", null, null, 0))
                .ToList(),
        };

        Assert.Equal(AccountLimits.Max, SettingsMigration.Apply(many).Accounts.Count);
        Assert.Equal("a0", SettingsMigration.Apply(many).Accounts[0].Id);
    }

    [Fact]
    public void LeavesAnAlreadyMigratedFileAlone()
    {
        var already = new WidgetSettingsData
        {
            Accounts = [new AccountProfile("a1", "Work", "org-9", null, 0)],
            TrayAccountId = "a1",
        };

        Assert.Same(already, SettingsMigration.Apply(already));
    }
}
