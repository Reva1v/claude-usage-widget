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
/// A Team seat's kind (Standard or Premium) is not in that body: two seats of
/// one organization logged the same fields on 2026-11-01. The usage is the
/// witness instead: see <see cref="LabelFor(AccountProfile?, UsageSnapshot?)"/>.
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
    /// The plan for one account as the settings hold it.
    ///
    /// Takes the PROFILE, not its three fields, so the read cannot be spelled
    /// out again at a call site no test reaches — which is where it was, in the
    /// usage export. A null profile is an answer too: the export runs off
    /// freshly loaded settings, and the account it is exporting may no longer
    /// be in them.
    public static string? LabelFor(AccountProfile? profile) => LabelFor(profile, null);

    /// The same, with the account's latest usage as the witness the
    /// organization lacks: a Team seat whose plan limits carry a Fable weekly
    /// bucket (any utilization) is a Premium seat, because a Standard seat gets
    /// Fable only through usage credits (claude.com/pricing). Another model's
    /// bucket is no witness. No usage yet reads plain "Team".
    public static string? LabelFor(AccountProfile? profile, UsageSnapshot? usage)
    {
        var label = profile is null
            ? null
            : LabelOrNull(profile.Capabilities, profile.RateLimitTier, profile.RavenType);

        return label == "Team" && usage?["seven_day_fable"] is not null
            ? "Team Premium"
            : label;
    }

    /// The plan sold without a five-hour limit; the dials read it.
    public const string Ultra = "Ultra";

    /// The plan, or null while nothing has been fetched for this account.
    ///
    /// <see cref="Label"/> answers "what plan is this", and its answer for an
    /// organization that claims nothing is Free — correct there, wrong for a
    /// caller that has not asked yet. All three fields absent is the sentinel:
    /// the picker fills the capability list (empty at worst) for any
    /// organization it found, so the guard is AND and never OR.
    ///
    /// One copy of the rule on purpose: the panel's plan line and the usage
    /// export both need it, and two copies would answer differently the day one
    /// of them is edited.
    public static string? LabelOrNull(
        IReadOnlyList<string>? capabilities, string? rateLimitTier, string? ravenType) =>
        capabilities is null && rateLimitTier is null && ravenType is null
            ? null
            : Label(capabilities, rateLimitTier, ravenType);

    /// Never empty and never a lie: an unknown tier is shown prettified rather
    /// than dropped, because silence on the panel reads as a broken widget.
    /// "We have not asked yet" is a different question and is not answered here
    /// — <see cref="LabelOrNull"/> keeps that one, from the absence of the
    /// fields themselves.
    public static string Label(
        IReadOnlyList<string>? capabilities, string? rateLimitTier, string? ravenType)
    {
        var caps = capabilities ?? [];
        var tier = (rateLimitTier ?? "").Trim();

        // Team first: a raven organization also carries seats and billing that
        // would otherwise read as one of the personal plans.
        if (!string.IsNullOrEmpty(ravenType) || caps.Contains("raven")) return Named("Team", tier, "raven");

        if (caps.Contains("claude_max")) return MaxPlan(tier);
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

    /// Max 5x is sold as "Max" and Max 20x as "Ultra" since the plans were
    /// renamed (claude.com/pricing: Max $100, Ultra $200). The tier strings
    /// kept the old names: `default_claude_max_20x` was still what an Ultra
    /// account returned on 2026-11-01.
    private static string MaxPlan(string tier) => Strip(tier) switch
    {
        "claude_max_20x" => Ultra,
        "claude_max_5x" => "Max",
        _ => Named("Max", tier, "claude_max"),
    };

    /// The plan name plus whatever the tier adds after its family, in title
    /// case: "Team" + "premium" reads "Team Premium", as the pricing page
    /// spells the seat. A tier that names another family, or none at all,
    /// leaves the bare name: better a plan without its multiplier than a
    /// multiplier that was invented.
    private static string Named(string plan, string tier, string family)
    {
        var rest = Strip(tier);
        if (rest == family) return plan;
        if (!rest.StartsWith(family + "_", StringComparison.Ordinal)) return plan;

        var words = rest[(family.Length + 1)..]
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..])
            .ToList();
        return words.Count == 0 ? plan : $"{plan} {string.Join(' ', words)}";
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
