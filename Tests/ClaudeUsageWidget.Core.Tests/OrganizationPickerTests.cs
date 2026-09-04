namespace ClaudeUsageWidget.Core.Tests;

public class OrganizationPickerTests
{
    [Fact]
    public void PrefersTeamOrganizationWithChat() =>
        Assert.Equal("team-uuid", OrganizationPicker.Pick("""
        [
          { "uuid": "personal-uuid", "capabilities": ["chat"], "raven_type": "personal" },
          { "uuid": "team-uuid", "capabilities": ["chat"], "raven_type": "team" },
          { "uuid": "no-chat", "capabilities": ["api"] }
        ]
        """));

    [Fact]
    public void FallsBackToFirstChatCapable() =>
        Assert.Equal("personal-uuid", OrganizationPicker.Pick("""
        [ { "uuid": "personal-uuid", "capabilities": ["chat"] } ]
        """));

    [Fact]
    public void AcceptsIdWhenUuidMissing() =>
        Assert.Equal("42", OrganizationPicker.Pick("""
        [ { "id": "42", "capabilities": ["chat"] } ]
        """));

    [Fact]
    public void DescribeNamesEveryKeyAndQuotesTheTierLikeValues()
    {
        var line = OrganizationPicker.Describe("""
        [
          { "uuid": "other", "capabilities": ["chat"], "rate_limit_tier": "x" },
          { "uuid": "team-uuid", "name": "Acme", "capabilities": ["chat","claude_max"], "raven_type": "team",
            "rate_limit_tier": "default_claude_max_5x", "billing_type": "stripe_subscription" }
        ]
        """, "team-uuid");

        Assert.StartsWith("keys=uuid,name,capabilities,raven_type,rate_limit_tier,billing_type ", line);
        Assert.Contains("rate_limit_tier=\"default_claude_max_5x\"", line);
        Assert.Contains("capabilities=[\"chat\",\"claude_max\"]", line);
        Assert.Contains("raven_type=\"team\"", line);
        Assert.DoesNotContain("Acme", line);
    }

    [Fact]
    public void DescribeSaysSoWhenTheBodyIsNotJson() =>
        Assert.Equal("not-json", OrganizationPicker.Describe("nope", null));

    [Fact]
    public void FieldsReadsTheSubscriptionShapeOfThePickedOrganization()
    {
        // RAW, not a label: the mapping lives in SubscriptionTier and must be
        // fixable without asking claude.ai for the body again.
        var fields = OrganizationPicker.Fields("""
        [
          { "uuid": "other", "capabilities": ["chat"], "rate_limit_tier": "default_claude_ai" },
          { "uuid": "team-uuid", "capabilities": ["raven","chat"], "raven_type": "team",
            "rate_limit_tier": "default_raven" }
        ]
        """, "team-uuid");

        Assert.Equal(["raven", "chat"], fields.Capabilities);
        Assert.Equal("default_raven", fields.RateLimitTier);
        Assert.Equal("team", fields.RavenType);
    }

    [Fact]
    public void FieldsGivesAnEmptyCapabilityListRatherThanNullWhenTheOrganizationHasNone()
    {
        // The list is the sentinel for "these fields have been fetched" — null
        // means never asked, so a found organization must never produce null,
        // however little it says about itself.
        var fields = OrganizationPicker.Fields("""[ { "uuid": "u" } ]""", "u");

        Assert.NotNull(fields.Capabilities);
        Assert.Empty(fields.Capabilities);
        Assert.Null(fields.RateLimitTier);
        Assert.Null(fields.RavenType);
    }

    [Fact]
    public void FieldsAreAllNullWhenThePickedOrganizationIsGone()
    {
        // A saved organization id that is no longer in the body, and a body that
        // is not JSON at all: both are "we do not know", never a fabricated plan.
        Assert.Null(OrganizationPicker.Fields("""[ { "uuid": "u" } ]""", "vanished").Capabilities);
        Assert.Null(OrganizationPicker.Fields("nope", "u").Capabilities);
    }

    [Fact]
    public void FieldsSkipsNonStringCapabilities()
    {
        // A hand-mangled or future body must not crash the poll.
        var fields = OrganizationPicker.Fields("""
        [ { "uuid": "u", "capabilities": ["chat", 7, null], "rate_limit_tier": 3 } ]
        """, "u");

        Assert.Equal(["chat"], fields.Capabilities);
        Assert.Null(fields.RateLimitTier);
    }

    [Fact]
    public void ReturnsNullOnGarbageOrEmpty()
    {
        Assert.Null(OrganizationPicker.Pick("[]"));
        Assert.Null(OrganizationPicker.Pick("not json"));
    }
}
