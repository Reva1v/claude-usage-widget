namespace ClaudeUsageWidget.Core;

/// Everything one dial needs, derived from a snapshot. A plain value so the
/// derivation is testable without a UI framework.
public sealed record DialModel(string Title, double? Fraction, string? Remaining)
{
    public static DialModel Make(string key, string title, UsageSnapshot? snapshot, DateTimeOffset now)
    {
        var bucket = snapshot?[key];
        if (bucket is null) return new DialModel(title, null, null);

        return new DialModel(
            title,
            UsageMath.Fraction(bucket.Utilization),
            UsageMath.RemainingText(bucket.ResetsAt, now));
    }

    /// The three dials, always in the same order and always all three present —
    /// a dial with no data reads as `n/a` rather than disappearing and shifting
    /// the layout.
    ///
    /// "5H"/"7D" — the user chose time-window labels rather than limit names,
    /// to match what the taskbar band and tray already show.
    public static IReadOnlyList<DialModel> All(UsageSnapshot? snapshot, string? preferredModelKey, DateTimeOffset now)
    {
        var modelKey = snapshot is null ? null : ModelBuckets.Resolve(preferredModelKey, snapshot);

        var fiveHour = Make("five_hour", "5H", snapshot, now);
        var sevenDay = Make("seven_day", "7D", snapshot, now);

        // A spent week makes the five-hour window the wrong thing to wait for:
        // nothing more goes through until the WEEK resets, and a dial counting
        // down twenty minutes answers a question nobody is asking. The
        // percentage stays the five-hour one — only the countdown moves.
        //
        // The per-model bucket is a seven-day window too and deliberately does
        // NOT do this: exhausting it closes one model, and the five-hour window
        // still decides when the others may be used again.
        if (IsSpent(sevenDay)) fiveHour = fiveHour with { Remaining = sevenDay.Remaining };

        return
        [
            fiveHour,
            sevenDay,
            Make(
                modelKey ?? "",
                modelKey is not null ? ModelBuckets.Label(modelKey) : "MODEL",
                snapshot,
                now),
        ];
    }

    /// `UsageMath.Fraction` clamps at 1, so anything the server reports at or
    /// above 100 lands exactly here.
    private static bool IsSpent(DialModel dial) => dial.Fraction >= 1;
}

/// One account's line on the panel: its three dials plus the 5H reset time,
/// which at twelve dials no longer fits inside a dial centre.
/// <param name="PlanLabel">The subscription plan, ready to draw — the view is
/// handed the words, never the tier strings to map. Null means the fields have
/// not been fetched for this account yet, which is NOT the same as a free plan
/// and must draw nothing.</param>
public sealed record AccountRow(
    string AccountId,
    string DisplayName,
    IReadOnlyList<DialModel> Dials,
    string? SessionResetsIn,
    string? PlanLabel = null)
{
    /// Rows in settings order, always — the panel is a full picture, never a
    /// ranking. An account with no snapshot keeps its row with `n/a` dials
    /// rather than disappearing and shifting the rows below it.
    public static IReadOnlyList<AccountRow> ForAll(
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        string? preferredModelKey,
        DateTimeOffset now) =>
        accounts.Select(account =>
        {
            snapshots.TryGetValue(account.Id, out var snapshot);
            var dials = DialModel.All(snapshot, preferredModelKey, now);
            // Read off the dial rather than recomputed from the bucket: the
            // band shows one time per account, and it must be the time the
            // panel shows, including where a spent week moved it.
            return new AccountRow(
                account.Id, account.DisplayName, dials, dials[0].Remaining, PlanLabelFor(account));
        }).ToList();

    /// The plan, or null while nothing has been fetched for this account.
    ///
    /// `SubscriptionTier.Label` answers "what plan is this", and its answer for
    /// an organization that claims nothing is Free — correct there and wrong
    /// here, because an account whose fields were never read claims nothing for
    /// a different reason. All three absent is the sentinel: the picker fills
    /// the capability list (empty at worst) for any organization it found.
    private static string? PlanLabelFor(AccountProfile account) =>
        account.Capabilities is null && account.RateLimitTier is null && account.RavenType is null
            ? null
            : SubscriptionTier.Label(account.Capabilities, account.RateLimitTier, account.RavenType);
}

/// The line under the dials. Null means everything is fine and the widget
/// should stay quiet.
public static class StatusLine
{
    /// A snapshot older than three missed refreshes is worth calling out.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// `retryUntil` is the store's rate-limit deadline, when one is running;
    /// it turns the bare "rate limited" into a countdown.
    public static string? Text(UsageState state, DateTimeOffset now, DateTimeOffset? retryUntil = null) =>
        state switch
        {
            UsageState.Loading => "loading…",
            UsageState.Ok(_, var fetchedAt) => StaleText(fetchedAt, now),
            // The two states that make the dials meaningless are spoken for by
            // BlockingNotice, which covers the panel; repeating them down here
            // would say the same thing twice.
            UsageState.Failed(var error) when error.Kind is UsageErrorKind.NoCredentials or UsageErrorKind.Unauthorized => null,
            UsageState.Failed(var error) when error.Kind is UsageErrorKind.MalformedResponse => "unexpected response from the API",
            UsageState.Failed(var error) when error.Kind is UsageErrorKind.RateLimited => RateLimitedText(now, retryUntil),
            UsageState.Failed(var error) when error.Kind is UsageErrorKind.Network => error.Message,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static string? StaleText(DateTimeOffset fetchedAt, DateTimeOffset now)
    {
        var age = now - fetchedAt;
        if (age < StaleAfter) return null;
        var ago = UsageMath.RemainingText(now, fetchedAt);
        return ago is null ? null : $"updated {ago} ago";
    }

    private static string RateLimitedText(DateTimeOffset now, DateTimeOffset? retryUntil)
    {
        var wait = UsageMath.RemainingText(retryUntil, now);
        return wait is null ? "rate limited" : $"rate limited · retry in {wait}";
    }
}
