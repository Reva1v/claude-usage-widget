using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ClaudeUsageWidget.App.Views;
using Color = System.Drawing.Color;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// The tray menu in the panel's palette: flat background, a rounded full-width
/// hover, thin separators, a tick glyph instead of the boxed check, and a 1 px
/// border in the track colour in place of the system's.
/// </summary>
internal sealed class WidgetMenuRenderer : ToolStripProfessionalRenderer
{
    /// The colour table is handed to the base constructor once and can never be
    /// swapped afterwards, so it holds a mutable palette rather than a copy of
    /// today's colours — otherwise a theme switch would leave the one thing the
    /// base renderer still paints on its own (the highlight of a hovered but
    /// disabled item) in the old theme.
    private readonly MenuColorTable _colors;

    private Palette _palette;

    public WidgetMenuRenderer(Palette palette) : this(new MenuColorTable(palette))
    {
    }

    private WidgetMenuRenderer(MenuColorTable colors) : base(colors)
    {
        _colors = colors;
        _palette = colors.Palette;
        RoundedEdges = false;
    }

    public Palette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            _colors.Palette = value;
        }
    }

    /// The palette is WPF's; everything painted here is GDI+. Alpha is dropped
    /// deliberately: a menu window is opaque.
    private static Color Gdi(System.Windows.Media.Color c) => Color.FromArgb(c.R, c.G, c.B);

    private Color Background => Gdi(_palette.Panel);
    private Color Text => Gdi(_palette.Text);
    private Color Dim => Gdi(_palette.Dim);
    private Color Track => Gdi(_palette.Track);
    private Color Accent => Gdi(_palette.Accent);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Same colour as the menu: no separate gutter strip.
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Track);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }

        // 60 % track over the panel, inset by the menu's own padding, 4 px radius.
        var rect = new Rectangle(4, 0, e.Item.Width - 8, e.Item.Height);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var path = Rounded(rect, 4);
        using var brush = new SolidBrush(Color.FromArgb(153, Track));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Dim;
        e.TextRectangle = CenteredInRow(e.TextRectangle, e.Item);
        base.OnRenderItemText(e);
    }

    /// Vertical padding on a menu item grows the row from the BOTTOM: WinForms
    /// puts the content at top + Padding.Top and lets the rest of the height
    /// fall away below it, so a row padded 4 and 4 draws its text about 3 px
    /// above the middle (measured at 4x with CUW_RENDER_MENU). Recentring the
    /// rectangle against the row's own height puts text, icons and ticks on
    /// one line whatever the padding, the font or the DPI.
    private static Rectangle CenteredInRow(Rectangle box, ToolStripItem item)
    {
        if (box.Height <= 0 || box.Height >= item.Height) return box;

        return box with { Y = (item.Height - box.Height) / 2 };
    }

    /// The same for something drawn in the gutter — an icon or a tick — which
    /// also has to sit in the middle of that column rather than at whatever
    /// offset WinForms laid it out at. The gutter is the drop-down's own left
    /// padding: ToolStripDropDownMenu widens it to hold the image margin, so
    /// Padding.Left IS the column.
    private static Rectangle CenteredInGutter(Rectangle box, ToolStripItem item)
    {
        box = CenteredInRow(box, item);

        var gutter = item.Owner?.Padding.Left ?? 0;
        if (gutter <= box.Width) return box;

        return box with { X = (gutter - box.Width) / 2 };
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        // ToolStripArrowRenderEventArgs.Item is nullable (the arrow can be
        // drawn for the strip itself); no item means nothing is hovered.
        e.ArrowColor = e.Item?.Selected == true ? Text : Dim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Track);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // A tick in the accent colour where the icon would be — no box.
        DrawTick(e);
    }

    /// A checked item shows the tick INSTEAD of its icon. ToolStripMenuItem.OnPaint
    /// draws the check whenever CheckState isn't Unchecked — with ShowCheckMargin
    /// off it just moves it into the image rectangle — and then draws the Image
    /// over it unconditionally; suppressing the image here is what leaves the tick
    /// visible. Suppressing rather than redrawing the tick matters: two passes of
    /// the same anti-aliased glyph composite into a heavier one than the radio
    /// items in the submenus get, which have no image at all.
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem { Checked: true }) return;
        if (e.Image is null) return;

        // Drawn here rather than through the base renderer so the icon takes
        // the same recentring as the text — see CenteredInRow.
        var box = CenteredInGutter(e.ImageRectangle, e.Item);
        e.Graphics.DrawImage(e.Image, box.X, box.Y, box.Width, box.Height);
    }

    private void DrawTick(ToolStripItemImageRenderEventArgs e)
    {
        var box = e.ImageRectangle;
        if (box.Width <= 0 || box.Height <= 0) return;

        // E73E — CheckMark in Segoe MDL2 Assets. Written as an escape because
        // the glyph is a private-use codepoint that shows as an empty box (or
        // nothing) in most editors and diffs.
        box = CenteredInGutter(box, e.Item);
        var glyph = MenuGlyphs.Render("\uE73E", Accent, box.Height);
        var x = box.X + (box.Width - glyph.Width) / 2;
        var y = box.Y + (box.Height - glyph.Height) / 2;
        e.Graphics.DrawImage(glyph, x, y);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// Only the colours the base renderer still paints on its own (the gutter
    /// line and the check background) — everything visible is drawn by the
    /// overrides above.
    private sealed class MenuColorTable(Palette palette) : ProfessionalColorTable
    {
        public Palette Palette { get; set; } = palette;

        public override Color ToolStripDropDownBackground => Gdi(Palette.Panel);
        public override Color ImageMarginGradientBegin => Gdi(Palette.Panel);
        public override Color ImageMarginGradientMiddle => Gdi(Palette.Panel);
        public override Color ImageMarginGradientEnd => Gdi(Palette.Panel);
        public override Color CheckBackground => Gdi(Palette.Panel);
        public override Color CheckSelectedBackground => Gdi(Palette.Panel);
        public override Color CheckPressedBackground => Gdi(Palette.Panel);
        public override Color SeparatorDark => Gdi(Palette.Track);
        public override Color SeparatorLight => Gdi(Palette.Track);
        public override Color MenuBorder => Gdi(Palette.Track);
        public override Color MenuItemSelected => Gdi(Palette.Track);
        public override Color MenuItemSelectedGradientBegin => Gdi(Palette.Track);
        public override Color MenuItemSelectedGradientEnd => Gdi(Palette.Track);
        public override Color MenuItemBorder => Gdi(Palette.Track);
    }
}
