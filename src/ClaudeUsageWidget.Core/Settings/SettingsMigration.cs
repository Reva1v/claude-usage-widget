namespace ClaudeUsageWidget.Core;

/// How many accounts the widget supports at once.
///
/// One is the normal case: most people have a single Claude subscription, and
/// a single-account install must look and behave exactly like the release
/// before accounts existed. The cap exists because above four the rows stop
/// being readable at the default scale and the per-cycle request count stops
/// being polite to an undocumented endpoint — not because anything in the code
/// counts to four. Raising it is this one line.
public static class AccountLimits
{
    public const int Max = 4;
}

/// Turns a settings file written before the account list existed into one with
/// a single account, carrying its organization id and rate-limit state.
///
/// Applied on load, never on save: the legacy fields are cleared here, so the
/// first save after an upgrade writes the new shape and the old one is gone.
public static class SettingsMigration
{
    /// The pre-multi-account WebView2 profile was literally named `default`;
    /// the migrated account keeps that id so it keeps pointing at the same
    /// cookies.
    public const string LegacyAccountId = "default";

    public static WidgetSettingsData Apply(WidgetSettingsData raw)
    {
        // Drops from the END so the accounts already on screen keep their rows.
        if (raw.Accounts.Count > AccountLimits.Max)
            return raw with { Accounts = raw.Accounts.Take(AccountLimits.Max).ToList() };

        if (raw.Accounts.Count > 0) return raw;

        return raw with
        {
            Accounts =
            [
                new AccountProfile(
                    LegacyAccountId,
                    LegacyAccountId,
                    raw.OrganizationId,
                    raw.RetryPausedUntil,
                    raw.ConsecutiveRateLimits),
            ],
            TrayAccountId = raw.TrayAccountId ?? LegacyAccountId,
            OrganizationId = null,
            RetryPausedUntil = null,
            ConsecutiveRateLimits = 0,
        };
    }
}
