namespace ClaudeUsageWidget.Core;

/// What the taskbar band shows.
public enum BandView
{
    /// The tray account's three figures — 5H, 7D and the model — as label
    /// over percentage, the band as it was before accounts existed.
    Metrics,

    /// Every account's name over its 5H figure and reset time.
    Accounts,
}

public static class BandViews
{
    /// Null — a file that never chose — follows the account count: with one
    /// account the three-metric band is the one people already had, and the
    /// per-account band only earns its width once there is a second account.
    public static BandView Resolve(BandView? saved, int accountCount) =>
        saved ?? (accountCount <= 1 ? BandView.Metrics : BandView.Accounts);
}
