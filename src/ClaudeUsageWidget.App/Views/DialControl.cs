using System.Windows;
using System.Windows.Media;
using ClaudeUsageWidget.Core;
// UseWindowsForms makes System.Drawing globally visible — Point/Color
// also exist there under the same name.
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// A single dial: a backing ring, a fill arc and a percentage in the center.
/// Port of <c>Sources/ClaudeUsageWidgetCore/Views/DialView.swift</c>.
/// </summary>
///
/// Drawn manually via <see cref="OnRender"/> rather than by composing
/// ready-made WPF shapes: the arc needs an angle computed via
/// <see cref="DialGeometry"/>, and three centered lines of text over it —
/// assembling this declaratively out of standard panels would have come out
/// more verbose than a direct DrawingContext.
public sealed class DialControl : DialControlBase
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DialControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double?), typeof(DialControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RemainingProperty = DependencyProperty.Register(
        nameof(Remaining), typeof(string), typeof(DialControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DimmedProperty = DependencyProperty.Register(
        nameof(Dimmed), typeof(bool), typeof(DialControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public double? Fraction
    {
        get => (double?)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public string? Remaining
    {
        get => (string?)GetValue(RemainingProperty);
        set => SetValue(RemainingProperty, value);
    }

    public bool Dimmed
    {
        get => (bool)GetValue(DimmedProperty);
        set => SetValue(DimmedProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var scale = size / DesignSize;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var arcInset = 4 * scale;
        var arcWidth = 5 * scale;
        // DialArc(inset:) in the original draws at the same radius as the
        // backing ring after .padding(arcInset - arcWidth/2) on strokeBorder —
        // the centerline of the stroke is the same for both.
        var radius = size / 2 - arcInset;

        dc.DrawEllipse(null, RoundPen(Theme.TrackBrush, arcWidth), center, radius, radius);

        if (Fraction is { } fraction)
        {
            var color = Dimmed ? Theme.Dim : Theme.ColorFor(Thresholds.Level(fraction));
            DrawArc(dc, center, radius, fraction, color, arcWidth);
        }

        DrawText(dc, center, scale);
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double fraction, Color color, double strokeWidth)
    {
        // fraction == 1 gives a degenerate arc: the start and end points
        // coincide, and ArcTo draws nothing at all. The same epsilon trick
        // as in UsageMath.PercentText — we back off slightly from a full
        // circle, visually indistinguishable, but the arc always exists.
        var clamped = Math.Min(Math.Max(fraction, 0), 0.9999);
        if (clamped <= 0) return;

        var start = PointOnCircle(center, radius, -90);
        var end = PointOnCircle(center, radius, DialGeometry.AngleDegrees(clamped));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, clamped > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();

        dc.DrawGeometry(null, RoundPen(new SolidColorBrush(color), strokeWidth), geometry);
    }

    private void DrawText(DrawingContext dc, Point center, double scale)
    {
        var pixelsPerDip = DialText.PixelsPerDip(this);
        var valueBrush = Dimmed ? Theme.DimBrush : Theme.TextBrush;
        var valueString = Fraction is { } fraction ? UsageMath.PercentText(fraction) : "n/a";

        var titleText = DialText.Format(Title, Theme.LabelFontSize(scale), Theme.LabelWeight, Theme.DimBrush, pixelsPerDip);
        var valueText = DialText.Format(valueString, Theme.ValueFontSize(scale), Theme.ValueWeight, valueBrush, pixelsPerDip);
        var remainingText = DialText.Format(Remaining ?? "—", Theme.CaptionFontSize(scale), Theme.CaptionWeight, Theme.DimBrush, pixelsPerDip);

        // VStack(spacing: 1) in DialView.swift — the gap is literally 1pt, it
        // does not scale together with the rest of the dial.
        DialText.DrawStackCentered(dc, center, 1, titleText, valueText, remainingText);
    }
}
