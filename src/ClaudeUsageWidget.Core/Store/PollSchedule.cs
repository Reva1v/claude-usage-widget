namespace ClaudeUsageWidget.Core;

/// Where each account sits inside the refresh cycle.
///
/// Four accounts on one 5-minute tick would fire four claude.ai requests at
/// the same instant, every time. The widget already avoids
/// `api.anthropic.com` because a poll that trips Cloudflare's limit renews
/// its own ban; there is no reason to build a burst on the endpoint it does
/// use. Spreading them costs nothing — the figures move far slower than the
/// cycle.
public static class PollSchedule
{
    public static TimeSpan OffsetFor(
        int index,
        int accountCount,
        int cycleSeconds = UsageStore.RefreshIntervalSeconds)
    {
        if (accountCount <= 0) throw new ArgumentOutOfRangeException(nameof(accountCount));
        if (index < 0 || index >= accountCount) throw new ArgumentOutOfRangeException(nameof(index));

        return TimeSpan.FromSeconds((double)cycleSeconds * index / accountCount);
    }
}
