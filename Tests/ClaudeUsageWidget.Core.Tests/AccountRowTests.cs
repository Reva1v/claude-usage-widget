namespace ClaudeUsageWidget.Core.Tests;

public class AccountRowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    private static UsageSnapshot Snapshot(double fiveHour, int resetInSeconds) =>
        new(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(fiveHour, Now.AddSeconds(resetInSeconds)),
            ["seven_day"] = new(30, Now.AddDays(3)),
            ["seven_day_opus"] = new(12, Now.AddDays(3)),
        });

    private static AccountProfile Account(string id) => new(id, id, "org-" + id, null, 0);

    [Fact]
    public void BuildsOneRowPerAccountInSettingsOrder()
    {
        var accounts = new[] { Account("work"), Account("personal"), Account("shared"), Account("low") };
        var snapshots = new Dictionary<string, UsageSnapshot?>
        {
            ["work"] = Snapshot(62, 4_320),
            ["personal"] = Snapshot(18, 600),
            ["shared"] = Snapshot(91, 60),
            ["low"] = Snapshot(44, 7_200),
        };

        var rows = AccountRow.ForAll(accounts, snapshots, null, Now);

        Assert.Equal(["work", "personal", "shared", "low"], rows.Select(r => r.AccountId));
        Assert.All(rows, row => Assert.Equal(3, row.Dials.Count));
        Assert.Equal(0.62, rows[0].Dials[0].Fraction);
        Assert.Equal("1h 12m", rows[0].SessionResetsIn);
    }

    [Fact]
    public void TheBandTimeFollowsTheFiveHourDialIntoASpentWeek()
    {
        // The band shows one time per account and it comes from the same place
        // the dial does, or the panel and the taskbar would disagree.
        var spent = new UsageSnapshot(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(30, Now.AddMinutes(20)),
            ["seven_day"] = new(100, Now.AddDays(3)),
            ["seven_day_opus"] = new(12, Now.AddDays(3)),
        });

        var row = Assert.Single(AccountRow.ForAll(
            [Account("work")], new Dictionary<string, UsageSnapshot?> { ["work"] = spent }, null, Now));

        Assert.Equal("3d 0h", row.SessionResetsIn);
        Assert.Equal(row.Dials[0].Remaining, row.SessionResetsIn);
    }

    [Fact]
    public void KeepsARowForAnAccountWithNoSnapshot()
    {
        // The row must stay: dropping it would shift every row below and make
        // the panel jump every time one account fails to refresh.
        var rows = AccountRow.ForAll(
            [Account("work")],
            new Dictionary<string, UsageSnapshot?> { ["work"] = null },
            null,
            Now);

        var row = Assert.Single(rows);
        Assert.Equal(3, row.Dials.Count);
        Assert.All(row.Dials, dial => Assert.Null(dial.Fraction));
        Assert.Null(row.SessionResetsIn);
    }

    [Fact]
    public void KeepsARowForAnAccountMissingFromTheSnapshotMap()
    {
        var rows = AccountRow.ForAll(
            [Account("work")],
            new Dictionary<string, UsageSnapshot?>(),
            null,
            Now);

        Assert.Null(Assert.Single(rows).Dials[0].Fraction);
    }

    [Fact]
    public void OneAccountIsTheOrdinaryCase()
    {
        // Everyone who is not this repository's author has exactly one Claude
        // subscription. A single-account install must be a normal panel, not a
        // degenerate multi-account one.
        var rows = AccountRow.ForAll(
            [Account("mine")],
            new Dictionary<string, UsageSnapshot?> { ["mine"] = Snapshot(41, 3_600) },
            null,
            Now);

        var row = Assert.Single(rows);
        Assert.Equal(0.41, row.Dials[0].Fraction);
        Assert.Equal("1h 0m", row.SessionResetsIn);
    }

    [Fact]
    public void NoAccountsMeansNoRows()
    {
        Assert.Empty(AccountRow.ForAll([], new Dictionary<string, UsageSnapshot?>(), null, Now));
    }

    [Fact]
    public void CarriesThePlanLabelSoTheViewNeverComputesIt()
    {
        // The three live accounts, with the fields exactly as `/api/organizations`
        // returned them on 2026-09-04 (widget.log). The row is what the panel
        // draws under the name; the view has no business mapping tier strings.
        var accounts = new[]
        {
            new AccountProfile("p", "personal", "org-p", null, 0,
                Capabilities: ["claude_max", "chat"], RateLimitTier: "default_claude_max_20x"),
            new AccountProfile("w", "work", "org-w", null, 0,
                Capabilities: ["raven", "chat"], RateLimitTier: "default_raven", RavenType: "team"),
            new AccountProfile("l", "low", "org-l", null, 0,
                Capabilities: ["chat", "claude_max"], RateLimitTier: "default_claude_max_5x"),
        };

        var rows = AccountRow.ForAll(accounts, new Dictionary<string, UsageSnapshot?>(), null, Now);

        Assert.Equal(["Max 20x", "Team", "Max 5x"], rows.Select(r => r.PlanLabel));
    }

    [Fact]
    public void AnAccountWhoseFieldsWereNeverFetchedHasNoPlanLabel()
    {
        // Null is "we have not asked yet", and it must NOT read as Free: a fresh
        // install would otherwise call a Max subscription free until the first
        // poll lands.
        var rows = AccountRow.ForAll(
            [new AccountProfile("a1", "personal", "org-1", null, 0)],
            new Dictionary<string, UsageSnapshot?>(),
            null,
            Now);

        Assert.Null(Assert.Single(rows).PlanLabel);
    }

    [Fact]
    public void AnOrganizationThatClaimsNothingIsFreeRatherThanUnknown()
    {
        // The other side of the rule above: fields that HAVE been fetched and
        // say nothing are a free account, and the label says so.
        var rows = AccountRow.ForAll(
            [new AccountProfile("a1", "personal", "org-1", null, 0, Capabilities: ["chat"])],
            new Dictionary<string, UsageSnapshot?>(),
            null,
            Now);

        Assert.Equal("Free", Assert.Single(rows).PlanLabel);
    }

    [Fact]
    public void UsesTheDisplayNameNotTheId()
    {
        var rows = AccountRow.ForAll(
            [new AccountProfile("a1", "Work account", null, null, 0)],
            new Dictionary<string, UsageSnapshot?>(),
            null,
            Now);

        Assert.Equal("Work account", Assert.Single(rows).DisplayName);
    }
}
