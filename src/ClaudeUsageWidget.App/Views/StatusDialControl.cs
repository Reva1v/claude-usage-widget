using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsageWidget.Core;
// UseWindowsForms делает System.Drawing/System.Windows.Forms глобально
// видимыми — Point/Cursor/Cursors существуют и там под тем же именем.
using Point = System.Windows.Point;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Четвёртый циферблат: собственный статус claude.ai. Кольцо закрашено
/// целиком, а не на долю — состояния, а не процента. Клик открывает
/// status.claude.com. Порт
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

        // Полностью закрашенный (пусть и прозрачный) прямоугольник участвует
        // в hit-тестировании целиком, а голая обводка — только по своим
        // пикселям. Порт .contentShape(Rectangle()) — StatusDialView.swift:50.
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
        // .padding(.horizontal, 6 * scale) в StatusDialView.swift:47.
        var maxWidth = Math.Max(0, size - 2 * 6 * scale);

        var labelText = DialText.Format("STATUS", Theme.LabelFontSize(scale), Theme.LabelWeight, Theme.DimBrush, pixelsPerDip);
        var valueText = DialText.FormatFitted(
            ServiceStatusText.Label(Status), Theme.ValueFontSize(scale), Theme.ValueWeight, valueBrush, maxWidth, 0.6, pixelsPerDip);

        DialText.DrawStackCentered(dc, center, 1, labelText, valueText);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // Событие останавливается здесь, а не всплывает к
        // DesktopWidgetWindow — иначе клик по циферблату статуса запустил
        // бы перетаскивание панели вместо открытия ссылки. В оригинале то
        // же самое делает SwiftUI Button, перехватывающий жест раньше, чем
        // NSWindow.mouseDown вообще о нём узнаёт.
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
        OpenStatusPage();
    }

    private static void OpenStatusPage()
    {
        // UseShellExecute: true — без него .NET пытается запустить URL как
        // исполняемый файл напрямую и падает с Win32Exception (тот же приём,
        // что и в Tray/TrayIcon.cs).
        Process.Start(new ProcessStartInfo(StatusUrl) { UseShellExecute = true });
    }
}
