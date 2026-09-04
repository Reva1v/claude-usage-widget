using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// Draws the tray's live digit — the value of the selected metric without
/// the "%" sign (on an icon the size of
/// <see cref="SystemInformation.SmallIconSize"/> the percent sign squeezes
/// the digits themselves into illegibility), white on transparent,
/// GDI+ → <see cref="Icon.FromHandle"/>.
/// </summary>
///
/// While there's no data yet, a placeholder ring is drawn — the same glyph
/// that used to live in TrayIcon.CreateRingIcon (a port of the
/// monochrome-ring from <c>ClaudeUsageWidgetApp.swift:17-30</c>; there it's
/// a template image that macOS itself tints for the light/dark menu bar —
/// the Win32 tray has no such auto-tinting, and the vast majority of tray
/// panels are dark, so white is hardcoded directly, not black).
public static class TrayIconRenderer
{
    /// <summary>
    /// <paramref name="valueText"/> — a value like "42%" (as in
    /// <c>TrayText.Metrics</c>) or null/empty/"—" (no fraction). The returned
    /// <see cref="Icon"/> wraps a new HICON owned by the caller: it must call
    /// DestroyIcon on replacement/disposal (the same contract that
    /// TrayIcon.CreateRingIcon used to have — see
    /// TrayIcon.SetIcon/Dispose).
    /// </summary>
    public static Icon Render(string? valueText)
    {
        var size = SystemInformation.SmallIconSize;

        using var bitmap = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // AntiAlias/AntiAliasGridFit — plain grayscale anti-aliasing, not
            // ClearType: on a transparent background subpixel ClearType would give
            // colored halos along glyph edges, while these two modes don't.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var digits = DigitsOnly(valueText);
            if (digits is null)
            {
                DrawRing(g, size);
            }
            else
            {
                DrawDigits(g, size, digits);
            }
        }

        // Bitmap.GetHicon() allocates a new HICON that outlives the bitmap —
        // ownership passes to the caller (see the doc comment above).
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>"42%" → "42"; a string with no digits at all — null/empty, "—"
    /// (TrayText.Metrics for a missing fraction), or anything else
    /// non-numeric — → null, meaning the placeholder ring. Checking for "at
    /// least one digit" rather than a pointwise comparison with "—":
    /// RefreshTrayIcon on the very first startup (LastSnapshot still null)
    /// feeds exactly "—" in here, and that's what needs checking, not just
    /// the explicit null/empty cases.</summary>
    private static string? DigitsOnly(string? valueText)
    {
        if (string.IsNullOrEmpty(valueText)) return null;

        var digits = valueText.TrimEnd('%');
        return digits.Any(char.IsDigit) ? digits : null;
    }

    private static void DrawRing(Graphics g, Size size)
    {
        using var pen = new Pen(Color.White, 2f);
        const float inset = 2f;
        g.DrawEllipse(pen, inset, inset, size.Width - inset * 2, size.Height - inset * 2);
    }

    private static void DrawDigits(Graphics g, Size size, string digits)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        using var font = FitFont(g, digits, size);
        var rect = new RectangleF(0, 0, size.Width, size.Height);
        g.DrawString(digits, font, Brushes.White, rect, format);
    }

    /// <summary>
    /// Picks the largest Segoe UI Bold size at which the string still fits
    /// within the icon's width — "8" and "100" have completely different
    /// widths at the same size, and the tray is too small to fix the font for
    /// the worst case (three digits).
    /// </summary>
    private static Font FitFont(Graphics g, string digits, Size size)
    {
        const float minEm = 6f;
        const float margin = 1f;

        for (var em = size.Height * 0.95f; em >= minEm; em -= 0.5f)
        {
            var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(digits, font);
            if (measured.Width <= size.Width - margin) return font;
            font.Dispose();
        }

        return new Font("Segoe UI", minEm, FontStyle.Bold, GraphicsUnit.Pixel);
    }
}
