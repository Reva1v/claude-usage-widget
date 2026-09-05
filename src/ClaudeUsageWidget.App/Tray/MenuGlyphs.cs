using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// Segoe MDL2 glyphs as menu images. WinForms menus take bitmaps, not fonts,
/// so each glyph is drawn once per colour and size and cached.
/// </summary>
///
/// The cache is what keeps this out of the paint path's cost: OnRenderItemCheck
/// runs on every hover frame of a checked item, and a fresh Bitmap + Font per
/// frame would be a GDI handle churn the tray would eventually notice.
internal static class MenuGlyphs
{
    private static readonly Dictionary<(string Glyph, int Argb, int Size), Bitmap> Cache = [];

    public static Bitmap Render(string glyph, Color ink, int sizePx)
    {
        // A zero/negative box comes from a layout that hasn't measured yet;
        // Bitmap would throw on it, and one dead pixel is a cheaper answer.
        if (sizePx < 1) sizePx = 1;

        var key = (glyph, ink.ToArgb(), sizePx);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var bitmap = new Bitmap(sizePx, sizePx);
        using (var g = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe MDL2 Assets", sizePx * 0.72f, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(ink))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, sizePx, sizePx), format);
        }

        Cache[key] = bitmap;
        return bitmap;
    }
}
