namespace ClaudeUsageWidget.Core.Tests;

/// When the three raw subscription fields are worth fetching again.
///
/// They were written once and never refreshed, so a plan CHANGE never reached
/// the export: an account that leaves Team kept its "Team" label — and the
/// launcher kept hiding Fable for it — until the next sign-in.
public class SubscriptionRefreshTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    [Fact]
    public void TheCadenceIsOneHour() =>
        // Named, not a literal at the call site: the organizations endpoint is
        // cheap, but it is not the one the 429 budget is for.
        Assert.Equal(TimeSpan.FromHours(1), SubscriptionRefresh.Interval);

    [Fact]
    public void NothingFetchedYetRefreshesImmediately() =>
        // Covers both a fresh account and a settings file written before the
        // stamp existed.
        Assert.True(SubscriptionRefresh.ShouldRefresh(null, Now));

    [Fact]
    public void WithinTheHourItWaits() =>
        Assert.False(SubscriptionRefresh.ShouldRefresh(Now.AddMinutes(-59), Now));

    [Fact]
    public void AtTheHourItRefreshes() =>
        // The boundary is INCLUSIVE: an exclusive one turns "hourly" into
        // "whenever the poll clock happens to land past the hour".
        Assert.True(SubscriptionRefresh.ShouldRefresh(Now.AddHours(-1), Now));

    private static readonly OrganizationFields Team =
        new(["raven", "chat"], "default_raven", "team");

    [Fact]
    public void LearnedNothingIsTheUnknownSentinel()
    {
        // OrganizationPicker.Fields answers Unknown for a body that is not the
        // expected JSON array and for one that no longer carries the stored id
        // — a challenge page returned with a 2xx, say, while /usage still works.
        Assert.True(SubscriptionRefresh.LearnedNothing(OrganizationFields.Unknown));
        Assert.False(SubscriptionRefresh.LearnedNothing(Team));
    }

    [Fact]
    public void AnEmptyCapabilityListIsAnAnswerNotSilence() =>
        // The picker's own distinction: null means the organization was not
        // found, empty means it was found and claims nothing. The second is a
        // fact about the account and must overwrite what was there.
        Assert.False(SubscriptionRefresh.LearnedNothing(new OrganizationFields([], null, null)));

    [Fact]
    public void ARefreshThatLearnedNothingKeepsTheFieldsItHad()
    {
        // The regression this exists for: storing Unknown blanked a good plan,
        // so the panel went blank and the export said "plan":null — which the
        // launcher reads as "unknown", un-filtering a Team account for an hour.
        var kept = SubscriptionRefresh.Merge(Team, OrganizationFields.Unknown);

        Assert.Equal(["raven", "chat"], kept.Capabilities);
        Assert.Equal("default_raven", kept.RateLimitTier);
        Assert.Equal("team", kept.RavenType);
    }

    [Fact]
    public void ARefreshThatLearnedSomethingWins() =>
        // The whole point of the cadence: an account that LEFT Team must stop
        // being Team, so a parsed body always replaces what was stored.
        Assert.Equal(
            "default_claude_max_20x",
            SubscriptionRefresh.Merge(Team, new OrganizationFields(["claude_max", "chat"],
                "default_claude_max_20x", null)).RateLimitTier);

    [Fact]
    public void KeepingNothingIsStillNothing() =>
        // A fresh account whose first refresh could not be parsed stays at the
        // "never asked" sentinel rather than inventing a plan.
        Assert.True(SubscriptionRefresh.LearnedNothing(
            SubscriptionRefresh.Merge(OrganizationFields.Unknown, OrganizationFields.Unknown)));

    [Fact]
    public void AStampInTheFutureRefreshes() =>
        // A hand-edited settings file or a clock moved back would otherwise
        // freeze the refresh until the wall clock caught up.
        Assert.True(SubscriptionRefresh.ShouldRefresh(Now.AddMinutes(5), Now));
}
