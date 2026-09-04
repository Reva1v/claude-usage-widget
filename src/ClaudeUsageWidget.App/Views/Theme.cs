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
    /// The palette being drawn. Swapped by <see cref="Apply"/>; everything
    /// below that is a colour reads through here, so a consumer that asks on
    /// every paint is already theme-aware, and one that cached a brush
    /// subscribes to <see cref="Changed"/>.
    public static Palette Current { get; private set; } = Palette.Dark;

    public static ThemeKind Kind { get; private set; } = ThemeKind.Dark;

    /// Raised after Current has changed, on the thread Apply was called on —
    /// always the dispatcher; see App.ApplyTheme.
    public static event Action? Changed;

    public const string TextBrushKey = "Theme.TextBrush";
    public const string DimBrushKey = "Theme.DimBrush";
    public const string PanelBackgroundBrushKey = "Theme.PanelBackgroundBrush";
    public const string OverlayBackgroundBrushKey = "Theme.OverlayBackgroundBrush";

    /// Swaps the palette, republishes the XAML resource keys and tells the
    /// code consumers. Idempotent: the same kind twice is a no-op, so a
    /// system-theme event that changed nothing costs nothing.
    public static void Apply(ThemeKind kind)
    {
        if (kind == Kind && Current == Palette.For(kind)) return;

        Kind = kind;
        Current = Palette.For(kind);
        PublishResources();
        Changed?.Invoke();
    }

    /// The four brushes WidgetRootView.xaml binds with DynamicResource. Called
    /// at startup too, before the first window is created, so the keys exist.
    public static void PublishResources()
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null) return;
        resources[TextBrushKey] = Current.TextBrush;
        resources[DimBrushKey] = Current.DimBrush;
        resources[PanelBackgroundBrushKey] = Current.PanelBackgroundBrush;
        resources[OverlayBackgroundBrushKey] = Current.OverlayBackgroundBrush;
    }

    public static Color Panel => Current.Panel;
    public static Color Track => Current.Track;
    public static Color Text => Current.Text;
    public static Color Dim => Current.Dim;
    public static Color Accent => Current.Accent;
    public static Color Warning => Current.Warning;
    public static Color Danger => Current.Danger;
    public static Color Info => Current.Info;

    public static SolidColorBrush TrackBrush => Current.TrackBrush;
    public static SolidColorBrush TextBrush => Current.TextBrush;
    public static SolidColorBrush DimBrush => Current.DimBrush;
    public static SolidColorBrush WarningBrush => Current.WarningBrush;
    public static SolidColorBrush PanelBackgroundBrush => Current.PanelBackgroundBrush;
    public static SolidColorBrush OverlayBackgroundBrush => Current.OverlayBackgroundBrush;

    public static Color ColorFor(ThresholdLevel level) => Current.ColorFor(level);
    public static Color ColorFor(ServiceStatus status) => Current.ColorFor(status);
    public static SolidColorBrush PanelBrush(double alpha) => Current.PanelBrush(alpha);

    public const double PanelAlpha = Palette.PanelAlpha;
    public const double OverlayAlpha = Palette.OverlayAlpha;

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
}
