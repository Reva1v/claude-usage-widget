namespace ClaudeUsageWidget.Core;

/// A saved sign-in: the WebView2 profile that holds its cookies, what to call
/// it in the account list, and the state that is only global today because
/// there has only ever been one account.
///
/// `Id` doubles as the WebView2 ProfileName, so it is restricted to that
/// character set: ASCII letters and digits plus `# @ $ ( ) + - _ ~ .` and
/// space, at most 64 characters, never ending in a period or a space. A GUID
/// with dashes satisfies all of it.
/// <param name="ExportPath">Optional: a file this account's latest figures are
/// written to after every successful refresh, for another tool to read. Null —
/// the default — writes nothing. See <see cref="UsageExport"/>.</param>
public sealed record AccountProfile(
    string Id,
    string DisplayName,
    string? OrganizationId,
    DateTimeOffset? RetryPausedUntil,
    int ConsecutiveRateLimits,
    string? ExportPath = null,
    IReadOnlyList<string>? Capabilities = null,
    string? RateLimitTier = null,
    string? RavenType = null);

/// Everything persisted between launches. Serialized to JSON verbatim by
/// <see cref="SettingsStore"/> — every property here is a JSON field, so
/// adding one only requires updating this record.
public sealed record WidgetSettingsData
{
    public bool WidgetVisible { get; init; } = true;
    public bool PositionLocked { get; init; }

    /// The per-model bucket the third dial is pinned to. Null/empty means
    /// "let ModelBuckets choose".
    public string? ModelBucket { get; init; }

    /// Side length of the square panel, in points. One value drives both
    /// axes — the widget is always a square, so resizing cannot distort it.
    public double WidgetSide { get; init; } = WidgetSettings.DefaultSide;

    public double? WidgetX { get; init; }
    public double? WidgetY { get; init; }
    public string TrayMetricKey { get; init; } = "five_hour";
    public bool TaskbarBandEnabled { get; init; }

    /// Where the taskbar band docks: "tray" (left of the notification area,
    /// default) or "left" (left edge of the taskbar, e.g. where the Widgets
    /// button sits if the user disabled it).
    public string BandPosition { get; init; } = "tray";

    /// What the taskbar band shows: the tray account's three figures, or one
    /// group per account. NULLABLE: null follows the account count through
    /// <see cref="BandViews.Resolve"/>, so a single account keeps the band it
    /// had before accounts existed. Never default it here.
    public BandView? BandView { get; init; }
    /// Which account the tray icon and its metric describe. Null falls back to
    /// the first account.
    public string? TrayAccountId { get; init; }

    /// Where every account writes its figures for another tool to read. Null —
    /// the default — is off, and nothing is written at all.
    ///
    /// When set, each account writes
    /// `<ExportDirectory>\<UsageExport.FileNameFor(DisplayName, Id)>.widget.json`
    /// after every successful refresh. <see cref="AccountProfile.ExportPath"/>
    /// stays a per-account override and wins over this for that account.
    public string? ExportDirectory { get; init; }

    /// How the panel arranges accounts and how each account's block arranges
    /// its dials. Null or nonsense falls back to the default through
    /// <see cref="WidgetLayout.Sanitize"/> — never patched up halfway.
    public WidgetLayout? Layout { get; init; }

    /// Whether the service state is a dial in every block or one line for all
    /// of them. NULLABLE on purpose: null means the file predates the setting,
    /// which <see cref="StatusModes.Resolve"/> reads off the layout instead of
    /// guessing. Never default it here.
    public StatusMode? StatusMode { get; init; }

    /// Whether the per-model dial gets a cell. NULLABLE for the same reason as
    /// <see cref="StatusMode"/>: null is a file from before the setting, which
    /// <see cref="ModelDials.Resolve"/> reads as Shown so nobody loses the dial
    /// by upgrading. Never default it here.
    public ModelDial? ModelDial { get; init; }

    /// Whether the subscription plan is drawn under the account name. NULLABLE
    /// like the two above, but <see cref="PlanLines.Resolve"/> reads null as
    /// HIDDEN rather than shown: the line is new, so defaulting it on would grow
    /// every panel that upgrades for something nobody asked for. Never default
    /// it here.
    public PlanLine? PlanLine { get; init; }

    public IReadOnlyList<AccountProfile> Accounts { get; init; } = [];

    /// The account with this id, or null when there is none.
    public AccountProfile? Account(string id) => Accounts.FirstOrDefault(a => a.Id == id);

    /// A copy with one account rewritten, everything else untouched. An id
    /// that is no longer in the list (removed while a poll was in flight)
    /// changes nothing rather than resurrecting the account.
    public WidgetSettingsData WithAccount(string id, Func<AccountProfile, AccountProfile> update) =>
        this with { Accounts = Accounts.Select(a => a.Id == id ? update(a) : a).ToList() };

    /// Legacy single-account fields, from before there was an account list.
    /// Read once by <see cref="SettingsMigration"/> and cleared on the first
    /// save; never written again. Do not read them anywhere else — the live
    /// values live on <see cref="AccountProfile"/>.
    ///
    /// They stay on the record rather than being deleted because
    /// System.Text.Json drops unknown members silently: removing them would
    /// make an old settings file unreadable and the migration impossible.
    public string? OrganizationId { get; init; }
    public DateTimeOffset? RetryPausedUntil { get; init; }
    public int ConsecutiveRateLimits { get; init; }
}

/// Bounds and defaults for the widget's size. Ports
/// `Sources/ClaudeUsageWidgetCore/WidgetSettings.swift`, minus the
/// UserDefaults plumbing — persistence here goes through
/// <see cref="SettingsStore"/> and a single <see cref="WidgetSettingsData"/>
/// instead of individually keyed reads.
public static class WidgetSettings
{
    /// The layout is designed at 170 pt (the macOS small desktop widget
    /// footprint) and scales linearly from there.
    public const double DefaultSide = 170;

    public const double MinSide = 150;
    public const double MaxSide = 340;

    public static double ClampSide(double side) => Math.Min(Math.Max(side, MinSide), MaxSide);
}
