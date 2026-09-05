using System.Drawing;
using System.Windows.Forms;
using ClaudeUsageWidget.App.Windows;
using FontFamily = System.Drawing.FontFamily;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// Font, spacing and Windows 11 corners for the tray menu and every submenu.
/// </summary>
internal static class MenuChrome
{
    /// The system menu font, not Consolas: the menu is chrome, not data.
    /// Segoe UI Variable exists on Windows 11 only; GDI falls back silently.
    private static readonly Font MenuFont = new(
        FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI",
        9f, GraphicsUnit.Point);

    /// Breathing room around the item stack, and inside every item — WinForms'
    /// own defaults are tighter than the panel this menu belongs to.
    private static readonly Padding MenuPadding = new(6, 6, 6, 6);
    private static readonly Padding ItemPadding = new(12, 6, 12, 6);

    /// Font, padding and the renderer, once, before the first Show — item
    /// heights are measured with whatever font is set at that moment.
    public static void Attach(ContextMenuStrip menu, WidgetMenuRenderer renderer)
    {
        menu.Renderer = renderer;
        menu.Font = MenuFont;
        menu.ShowImageMargin = true;
        menu.ShowCheckMargin = false;
        menu.Padding = MenuPadding;
        menu.DropShadowEnabled = true;
        StyleItems(menu.Items, renderer);
        menu.Opening += (_, _) => RoundCorners(menu);
    }

    /// <summary>
    /// Padding and image scaling for these items, and the same chrome for any
    /// submenu they already own. Public because parts of the menu are rebuilt
    /// on every opening (accounts, layout, the model buckets): items created
    /// after <see cref="Attach"/> would otherwise ship with WinForms' spacing
    /// next to items that have ours.
    /// </summary>
    public static void StyleItems(ToolStripItemCollection items, WidgetMenuRenderer renderer)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            item.Padding = ItemPadding;

            // The glyphs are rendered at the menu's own pixel size; letting
            // WinForms rescale them to its 16x16 default would blur them.
            item.ImageScaling = ToolStripItemImageScaling.None;

            // Reading DropDown on an item that has no submenu would create one,
            // so an empty item is left alone; a submenu filled in later is
            // handed to StyleDropDown by its owner instead.
            if (!item.HasDropDownItems) continue;

            StyleDropDown(item.DropDown, renderer);
            StyleItems(item.DropDownItems, renderer);
        }
    }

    /// <summary>
    /// The same font, padding, renderer and rounded corners for a submenu's own
    /// window. Call it once per dropdown — it subscribes to Opening.
    /// </summary>
    public static void StyleDropDown(ToolStripDropDown drop, WidgetMenuRenderer renderer)
    {
        drop.Renderer = renderer;
        drop.Font = MenuFont;
        drop.Padding = MenuPadding;

        // These are ToolStripDropDownMenu's own defaults, pinned so the root
        // menu and its submenus cannot drift apart: with the check margin off,
        // the tick is drawn in the image slot (WidgetMenuRenderer.DrawTick),
        // and a submenu with a check margin of its own would indent its
        // radio items by a second gutter.
        if (drop is ToolStripDropDownMenu dropMenu)
        {
            dropMenu.ShowImageMargin = true;
            dropMenu.ShowCheckMargin = false;
        }

        drop.Opening += (_, _) => RoundCorners(drop);
    }

    /// DWMWA_WINDOW_CORNER_PREFERENCE exists from Windows 11 (22000). The
    /// handle exists only while the dropdown is open, hence at Opening. A
    /// failure is a square menu, nothing else.
    private static void RoundCorners(ToolStripDropDown drop)
    {
        if (Environment.OSVersion.Version.Build < 22000) return;
        try
        {
            var pref = Win32.DwmwcpRound;
            Win32.DwmSetWindowAttribute(drop.Handle, Win32.DwmwaWindowCornerPreference, ref pref, sizeof(int));
        }
        catch
        {
            // Square corners.
        }
    }
}
