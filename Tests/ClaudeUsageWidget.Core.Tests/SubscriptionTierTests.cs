namespace ClaudeUsageWidget.Core.Tests;

/// The plan label under the account name, from the three RAW fields
/// `/api/organizations` carries.
///
/// The three live cases below are transcribed from `widget.log` on 2026-09-04
/// (the `organization` lines the picker logs), one per account on this machine.
/// Everything else — Pro, Free, Enterprise, a raven tier with a tail — comes
/// from research, not from a body anyone here has seen: helixml/helix
/// `subscription_profile.go` and lugia19/Claude-Usage-Extension `claude-api.js`
/// for the tier strings, claude.com/pricing for the plan names. Those tests are
/// marked as such where it matters.
public class SubscriptionTierTests
{
    [Fact]
    public void PersonalIsMaxTwentyTimes() =>
        // widget.log 19:18:04 — capabilities=["claude_max","chat"]
        Assert.Equal("Max 20x", SubscriptionTier.Label(
            ["claude_max", "chat"], "default_claude_max_20x", null));

    [Fact]
    public void WorkIsTeam() =>
        // widget.log 19:19:45 — capabilities=["raven","chat"], raven_type="team"
        Assert.Equal("Team", SubscriptionTier.Label(
            ["raven", "chat"], "default_raven", "team"));

    [Fact]
    public void LowIsMaxFiveTimes() =>
        // widget.log 19:21:25 — capabilities=["chat","claude_max"], chat FIRST.
        // The order differs from the personal account's, which is why the
        // capability is matched by membership and never by index.
        //
        // That body also carries subscription_pause={"status":"scheduled",...},
        // which is deliberately not a parameter here at all: the label says what
        // the plan IS, and a pause was not asked for.
        Assert.Equal("Max 5x", SubscriptionTier.Label(
            ["chat", "claude_max"], "default_claude_max_5x", null));

    [Fact]
    public void ProComesFromTheCapabilityBecauseTheTierCannotSayIt() =>
        // RESEARCH ONLY — no Pro account exists on this machine. `default_claude_ai`
        // is the tier of BOTH free and pro, so the `claude_pro` capability is the
        // only thing that separates them.
        Assert.Equal("Pro", SubscriptionTier.Label(
            ["claude_pro", "chat"], "default_claude_ai", null));

    [Fact]
    public void TheFreeTierWithNoPaidCapabilityIsFree() =>
        // RESEARCH ONLY. Same tier as Pro above, without the capability. Without
        // this rule the fallback would prettify the string into "Claude ai",
        // which is not the name of any plan.
        Assert.Equal("Free", SubscriptionTier.Label(["chat"], "default_claude_ai", null));

    [Fact]
    public void ATeamOrganizationIsATeamEvenWithoutTheRavenType() =>
        // Either signal is enough: the capability and the field say the same
        // thing, and a body carrying only one of them still reads as Team.
        Assert.Equal("Team", SubscriptionTier.Label(["raven", "chat"], "default_raven", null));

    [Fact]
    public void TheRavenTypeAloneIsEnough() =>
        Assert.Equal("Team", SubscriptionTier.Label(["chat"], null, "team"));

    [Fact]
    public void ARavenTierThatSaysMoreThanRavenKeepsItsTail() =>
        // HYPOTHETICAL: claude.com/pricing sells Team as Standard and Premium
        // seats, and this machine only has the plain one. If a seat ever reaches
        // the tier string, the label says so rather than flattening it to "Team".
        Assert.Equal("Team premium", SubscriptionTier.Label(
            ["raven", "chat"], "default_raven_premium", "team"));

    [Fact]
    public void MaxWithNoSuffixInTheTierIsBareMax() =>
        // The capability says Max, the tier does not say which — better a plan
        // name with no multiplier than a multiplier that was invented.
        Assert.Equal("Max", SubscriptionTier.Label(["claude_max", "chat"], "default_claude_max", null));

    [Fact]
    public void MaxSurvivesATierThatNamesAnotherFamily() =>
        Assert.Equal("Max", SubscriptionTier.Label(["claude_max", "chat"], "something_else", null));

