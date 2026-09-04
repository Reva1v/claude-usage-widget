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
}
