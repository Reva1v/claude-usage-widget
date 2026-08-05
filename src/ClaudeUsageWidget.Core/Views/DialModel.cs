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
    /// "5H"/"7D" — пользователь выбрал подписи временных окон, а не имён
    /// лимитов, чтобы совпадать с тем, что уже показывают taskbar-band и tray.
    public static IReadOnlyList<DialModel> All(UsageSnapshot? snapshot, string? preferredModelKey, DateTimeOffset now)
    {
        var modelKey = snapshot is null ? null : ModelBuckets.Resolve(preferredModelKey, snapshot);
        return
        [
            Make("five_hour", "5H", snapshot, now),
            Make("seven_day", "7D", snapshot, now),
            Make(
                modelKey ?? "",
                modelKey is not null ? ModelBuckets.Label(modelKey) : "MODEL",
                snapshot,
                now),
        ];
    }
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
