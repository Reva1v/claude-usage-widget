namespace ClaudeUsageWidget.Core;

/// Where things sit on the dial face. Pulled out of the view so the angles
/// are testable without a UI framework. Ports
/// `Sources/ClaudeUsageWidgetCore/Views/DialView.swift:5-10`; a later WPF
/// `ArcSegment` will need this angle to place the point where the arc ends.
public static class DialGeometry
{
    /// Dials start at twelve o'clock and run clockwise, like a watch.
    public static double AngleDegrees(double fraction) => -90 + 360 * fraction;
}
