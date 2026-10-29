using System.Windows.Media;
// UseWindowsForms makes System.Drawing globally visible — Point also exists
// there under the same name.
using Point = System.Windows.Point;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// The bare ring behind an account name in a cell: the dials' track at the
/// dials' geometry, so the name reads as one more dial in the row rather than
/// loose text beside them (the user, 2026-10-29). No fill and no text — the
/// name is a TextBlock laid over it, which keeps the trimming and tooltip the
/// row placements already have.
/// </summary>
public sealed class NameRingControl : DialControlBase
{
    /// How far in from the cell's edge the name may run: the ring's inset and
    /// stroke (4 + 5 / 2) plus a point and a half of air, at the design size.
    public const double TextInset = 8 / DesignSize;

    public NameRingControl()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        // The same numbers StatusDialControl draws its track with.
        var scale = size / DesignSize;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - 4 * scale;
        dc.DrawEllipse(null, RoundPen(Theme.TrackBrush, 5 * scale), center, radius, radius);
    }
}
