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
        using (var family = new FontFamily("Segoe MDL2 Assets"))
        using (var brush = new SolidBrush(ink))
        using (var path = new GraphicsPath())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // The glyph as an outline, then moved so its OWN bounding box is
            // centred in the bitmap. Drawing the string centred instead lands
            // it a pixel or so high: a text layout centres the font's line box
            // — ascent, descent and internal leading included — and an icon
            // glyph does not fill that box symmetrically.
            path.AddString(glyph, family, (int)FontStyle.Regular, sizePx * 0.72f,
                PointF.Empty, StringFormat.GenericTypographic);

            var bounds = path.GetBounds();
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                using var move = new Matrix();
                move.Translate(
                    (sizePx - bounds.Width) / 2 - bounds.X,
                    (sizePx - bounds.Height) / 2 - bounds.Y);
                path.Transform(move);
                g.FillPath(brush, path);
            }
        }

        Cache[key] = bitmap;
        return bitmap;
    }
}
