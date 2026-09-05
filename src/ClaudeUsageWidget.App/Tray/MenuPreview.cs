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
            AvailableModelBuckets: ["seven_day_opus", "seven_day_fable"],
            SelectedModelBucket: null,
            ShowOnDesktop: true,
            PositionLocked: false,
            TaskbarBandEnabled: true,
            BandPosition: "tray",
            ResolvedModelLabel: "OPUS",
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
            report.AppendLine($"  h={item.Height,3}  w={item.Width,4}  x={item.Bounds.X,3}  {label}");
        }

        Save(menu, Path.Combine(dir, "menu.png"));

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
