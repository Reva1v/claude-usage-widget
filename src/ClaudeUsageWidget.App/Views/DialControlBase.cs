using System.Windows;
using System.Windows.Media;
// UseWindowsForms makes System.Drawing globally visible — Point/Brush/Pen
// also exist there under the same name.
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Shared geometry for <see cref="DialControl"/> and <see cref="StatusDialControl"/>:
/// both draw a ring at the same 68pt design size (DialView.designSize
/// in the original) and convert an angle to a point on the circle the same way.
/// </summary>
public abstract class DialControlBase : FrameworkElement
{
    /// The dial's design size, at which the stroke widths and fonts are defined.
    protected const double DesignSize = 68;

    /// A WPF screen grows downward along Y, so cos/sin in ordinary degrees
    /// already give clockwise motion — exactly what's needed for the angle
    /// from DialGeometry.AngleDegrees (−90° = 12 o'clock, then clockwise).
    protected static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    protected static Pen RoundPen(Brush brush, double thickness) =>
        new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
}
