using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
// UseWindowsForms делает System.Drawing глобально видимым — Point/Brush
// существуют и там под тем же именем.
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using FlowDirection = System.Windows.FlowDirection;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Общая для <see cref="DialControl"/> и <see cref="StatusDialControl"/>
/// разметка стопки центрированных строк (заголовок/значение/остаток).
/// Вынесено отдельно, чтобы оба циферблата не дублировали одну и ту же
/// возню с <see cref="FormattedText"/>.
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

    /// Сжимает шрифт, пока строка не влезет в <paramref name="maxWidth"/>, но
    /// не более чем на <paramref name="minScale"/> — порт
    /// .minimumScaleFactor(0.6) из StatusDialView.swift:44.
    public static FormattedText FormatFitted(
        string text, double fontSize, FontWeight weight, Brush brush, double maxWidth, double minScale, double pixelsPerDip)
    {
        var formatted = Format(text, fontSize, weight, brush, pixelsPerDip);
        if (maxWidth <= 0 || formatted.Width <= maxWidth) return formatted;

        var factor = Math.Max(minScale, maxWidth / formatted.Width);
        return Format(text, fontSize * factor, weight, brush, pixelsPerDip);
    }

    /// Рисует строки, уложенные в стопку по центру X с зазором <paramref name="spacing"/>
    /// между ними, начиная с середины по вертикали (порт VStack(spacing:) в
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
