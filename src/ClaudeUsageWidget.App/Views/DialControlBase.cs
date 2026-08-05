using System.Windows;
using System.Windows.Media;
// UseWindowsForms делает System.Drawing глобально видимым — Point/Brush/Pen
// существуют и там под тем же именем.
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Общая геометрия для <see cref="DialControl"/> и <see cref="StatusDialControl"/>:
/// оба рисуют кольцо на одном и том же дизайн-размере 68pt (DialView.designSize
/// в оригинале) и переводят угол в точку на окружности одинаково.
/// </summary>
public abstract class DialControlBase : FrameworkElement
{
    /// Дизайн-размер циферблата, на котором заданы толщины и шрифты.
    protected const double DesignSize = 68;

    /// Экран WPF растёт вниз по Y, поэтому cos/sin в обычных градусах уже
    /// дают движение по часовой стрелке — ровно то, что нужно для угла из
    /// DialGeometry.AngleDegrees (−90° = 12 часов, дальше по часовой).
    protected static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    protected static Pen RoundPen(Brush brush, double thickness) =>
        new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
}
