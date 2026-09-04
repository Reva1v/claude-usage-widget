namespace ClaudeUsageWidget.Core;

/// The plan name shown under the account, from the three RAW fields
/// `/api/organizations` carries for the picked organization.
///
/// No single field says the plan. Measured on this machine 2026-09-04:
/// `capabilities=["claude_max","chat"] rate_limit_tier="default_claude_max_20x"`,
/// `capabilities=["raven","chat"] rate_limit_tier="default_raven" raven_type="team"`,
/// `capabilities=["chat","claude_max"] rate_limit_tier="default_claude_max_5x"`.
/// The capability names the FAMILY (max / pro / team), the tier carries the
/// multiplier, and `raven_type` is the second witness for a team. Note the third
/// line: `chat` comes first there and second above, so capabilities are matched
/// by MEMBERSHIP and never by index.
///
/// Everything the three samples do not cover — Pro, Free, Enterprise — comes
/// from research, not from a body seen here: helixml/helix
/// `subscription_profile.go` and lugia19/Claude-Usage-Extension `claude-api.js`
/// list the tier strings, claude.com/pricing the plan names.
///
/// The fields are stored raw on <see cref="AccountProfile"/> precisely because
/// of that: a wrong guess here is fixed by editing this file, with no second
/// round trip to claude.ai for an answer already on disk.
public static class SubscriptionTier
{
    /// Never empty and never a lie: an unknown tier is shown prettified rather
    /// than dropped, because silence on the panel reads as a broken widget.
    /// "We have not asked yet" is a different question and is not answered here
    /// — <see cref="AccountRow.ForAll"/> keeps that one, from the absence of the
    /// fields themselves.
    public static string Label(
        IReadOnlyList<string>? capabilities, string? rateLimitTier, string? ravenType)
    {
        var caps = capabilities ?? [];
        var tier = (rateLimitTier ?? "").Trim();

        // Team first: a raven organization also carries seats and billing that
        // would otherwise read as one of the personal plans.
        if (!string.IsNullOrEmpty(ravenType) || caps.Contains("raven")) return Named("Team", tier, "raven");

        if (caps.Contains("claude_max")) return Named("Max", tier, "claude_max");
        if (caps.Contains("claude_pro")) return "Pro";

        if (tier.Contains("enterprise", StringComparison.OrdinalIgnoreCase) ||
            caps.Any(c => c.Contains("enterprise", StringComparison.OrdinalIgnoreCase)))
            return "Enterprise";

        // `default_claude_ai` is the tier of BOTH free and pro (research), so
        // reaching it with no paid capability above means free. Without this the
        // fallback would prettify it into "Claude ai", which is not the name of
        // any plan claude.com sells.
        if (Strip(tier) == "claude_ai") return "Free";

        return tier.Length == 0 ? "Free" : Prettify(tier);
    }

    /// The plan name plus whatever the tier adds after its family — "Max" +
    /// "20x", "Team" + "premium". A tier that names another family, or none at
    /// all, leaves the bare name: better a plan without its multiplier than a
    /// multiplier that was invented.
    private static string Named(string plan, string tier, string family)
    {
        var rest = Strip(tier);
        if (rest == family) return plan;
        if (!rest.StartsWith(family + "_", StringComparison.Ordinal)) return plan;

        return $"{plan} {rest[(family.Length + 1)..].Replace('_', ' ')}";
    }

    /// Every tier seen so far is `default_<something>`; the prefix says nothing
    /// about the plan and only gets in the way of reading the rest.
    private static string Strip(string tier) =>
        tier.StartsWith("default_", StringComparison.Ordinal) ? tier["default_".Length..] : tier;

    private static string Prettify(string tier)
    {
        var words = Strip(tier).Replace('_', ' ');
        return words.Length == 0 ? "Free" : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
