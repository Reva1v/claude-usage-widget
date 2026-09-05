using System.Windows.Media;
using ClaudeUsageWidget.Core;
using Color = System.Windows.Media.Color;

namespace ClaudeUsageWidget.App.Views;

/// The colours of ONE theme, with the brushes made once and frozen. Two
/// instances exist for the life of the process; Theme.Current points at
/// whichever is on.
public sealed record Palette(
    Color Panel,
    Color Track,
    Color Text,
    Color Dim,
    Color Accent,
    Color Warning,
    Color Danger,
    Color Info)
{
    /// Theme.swift's alpha was 0.35 over a desktop blur; without blur the
    /// digits need more behind them.
    public const double PanelAlpha = 0.82;

    /// Header and BlockingNotice — almost opaque, as in Theme.swift.
    public const double OverlayAlpha = 0.92;

    public SolidColorBrush TrackBrush { get; } = Freeze(new SolidColorBrush(Track));
    public SolidColorBrush TextBrush { get; } = Freeze(new SolidColorBrush(Text));
    public SolidColorBrush DimBrush { get; } = Freeze(new SolidColorBrush(Dim));
    public SolidColorBrush WarningBrush { get; } = Freeze(new SolidColorBrush(Warning));
    public SolidColorBrush AccentBrush { get; } = Freeze(new SolidColorBrush(Accent));

    public SolidColorBrush PanelBackgroundBrush { get; } = Freeze(new SolidColorBrush(WithAlpha(Panel, PanelAlpha)));
    public SolidColorBrush OverlayBackgroundBrush { get; } = Freeze(new SolidColorBrush(WithAlpha(Panel, OverlayAlpha)));

    /// The panel colour with no transparency — the tray menu, which cannot be
    /// translucent, and the rename dialog.
    public SolidColorBrush PanelOpaqueBrush { get; } = Freeze(new SolidColorBrush(Panel));

    public Color ColorFor(ThresholdLevel level) => level switch
    {
        ThresholdLevel.Ok => Accent,
        ThresholdLevel.Warning => Warning,
        ThresholdLevel.Danger => Danger,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public Color ColorFor(ServiceStatus status) => status switch
    {
        ServiceStatus.Operational => Accent,
        ServiceStatus.Degraded or ServiceStatus.PartialOutage => Warning,
        ServiceStatus.MajorOutage => Danger,
        ServiceStatus.Maintenance => Info,
        ServiceStatus.Unknown => Dim,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public SolidColorBrush PanelBrush(double alpha) => new(WithAlpha(Panel, alpha));

    /// Today's palette, unchanged: the port of Theme.swift.
    public static readonly Palette Dark = new(
        Panel: Color.FromRgb(0x1E, 0x22, 0x30),
        Track: Color.FromRgb(0x40, 0x45, 0x57),
        Text: Color.FromRgb(0xC7, 0xCC, 0xDE),
        Dim: Color.FromRgb(0x73, 0x78, 0x8C),
        Accent: Color.FromRgb(0xA6, 0xD1, 0x89),
        Warning: Color.FromRgb(0xE5, 0xC8, 0x90),
        Danger: Color.FromRgb(0xE7, 0x82, 0x84),
        Info: Color.FromRgb(0x8A, 0xB4, 0xE6));

    /// The same hues one step darker and more saturated: the dark pastels
    /// wash out on an off-white panel.
    ///
    /// Track is the unfilled part of a dial, and it is darker than the spec's
    /// first guess (#D3D8E3): against this panel that was a contrast ratio of
    /// 1.3 and the rings all but vanished (the user, 2026-09-05). #B7BECC is
    /// 1.7, the same as the dark theme's track against its own panel.
    public static readonly Palette Light = new(
        Panel: Color.FromRgb(0xF3, 0xF5, 0xF9),
        Track: Color.FromRgb(0xB7, 0xBE, 0xCC),
        Text: Color.FromRgb(0x26, 0x2B, 0x3A),
        Dim: Color.FromRgb(0x6C, 0x73, 0x88),
        Accent: Color.FromRgb(0x4E, 0x9A, 0x4C),
        Warning: Color.FromRgb(0xC9, 0x96, 0x2A),
        Danger: Color.FromRgb(0xD2, 0x56, 0x5A),
        Info: Color.FromRgb(0x38, 0x77, 0xC8));

    public static Palette For(ThemeKind kind) => kind == ThemeKind.Light ? Light : Dark;

    private static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
