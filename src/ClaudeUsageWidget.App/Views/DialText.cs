using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
// UseWindowsForms makes System.Drawing globally visible — Point/Brush
// also exist there under the same name.
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using FlowDirection = System.Windows.FlowDirection;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Shared between <see cref="DialControl"/> and <see cref="StatusDialControl"/>
/// layout of a stack of centered lines (title/value/remaining).
/// Factored out separately so both dials don't duplicate the same
/// fiddling with <see cref="FormattedText"/>.
/// </summary>
internal static class DialText
{
    public static double PixelsPerDip(Visual visual) => VisualTreeHelper.GetDpi(visual).PixelsPerDip;

    public static FormattedText Format(string text, double fontSize, FontWeight weight, Brush brush, double pixelsPerDip) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(Theme.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            Math.Max(fontSize, 1),
            brush,
            pixelsPerDip);

    /// Shrinks the font until the line fits into <paramref name="maxWidth"/>, but
    /// by no more than <paramref name="minScale"/> — port of
    /// .minimumScaleFactor(0.6) from StatusDialView.swift:44.
    public static FormattedText FormatFitted(
        string text, double fontSize, FontWeight weight, Brush brush, double maxWidth, double minScale, double pixelsPerDip)
    {
        var formatted = Format(text, fontSize, weight, brush, pixelsPerDip);
        if (maxWidth <= 0 || formatted.Width <= maxWidth) return formatted;

        var factor = Math.Max(minScale, maxWidth / formatted.Width);
        return Format(text, fontSize * factor, weight, brush, pixelsPerDip);
    }

    /// Draws lines stacked centered on X with a gap of <paramref name="spacing"/>
    /// between them, starting from the vertical middle (port of VStack(spacing:) in
    /// DialView.swift / StatusDialView.swift).
    public static void DrawStackCentered(DrawingContext dc, Point center, double spacing, params FormattedText[] lines)
    {
        var totalHeight = lines.Sum(line => line.Height) + spacing * (lines.Length - 1);
        var y = center.Y - totalHeight / 2;

        foreach (var line in lines)
        {
            dc.DrawText(line, new Point(center.X - line.Width / 2, y));
            y += line.Height + spacing;
        }
    }
}
