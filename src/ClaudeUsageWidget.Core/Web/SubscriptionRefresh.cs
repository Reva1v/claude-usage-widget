namespace ClaudeUsageWidget.Core;

/// When the three raw subscription fields are due another read.
///
/// They describe a SUBSCRIPTION, and a subscription changes: an account can
/// leave Team, a Max multiplier can move. Written once and never refreshed,
/// they went stale silently — the panel kept showing the old plan and the
/// launcher kept filtering the model list by it until the next sign-in.
///
/// Pure and here rather than in the session, so the cadence is a decision with
/// a test and not a literal buried in a fetch path nothing can exercise.
public static class SubscriptionRefresh
{
    /// Once an hour per account. The organizations endpoint is cheap and its
    /// answer changes about never, but it is not free: the usage endpoint is
    /// what the 429 budget is for, and a rate-limited account is paused whole.
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// True when nothing has been fetched yet — a fresh account, or a settings
    /// file written before the stamp existed — and once the interval has
    /// elapsed. A stamp in the FUTURE also refreshes: a clock moved back or a
    /// hand-edited file would otherwise freeze the fields until the wall clock
    /// caught up, which is the stale-plan bug again with a different cause.
    public static bool ShouldRefresh(DateTimeOffset? lastFetchedAt, DateTimeOffset now) =>
        lastFetchedAt is not { } last || now - last >= Interval || now < last;

    /// A fetch that told us nothing about the subscription:
    /// <see cref="OrganizationPicker.Fields"/> answers
    /// <see cref="OrganizationFields.Unknown"/> for a body that is not the
    /// expected JSON array and for one the stored organization id is no longer
    /// in — a challenge or interstitial page answered 2xx, say, while the usage
    /// endpoint still works.
    ///
    /// An EMPTY capability list is not this: the picker's own contract is that
    /// null means the organization was not found and empty means it was found
    /// and claims nothing, which is a fact about the account.
    public static bool LearnedNothing(OrganizationFields fetched) =>
        fetched.Capabilities is null && fetched.RateLimitTier is null && fetched.RavenType is null;

    /// What to store after a refresh: the fetched fields, unless the fetch
    /// learned nothing, in which case the ones already on the account.
    ///
    /// Refreshing may not DESTROY. Overwriting a good plan with Unknown blanks
    /// the panel's plan line and exports `"plan":null`, which the launcher
    /// reads as "plan unknown" and answers by un-filtering the model list — a
    /// Team account offered Fable, for as long as the cadence takes to ask
    /// again. A real answer still wins, or an account that left Team would
    /// stay Team forever.
    public static OrganizationFields Merge(OrganizationFields existing, OrganizationFields fetched) =>
        LearnedNothing(fetched) ? existing : fetched;
}