    [Fact]
    public void EnterpriseIsReadFromEitherTheCapabilityOrTheTier()
    {
        // RESEARCH ONLY — claude.com/pricing lists Enterprise; no sample here.
        Assert.Equal("Enterprise", SubscriptionTier.Label(["enterprise", "chat"], null, null));
        Assert.Equal("Enterprise", SubscriptionTier.Label(["chat"], "default_enterprise", null));
    }

    [Fact]
    public void AnUnknownTierIsPrettifiedRatherThanDropped() =>
        // A plan that does not exist yet must show SOMETHING honest: the raw
        // string, made readable. Silence would read as a bug in the widget.
        Assert.Equal("Claude ultra", SubscriptionTier.Label(["chat"], "default_claude_ultra", null));

    [Fact]
    public void AnUnknownTierWithoutTheDefaultPrefixIsStillPrettified() =>
        Assert.Equal("Some new plan", SubscriptionTier.Label([], "some_new_plan", null));

    [Fact]
    public void NothingAtAllIsFree()
    {
        // The bottom of the chain: an organization that claims no paid
        // capability and no tier is a free one.
        Assert.Equal("Free", SubscriptionTier.Label([], null, null));
        Assert.Equal("Free", SubscriptionTier.Label(null, "", ""));
    }

    [Fact]
    public void LabelOrNullIsNullWhileNothingWasFetched() =>
        // "We have not asked yet" is not a plan. Label answers Free here — the
        // right answer to a different question — so the sentinel is its own
        // method, read by the panel's plan line and by the usage export alike.
        Assert.Null(SubscriptionTier.LabelOrNull(null, null, null));

    [Fact]
    public void LabelOrNullNeedsAllThreeAbsentToSaySoFar()
    {
        // One field present is an answer, and the guard must be AND, not OR: a
        // body carrying only raven_type still says Team, and an OR guard would
        // throw that away as "never asked".
        Assert.Equal("Team", SubscriptionTier.LabelOrNull(null, null, "team"));
        Assert.Equal("Free", SubscriptionTier.LabelOrNull([], null, null));
    }

    [Theory]
    // Past the sentinel, LabelOrNull is Label — the same two cases the export
    // and the panel show for the accounts on this machine.
    [InlineData("raven", "default_raven", "team", "Team")]
    [InlineData("claude_max", "default_claude_max_20x", null, "Max 20x")]
    public void LabelOrNullIsLabelOnceAnyFieldIsThere(
        string capability, string tier, string? ravenType, string expected) =>
        Assert.Equal(expected, SubscriptionTier.LabelOrNull([capability, "chat"], tier, ravenType));

    [Fact]
    public void LabelForAnAbsentProfileIsNull() =>
        // The export runs off freshly loaded settings, where the account it is
        // exporting may no longer be: no profile, no fields, no plan.
        Assert.Null(SubscriptionTier.LabelFor(null));

    [Fact]
    public void LabelForReadsTheThreeFieldsOffTheProfile() =>
        Assert.Equal("Team", SubscriptionTier.LabelFor(new AccountProfile(
            "a1", "work", "org-1", null, 0,
            Capabilities: ["raven", "chat"], RateLimitTier: "default_raven", RavenType: "team")));

    [Fact]
    public void LabelForAnswersPerProfileAndNeverOffTheWrongOne()
    {
        // Two accounts, two plans. The export picks the profile by id and this
        // is the other half of that: the label must come from the profile it was
        // handed, so exporting one account's plan onto another is a change a
        // test can see.
        var team = new AccountProfile(
            "a1", "work", "org-1", null, 0,
            Capabilities: ["raven", "chat"], RateLimitTier: "default_raven", RavenType: "team");
        var max = new AccountProfile(
            "a2", "personal", "org-2", null, 0,
            Capabilities: ["claude_max", "chat"], RateLimitTier: "default_claude_max_20x");

        Assert.Equal("Team", SubscriptionTier.LabelFor(team));
        Assert.Equal("Max 20x", SubscriptionTier.LabelFor(max));
    }

    [Fact]
    public void LabelForKeepsTheNeverAskedSentinel() =>
        Assert.Null(SubscriptionTier.LabelFor(new AccountProfile("a1", "work", "org-1", null, 0)));
}
