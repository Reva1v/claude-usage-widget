using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsageWidget.Core;
// UseWindowsForms makes System.Drawing/System.Windows.Forms globally
// visible — Point/Cursor/Cursors also exist there under the same name.
using Point = System.Windows.Point;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// The fourth dial: claude.ai's own status. The ring is filled entirely,
/// not by a fraction — it's a state, not a percentage. A click opens
/// status.claude.com. Port of
/// <c>Sources/ClaudeUsageWidgetCore/Views/StatusDialView.swift</c>.
/// </summary>
public sealed class StatusDialControl : DialControlBase
{
    private const string StatusUrl = "https://status.claude.com";

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(ServiceStatus), typeof(StatusDialControl),
        new FrameworkPropertyMetadata(ServiceStatus.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DimmedProperty = DependencyProperty.Register(
        nameof(Dimmed), typeof(bool), typeof(StatusDialControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public ServiceStatus Status
    {
        get => (ServiceStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool Dimmed
    {
        get => (bool)GetValue(DimmedProperty);
        set => SetValue(DimmedProperty, value);
    }

    public StatusDialControl()
    {
        Cursor = Cursors.Hand;
        ToolTip = "Claude service status — click to open status.claude.com";
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        // A fully filled (even if transparent) rectangle takes part in
        // hit-testing over its whole area, while a bare outline only does so
        // along its own pixels. Port of .contentShape(Rectangle()) — StatusDialView.swift:50.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var scale = size / DesignSize;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var arcInset = 4 * scale;
        var arcWidth = 5 * scale;
        var radius = size / 2 - arcInset;

        dc.DrawEllipse(null, RoundPen(Theme.TrackBrush, arcWidth), center, radius, radius);

        var isUnknown = Status == ServiceStatus.Unknown;
        var ringColor = Dimmed || isUnknown ? Theme.Dim : Theme.ColorFor(Status);
        var ringBrush = new SolidColorBrush(ringColor) { Opacity = isUnknown ? 0.4 : 1.0 };
        ringBrush.Freeze();
        dc.DrawEllipse(null, RoundPen(ringBrush, arcWidth), center, radius, radius);

        DrawText(dc, center, scale, size);
    }

    private void DrawText(DrawingContext dc, Point center, double scale, double size)
    {
        var pixelsPerDip = DialText.PixelsPerDip(this);
        var valueBrush = Dimmed ? Theme.DimBrush : Theme.TextBrush;
        // .padding(.horizontal, 6 * scale) in StatusDialView.swift:47.
        var maxWidth = Math.Max(0, size - 2 * 6 * scale);

        var labelText = DialText.Format("STATUS", Theme.LabelFontSize(scale), Theme.LabelWeight, Theme.DimBrush, pixelsPerDip);
        var valueText = DialText.FormatFitted(
            ServiceStatusText.Label(Status), Theme.ValueFontSize(scale), Theme.ValueWeight, valueBrush, maxWidth, 0.6, pixelsPerDip);

        DialText.DrawStackCentered(dc, center, 1, labelText, valueText);
    }

    /// Armed by a press on this dial, disarmed by the release, by a lost
    /// capture or by a release outside the square.
    private bool _armed;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // The event is stopped here rather than bubbling up to
        // DesktopWidgetWindow — otherwise a click on the status dial would
        // start dragging the panel instead of opening the link. In the
        // original, SwiftUI's Button does exactly the same thing, intercepting
        // the gesture before NSWindow.mouseDown even learns about it.
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);

        // A press is not a click. The capture is what makes the release
        // arrive here even if the cursor has left the dial by then, so the
        // "released somewhere else" case can be told apart from a real click.
        _armed = CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        base.OnMouseLeftButtonUp(e);

        var armed = _armed;
        _armed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!armed) return;

        // Press and release on the SAME dial. A press that travelled off the
        // control before the release is a slip or a drag, not a click, and
        // this panel lives right above the notification area.
        var point = e.GetPosition(this);
        if (point.X < 0 || point.Y < 0 || point.X > ActualWidth || point.Y > ActualHeight) return;

        OpenStatusPage();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _armed = false;
    }

    private static void OpenStatusPage()
    {
        // "-" for the account: the status page is service-wide and this
        // control belongs to no account in particular.
        WidgetLog.Write("-", "browser-open", $"site=status-dial url={StatusUrl}");

        // UseShellExecute: true — without it .NET tries to launch the URL
        // directly as an executable and fails with a Win32Exception (the same
        // trick as in Tray/TrayIcon.cs).
        Process.Start(new ProcessStartInfo(StatusUrl) { UseShellExecute = true });
    }
}
