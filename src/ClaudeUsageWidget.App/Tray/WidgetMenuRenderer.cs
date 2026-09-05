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
        base.OnRenderItemText(e);
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

    /// A checked item shows the tick INSTEAD of its icon: with ShowCheckMargin
    /// off both the check and the image land in the same slot, and WinForms
    /// paints the image last. Drawing the tick here rather than merely skipping
    /// the image keeps the item from coming out blank should a WinForms build
    /// take the other branch and never call OnRenderItemCheck at all — at worst
    /// the same pixels are painted twice.
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem { Checked: true })
        {
            DrawTick(e);
            return;
        }

        base.OnRenderItemImage(e);
    }

    private void DrawTick(ToolStripItemImageRenderEventArgs e)
    {
        var box = e.ImageRectangle;
        if (box.Width <= 0 || box.Height <= 0) return;

        // E73E — CheckMark in Segoe MDL2 Assets. Written as an escape because
        // the glyph is a private-use codepoint that shows as an empty box (or
        // nothing) in most editors and diffs.
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
