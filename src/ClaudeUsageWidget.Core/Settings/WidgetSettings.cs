namespace ClaudeUsageWidget.Core;

/// A saved sign-in: the folder its session data lives under, plus what to
/// call it in the account switcher.
public sealed record AccountProfile(string Id, string DisplayName, string ProfileFolder);

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
    public string? OrganizationId { get; init; }
    public DateTimeOffset? RetryPausedUntil { get; init; }
    public int ConsecutiveRateLimits { get; init; }
    public IReadOnlyList<AccountProfile> Accounts { get; init; } = [];
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
