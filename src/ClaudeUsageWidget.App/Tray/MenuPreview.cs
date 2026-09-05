using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.Core;
using Color = System.Drawing.Color;

namespace ClaudeUsageWidget.App.Tray;

/// Developer switch: set CUW_RENDER_MENU to a directory and the app shows the
/// tray menu offscreen, writes a PNG of it plus one of an open submenu, dumps
/// the measurements to a text file, and exits.
///
/// It exists because the menu cannot be judged from the code: item height,
/// the gap before a submenu and the width of the text column are whatever
/// WinForms measured them to be, and the only other way to see them is to ask
/// a human to right-click the tray icon and describe what they got.
internal static class MenuPreview
{
    public static void RunIfRequested()
    {
        var dir = Environment.GetEnvironmentVariable("CUW_RENDER_MENU");
        if (string.IsNullOrWhiteSpace(dir)) return;

        Directory.CreateDirectory(dir);

        foreach (var kind in new[] { ThemeKind.Dark, ThemeKind.Light })
        {
            Theme.Apply(kind);
            Render(Path.Combine(dir, kind.ToString().ToLowerInvariant()));
        }

        Environment.Exit(0);
    }

    private static void Render(string dir)
    {
        Directory.CreateDirectory(dir);

        using var tray = new TrayIcon();
        tray.SyncMenuState(new TrayMenuState(
            TrayMetricKey: "five_hour",
            AvailableModelBuckets: ["seven_day_fable", "seven_day_opus"],
            SelectedModelBucket: null,
            ShowOnDesktop: true,
            PositionLocked: false,
            TaskbarBandEnabled: true,
            BandPosition: "tray",
            ResolvedModelLabel: "FABLE",
            Accounts:
            [
                new AccountProfile("a1", "personal", null, null, 0),
                new AccountProfile("a2", "work", null, null, 0),
            ],
            TrayAccountId: "a1",
            PanelView: Core.PanelView.Classic,
            BandView: BandView.Metrics,
            Theme: ThemeChoice.System));

        var menu = tray.Menu;

        // A hidden owner window: a ContextMenuStrip needs one to be shown, and
        // the position is off-screen so nothing flashes over the desktop.
        using var owner = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
        };
        owner.Show();

        menu.Show(new Point(-31000, -31000));
        Application.DoEvents();

        var report = new StringBuilder();
        report.AppendLine($"menu size: {menu.Size.Width} x {menu.Size.Height}");
        report.AppendLine($"menu padding: {menu.Padding}");
        report.AppendLine($"font: {menu.Font.Name} {menu.Font.SizeInPoints}pt, dpi {menu.DeviceDpi}");
        report.AppendLine();
        report.AppendLine("items (height, text-x, text):");
        foreach (var item in menu.Items.OfType<ToolStripItem>())
        {
            var label = item is ToolStripSeparator ? "———" : item.Text;
            report.AppendLine(
                $"  h={item.Height,3}  w={item.Width,4}  content={item.ContentRectangle}  pad={item.Padding}  {label}");
        }

        Save(menu, Path.Combine(dir, "menu.png"));
        SaveZoom(menu, Path.Combine(dir, "menu-zoom.png"));

        // One row selected, so the hover highlight is in the picture: it is
        // drawn by the renderer and nothing else shows whether it sits evenly
        // in the row.
        if (menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == "Layout") is { } hovered)
        {
            hovered.Select();
            Application.DoEvents();
            Save(menu, Path.Combine(dir, "menu-hover.png"));
        }

        // The submenu's own window, and how far its left edge sits from the
        // parent menu's right edge: the gap the user sees.
        if (menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == "Accounts") is { } accounts)
        {
            accounts.ShowDropDown();
            Application.DoEvents();

            var drop = accounts.DropDown;
            report.AppendLine();
            report.AppendLine($"submenu size: {drop.Size.Width} x {drop.Size.Height}");
            report.AppendLine($"menu bounds:    {menu.Bounds}");
            report.AppendLine($"submenu bounds: {drop.Bounds}");
            report.AppendLine($"gap (submenu.Left - menu.Right): {drop.Bounds.Left - menu.Bounds.Right}");

            Save(drop, Path.Combine(dir, "submenu.png"));
            accounts.HideDropDown();
        }

        File.WriteAllText(Path.Combine(dir, "metrics.txt"), report.ToString());

        menu.Close();
        owner.Close();
    }

    /// The first few rows at 4x, with a red line across the middle of each one:
    /// a couple of pixels of vertical drift in the text is invisible at 1x and
    /// obvious here, and the line is the answer to "is it centred".
    private static void SaveZoom(ToolStrip strip, string path)
    {
        const int rows = 3;
        const int zoom = 4;

        var items = strip.Items.OfType<ToolStripItem>().Take(rows).ToList();
        if (items.Count == 0) return;

        var height = items.Sum(i => i.Height);
        using var full = new Bitmap(Math.Max(strip.Width, 1), Math.Max(strip.Height, 1));
        strip.DrawToBitmap(full, new Rectangle(0, 0, full.Width, full.Height));

        using var zoomed = new Bitmap(full.Width * zoom, height * zoom);
        using (var g = Graphics.FromImage(zoomed))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(full,
                new Rectangle(0, 0, zoomed.Width, zoomed.Height),
                new Rectangle(0, 0, full.Width, height),
                GraphicsUnit.Pixel);

            using var pen = new Pen(Color.Red, 1);
            var y = 0;
            foreach (var item in items)
            {
                var middle = (y + item.Height / 2.0) * zoom;
                g.DrawLine(pen, 0, (float)middle, zoomed.Width, (float)middle);
                y += item.Height;
            }
        }

        zoomed.Save(path, ImageFormat.Png);
    }

    /// DrawToBitmap rather than a screen grab: the menu is shown off-screen on
    /// purpose, and WM_PRINT paints through the same renderer overrides the
    /// real menu uses.
    private static void Save(ToolStrip strip, string path)
    {
        using var bitmap = new Bitmap(Math.Max(strip.Width, 1), Math.Max(strip.Height, 1));
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Magenta);
        }

        strip.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(path, ImageFormat.Png);
    }
}
