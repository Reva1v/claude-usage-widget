using System.Windows.Media;
using ClaudeUsageWidget.Core;
// UseWindowsForms makes System.Drawing/System.Windows.Forms globally
// visible (see ClaudeUsageWidget.App.GlobalUsings.g.cs) — Color/FontFamily
// exist in both worlds under the same name, hence the explicit aliases to
// the WPF variants (the same trick as in App.xaml.cs for Application).
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using FontWeight = System.Windows.FontWeight;
using FontWeights = System.Windows.FontWeights;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Port of the palette and typography from <c>Sources/ClaudeUsageWidgetCore/Views/Theme.swift</c>:
/// a dark glass panel with pastel dials.
/// </summary>
///
/// RGB components are converted from float(0…1) to byte(0…255) once here,
/// rather than on every render frame.
public static class Theme
{
    public static readonly Color Panel = Color.FromRgb(30, 34, 48);
    public static readonly Color Track = Color.FromRgb(64, 69, 87);
    public static readonly Color Text = Color.FromRgb(199, 204, 222);
    public static readonly Color Dim = Color.FromRgb(115, 120, 140);

    public static readonly Color Accent = Color.FromRgb(166, 209, 137);
    public static readonly Color Warning = Color.FromRgb(229, 200, 144);
    public static readonly Color Danger = Color.FromRgb(231, 130, 132);

    /// Scheduled maintenance — an informational line, not an alarming one. Port of Theme.info.
    public static readonly Color Info = Color.FromRgb(138, 180, 230);

    public static readonly SolidColorBrush TrackBrush = Freeze(new SolidColorBrush(Track));
    public static readonly SolidColorBrush TextBrush = Freeze(new SolidColorBrush(Text));
    public static readonly SolidColorBrush DimBrush = Freeze(new SolidColorBrush(Dim));
    public static readonly SolidColorBrush WarningBrush = Freeze(new SolidColorBrush(Warning));

    public static Color ColorFor(ThresholdLevel level) => level switch
    {
        ThresholdLevel.Ok => Accent,
        ThresholdLevel.Warning => Warning,
        ThresholdLevel.Danger => Danger,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static Color ColorFor(ServiceStatus status) => status switch
    {
        ServiceStatus.Operational => Accent,
        ServiceStatus.Degraded or ServiceStatus.PartialOutage => Warning,
        ServiceStatus.MajorOutage => Danger,
        ServiceStatus.Maintenance => Info,
        ServiceStatus.Unknown => Dim,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// A monospaced font instead of SwiftUI's .monospacedDigit(): WPF has no
    /// declarative feature that turns on tabular figures for an arbitrary
    /// font, and Consolas is monospaced by default — the same effect
    /// ("percentages don't jitter on update") that Theme.swift was going for.
    public static readonly FontFamily FontFamily = new("Consolas");

    public static readonly FontWeight LabelWeight = FontWeights.SemiBold;
    public static readonly FontWeight ValueWeight = FontWeights.SemiBold;
    public static readonly FontWeight CaptionWeight = FontWeights.Medium;

    /// Fonts are defined at a design size of 170pt and scale linearly together
    /// with the panel (DialView.designSize / WidgetRootView.scale in Theme.swift).
    public static double LabelFontSize(double scale) => 8 * scale;
    public static double ValueFontSize(double scale) => 14 * scale;
    public static double CaptionFontSize(double scale) => 8 * scale;

    /// Panel padding and gap between dials — WidgetRootView.swift:42-44.
    public static double Padding(double scale) => 12 * scale;
    public static double Gap(double scale) => 10 * scale;

    /// Panel corner rounding radius — WidgetRootView.swift:87.
    public static double CornerRadius(double scale) => 22 * scale;

    /// The panel's main background. Theme.swift puts an NSVisualEffectView
    /// underneath it (desktop blur) and so gets away with alpha 0.35; here
    /// there is no blur — an acrylic composited backdrop would require
    /// either a new NuGet package or an undocumented DWM composition, which
    /// is out of scope for this task — so the alpha is higher, to keep the
    /// digits readable over arbitrary wallpapers.
    public const double PanelAlpha = 0.82;

    /// Header and BlockingNotice — as in Theme.swift (panel.opacity(0.92)): they
    /// are almost opaque there too, blur underneath them isn't essential.
    public const double OverlayAlpha = 0.92;

    /// Background of the main panel (2x2 dials) — used via x:Static in
    /// WidgetRootView.xaml, so this is a ready-made brush rather than a
    /// method: x:Static can only read fields/properties, calling
    /// PanelBrush(alpha) from XAML isn't possible.
    public static readonly SolidColorBrush PanelBackgroundBrush = Freeze(PanelBrush(PanelAlpha));

    /// Background of the header and BlockingNotice — the same panel, but almost opaque.
    public static readonly SolidColorBrush OverlayBackgroundBrush = Freeze(PanelBrush(OverlayAlpha));

    public static SolidColorBrush PanelBrush(double alpha) =>
        new(Color.FromArgb((byte)Math.Round(alpha * 255), Panel.R, Panel.G, Panel.B));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
