using System.Windows.Media;
using ClaudeUsageWidget.Core;
// UseWindowsForms делает System.Drawing/System.Windows.Forms глобально
// видимыми (см. ClaudeUsageWidget.App.GlobalUsings.g.cs) — Color/FontFamily
// существуют в обоих мирах под одним именем, отсюда явные алиасы на
// WPF-варианты (тот же приём, что и в App.xaml.cs для Application).
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using FontWeight = System.Windows.FontWeight;
using FontWeights = System.Windows.FontWeights;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Порт палитры и типографики из <c>Sources/ClaudeUsageWidgetCore/Views/Theme.swift</c>:
/// тёмная стеклянная панель с пастельными циферблатами.
/// </summary>
///
/// RGB-компоненты пересчитаны из float(0…1) в byte(0…255) один раз здесь, а
/// не на каждый кадр рендера.
public static class Theme
{
    public static readonly Color Panel = Color.FromRgb(30, 34, 48);
    public static readonly Color Track = Color.FromRgb(64, 69, 87);
    public static readonly Color Text = Color.FromRgb(199, 204, 222);
    public static readonly Color Dim = Color.FromRgb(115, 120, 140);

    public static readonly Color Accent = Color.FromRgb(166, 209, 137);
    public static readonly Color Warning = Color.FromRgb(229, 200, 144);
    public static readonly Color Danger = Color.FromRgb(231, 130, 132);

    /// Плановое обслуживание — информационная линия, не тревожная. Порт Theme.info.
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

    /// Моноширинный шрифт вместо SwiftUI .monospacedDigit(): в WPF нет
    /// декларативной фичи, включающей табличные цифры для произвольного
    /// шрифта, а Consolas моноширинный по умолчанию — тот же эффект
    /// («проценты не дрожат при обновлении»), которого добивался Theme.swift.
    public static readonly FontFamily FontFamily = new("Consolas");

    public static readonly FontWeight LabelWeight = FontWeights.SemiBold;
    public static readonly FontWeight ValueWeight = FontWeights.SemiBold;
    public static readonly FontWeight CaptionWeight = FontWeights.Medium;

    /// Шрифты заданы на дизайн-размере 170pt и масштабируются линейно вместе
    /// с панелью (DialView.designSize / WidgetRootView.scale в Theme.swift).
    public static double LabelFontSize(double scale) => 8 * scale;
    public static double ValueFontSize(double scale) => 14 * scale;
    public static double CaptionFontSize(double scale) => 8 * scale;

    /// Отступ панели и зазор между циферблатами — WidgetRootView.swift:42-44.
    public static double Padding(double scale) => 12 * scale;
    public static double Gap(double scale) => 10 * scale;

    /// Радиус скругления панели — WidgetRootView.swift:87.
    public static double CornerRadius(double scale) => 22 * scale;

    /// Основной фон панели. Theme.swift кладёт под него NSVisualEffectView
    /// (блюр рабочего стола) и поэтому обходится альфой 0.35; здесь блюра
    /// нет — акриловый composited backdrop потребовал бы либо нового
    /// NuGet-пакета, либо недокументированного DWM-состава, что за рамками
    /// этой задачи — так что альфа выше, чтобы цифры оставались читаемыми
    /// на произвольных обоях.
    public const double PanelAlpha = 0.82;

    /// Хедер и BlockingNotice — как в Theme.swift (panel.opacity(0.92)): они
    /// и там почти непрозрачны, блюр под ними не принципиален.
    public const double OverlayAlpha = 0.92;

    /// Фон самой панели (2x2 циферблатов) — используется через x:Static в
    /// WidgetRootView.xaml, поэтому это готовая кисть, а не метод: x:Static
    /// умеет читать только поля/свойства, вызвать PanelBrush(alpha) из XAML
    /// нельзя.
    public static readonly SolidColorBrush PanelBackgroundBrush = Freeze(PanelBrush(PanelAlpha));

    /// Фон хедера и BlockingNotice — та же панель, но почти непрозрачная.
    public static readonly SolidColorBrush OverlayBackgroundBrush = Freeze(PanelBrush(OverlayAlpha));

    public static SolidColorBrush PanelBrush(double alpha) =>
        new(Color.FromArgb((byte)Math.Round(alpha * 255), Panel.R, Panel.G, Panel.B));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
