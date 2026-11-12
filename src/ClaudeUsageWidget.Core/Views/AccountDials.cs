namespace ClaudeUsageWidget.Core;

/// One account's plan and dials, derived together: the panel rows, the tray
/// icon and the tray tooltip all need both, and reading the plan in one place
/// and passing it to the dials in another is how a call site came to forget it.
/// <param name="Plan">The plan line's label; null until the plan is fetched.</param>
/// <param name="HasFiveHourLimit">See <see cref="SubscriptionTier.HasFiveHourLimit"/>.</param>
public sealed record AccountDials(string? Plan, bool HasFiveHourLimit, IReadOnlyList<DialModel> Dials)
{
    /// <paramref name="profile"/> is the account as the settings hold it now,
    /// null when it is gone from them.
    public static AccountDials For(
        AccountProfile? profile, UsageSnapshot? snapshot, string? preferredModelKey, DateTimeOffset now)
    {
        var hasFiveHourLimit = SubscriptionTier.HasFiveHourLimit(profile);
        return new AccountDials(
            SubscriptionTier.LabelFor(profile, snapshot),
            hasFiveHourLimit,
            DialModel.All(snapshot, preferredModelKey, now, hasFiveHourLimit));
    }
}
