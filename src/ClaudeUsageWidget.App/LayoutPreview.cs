using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.App.Windows;
using ClaudeUsageWidget.Core;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
// WinForms has a HorizontalAlignment of its own; this file means WPF's.
using HorizontalAlignment = System.Windows.HorizontalAlignment;
// UseWindowsForms makes System.Windows.Forms and System.Drawing globally
// visible — Border and Color have namesakes there.
using Border = System.Windows.Controls.Border;
using Color = System.Windows.Media.Color;

namespace ClaudeUsageWidget.App;

/// Developer switch: set CUW_RENDER_PREVIEW to a directory and the app renders
/// every layout combination to PNG there and exits, instead of starting.
///
/// It exists because the widget sits BEHIND application windows on purpose, so
/// a screenshot of the desktop shows whatever is covering it rather than the
/// panel. Judging a layout then depends on the state of someone's desktop; this
/// does not. Off unless the variable is set.
internal static class LayoutPreview
{
    public static void RunIfRequested()
    {
        var dir = Environment.GetEnvironmentVariable("CUW_RENDER_PREVIEW");
        if (string.IsNullOrWhiteSpace(dir)) return;

        Directory.CreateDirectory(dir);

        var now = DateTimeOffset.Now;
        // The subscription fields are the ones `/api/organizations` really
        // returned for these accounts on 2026-09-04 (widget.log), so the plan
        // PNGs show the labels a user will actually see rather than
        // placeholders. `shared` is left without them on purpose: that is what
        // an account whose fields have never been fetched looks like, and its
        // plan line must be BLANK rather than "Free".
        var accounts = new[]
        {
            new AccountProfile("a1", "personal", null, null, 0,
                Capabilities: ["claude_max", "chat"], RateLimitTier: "default_claude_max_20x"),
            new AccountProfile("a2", "work", null, null, 0,
                Capabilities: ["raven", "chat"], RateLimitTier: "default_raven", RavenType: "team"),
            new AccountProfile("a3", "low", null, null, 0,
                Capabilities: ["chat", "claude_max"], RateLimitTier: "default_claude_max_5x"),
            new AccountProfile("a4", "shared", null, null, 0),
        };

        var snapshots = new Dictionary<string, UsageSnapshot?>
        {
            ["a1"] = Snap(2, 100, 95, now),
            ["a2"] = Snap(62, 30, 12, now),
            ["a3"] = Snap(44, 15, 8, now),
            ["a4"] = null,
        };

        var cases = new (string Name, WidgetLayout Layout, int Accounts)[]
        {
            ("1-classic", PanelViews.LayoutFor(PanelView.Classic), 1),
            ("1-grid-cell", WidgetLayout.Default, 1),
            ("3-grid-cell", WidgetLayout.Default, 3),
            ("4-grid-cell", WidgetLayout.Default, 4),
            ("3-row-above", WidgetLayout.Default with
                { Block = BlockLayout.Default with { Flow = LayoutFlow.Row, Name = NamePlacement.Above } }, 3),
            ("2-column-below", WidgetLayout.Default with
                { PanelFlow = LayoutFlow.Column,
                  Block = BlockLayout.Default with { Flow = LayoutFlow.Row, Name = NamePlacement.Below } }, 2),
            ("2-grid-status", WidgetLayout.Default with
                { Block = BlockLayout.Default.Toggle(BlockItem.Status) }, 2),
            ("2-panelrow-hidden", WidgetLayout.Default with
                { PanelFlow = LayoutFlow.Row,
                  Block = BlockLayout.Default with { Flow = LayoutFlow.Grid, Name = NamePlacement.Hidden } }, 2),
        };

        // Every case in both palettes, into a folder each: the light values
        // are judged against the same PNGs the dark ones are, and a repaint
        // that only works in one theme shows up here rather than on the
        // desktop.
        foreach (var kind in new[] { ThemeKind.Dark, ThemeKind.Light })
        {
            Theme.Apply(kind);
            var themeDir = Path.Combine(dir, kind.ToString().ToLowerInvariant());
            Directory.CreateDirectory(themeDir);
            RenderAll(themeDir, accounts, snapshots, cases, now);
        }

        Environment.Exit(0);
    }

    /// Everything the preview draws, into one directory. Called once per
    /// palette; nothing in here depends on which one is active — the views
    /// read Theme.Current as they paint.
    private static void RenderAll(
        string dir,
        AccountProfile[] accounts,
        Dictionary<string, UsageSnapshot?> snapshots,
        (string Name, WidgetLayout Layout, int Accounts)[] cases,
        DateTimeOffset now)
    {
        foreach (var (name, layout, count) in cases)
        {
            var view = new WidgetRootView();
            // The mode a case means is the one its own order implies — the same
            // reading a settings file written before the setting gets.
            view.ApplyLayout(layout, StatusModes.Resolve(null, layout), ModelDial.Shown, PlanLine.Hidden, count, 228);

            var rows = AccountRow.ForAll(accounts.Take(count).ToList(), snapshots, null, now);
            view.SetContent(rows, ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

            LayoutPass(view);
            Save(view, Path.Combine(dir, name + ".png"));
        }

        RenderEditMode(dir, accounts.Take(2).ToList(), snapshots, now);
        RenderEditDrag(dir, accounts.Take(2).ToList(), snapshots, now);
        RenderEditAfterSwap(dir, accounts.Take(2).ToList(), snapshots, now);
        RenderEditDragNameAbove(dir, accounts.Take(2).ToList(), snapshots, now);

        RenderToolbar(dir, "toolbar-min", WidgetSettings.MinSide, WidgetLayout.Default,
            accounts.Take(2).ToList(), snapshots, now);
        RenderToolbar(dir, "toolbar-default", WidgetSettings.DefaultSide, WidgetLayout.Default,
            accounts.Take(2).ToList(), snapshots, now);
        RenderToolbar(dir, "toolbar-max", WidgetSettings.MaxSide, WidgetLayout.Default,
            accounts.Take(2).ToList(), snapshots, now);
        RenderToolbar(dir, "toolbar-wrap", WidgetSettings.MinSide,
            WidgetLayout.Default with { Block = BlockLayout.Default with { Flow = LayoutFlow.Column } },
            accounts.Take(1).ToList(), snapshots, now);

        RenderStatusLine(dir, "status-line-operational.png", ServiceStatus.Operational,
            accounts.Take(2).ToList(), snapshots, now);
        RenderStatusLine(dir, "status-line-degraded.png", ServiceStatus.Degraded,
            accounts.Take(2).ToList(), snapshots, now);
        RenderStatusCellRebuild(dir, accounts.Take(2).ToList(), snapshots, now);
        RenderModelHidden(dir, accounts.Take(2).ToList(), snapshots, now);

        RenderPadding(dir, "padding-170.png", WidgetSettings.DefaultSide,
            accounts.Take(3).ToList(), snapshots, now);
        RenderPadding(dir, "padding-207.png", 207, accounts.Take(3).ToList(), snapshots, now);

        RenderPlanLine(dir, "plan-line-above", NamePlacement.Above,
            accounts.Take(2).ToList(), snapshots, now);
        RenderPlanLine(dir, "plan-line-cell", NamePlacement.Cell,
            accounts.Take(2).ToList(), snapshots, now);
        RenderPlanLine(dir, "plan-line-below", NamePlacement.Below,
            accounts.Take(2).ToList(), snapshots, now);
        // All four, so `shared` — the account whose fields were never fetched —
        // is actually LOOKED AT with the line on. Its plan must be blank, not
        // "Free": null is "we have not asked", and the panel may not answer a
        // question nobody has put to the server.
        RenderPlanLine(dir, "plan-line-unfetched", NamePlacement.Above, accounts, snapshots, now);

        RenderBand(dir, AccountRow.ForAll(accounts, snapshots, null, now));

    }

    /// Edit-mode chrome offscreen. `UpdateLayout` first and the mode second:
    /// the outlines are read off arranged positions, and nothing is arranged
    /// until the panel has been measured.
    private static void RenderEditMode(
        string dir,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        var view = new WidgetRootView();
        view.ApplyLayout(WidgetLayout.Default, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(
            AccountRow.ForAll(accounts, snapshots, null, now),
            ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        view.EditMode = true;
        // The mode added the strip's band and the view grew by it: re-arrange
        // at the NEW size, or the render is cropped and every outline is read
        // off the old geometry.
        LayoutPass(view);

        Save(view, Path.Combine(dir, "edit-mode.png"));
    }

    /// The strip on BOTH sides of the panel, with the plain panel beside it —
    /// `<stem>-base`, `<stem>-above`, `<stem>-below`.
    ///
    /// The base PNG is what the other two are judged against, and the judgement
    /// is arithmetic on the file itself: same width, height taller by exactly
    /// the reserve. That is the amendment's whole claim — entering the mode
    /// grows the WINDOW and does not touch the panel — and a PNG pair is the
    /// only place it can be seen rather than asserted. What the eye adds: nine
    /// glyphs on one row and none of them a hollow box, the strip clear of the
    /// rounded corner on the side it was put, and the dashed cell outlines
    /// still on the cells after the band moved everything inside the window.
    private static void RenderToolbar(
        string dir,
        string stem,
        double side,
        WidgetLayout layout,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        Render(stem + "-base.png", null);
        Render(stem + "-above.png", true);
        Render(stem + "-below.png", false);

        void Render(string name, bool? stripAbove)
        {
            var view = new WidgetRootView();
            view.ApplyLayout(layout, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, side);
            view.SetContent(
                AccountRow.ForAll(accounts, snapshots, null, now),
                ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);
            LayoutPass(view);

            if (stripAbove is { } above)
            {
                // The side before the mode, the order the window uses: the view
                // measures itself once, off both.
                view.StripAbove = above;
                view.EditMode = true;
                LayoutPass(view);
            }

            Save(view, Path.Combine(dir, name));
        }
    }

    /// A drag in flight: the 5H dial halfway to the account name it would swap
    /// with, and that name outlined as the target. A mouse cannot be driven
    /// through a render pass, so `PreviewDrag` parks the chrome — but it parks
    /// it by calling what the mouse handlers call, so what this PNG shows is
    /// the shipped ghost and the shipped highlight, not a mock-up of them.
    ///
    /// SetContent runs again AFTER the mode is on, which the other case does
    /// not: that is what puts the hint on the status line, and a degraded
    /// service here proves the hint is appended to the notice rather than
    /// hiding it.
    private static void RenderEditDrag(
        string dir,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        var rows = AccountRow.ForAll(accounts, snapshots, null, now);

        var view = new WidgetRootView();
        view.ApplyLayout(WidgetLayout.Default, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(rows, ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        view.EditMode = true;
        view.SetContent(rows, ServiceStatus.Degraded, dimmed: false, statusLine: null, notice: null);
        // The mode added the strip's band and the view grew by it: re-arrange
        // at the NEW size, or the render is cropped and every outline is read
        // off the old geometry.
        LayoutPass(view);

        // Default's LaidOut is [Name, 5H, 7D, Model] — dragging cell 1 onto
        // cell 0 is the swap Task 4's live check performs by hand.
        view.PreviewDrag(block: 0, from: 1, to: 0);
        view.UpdateLayout();

        Save(view, Path.Combine(dir, "edit-mode-drag.png"));
    }

    /// Edit mode SURVIVING A SWAP: the panel is re-rendered from a swapped
    /// layout the way `App` re-renders it after a drop, with the mode still on.
    ///
    /// `ApplyLayout` runs `BuildGrid`, which throws away every cell host and
    /// constructs new ones, while `_chromeBounds` — the cache that lets
    /// `RefreshEditChrome` return early — still keys the dead hosts. This is the
    /// case that asks the chrome to redraw against a grid it has never seen, and
    /// the reason it is rendered rather than reasoned about.
    ///
    /// The swap is `BlockLayout.SwapCells`, not a hand-built record, so the PNG
    /// judges the shipped operation. What has to be in it: the dials visibly
    /// moved in BOTH blocks (one `BlockLayout` serves every account), and
    /// exactly one dashed outline per cell — no missing outline, no orphan left
    /// over a cell that no longer exists.
    ///
    /// Note what this cannot show. A swap permutes cell CONTENTS, never the
    /// cell rectangles: the hosts fill uniform `DialSize` tracks, so the set of
    /// outline rectangles is identical before and after. Chrome that failed to
    /// rebuild shows up here as outlines missing, doubled or orphaned — never
    /// as outlines offset from the dials they belong to.
    private static void RenderEditAfterSwap(
        string dir,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        var rows = AccountRow.ForAll(accounts, snapshots, null, now);

        var view = new WidgetRootView();
        view.ApplyLayout(WidgetLayout.Default, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(rows, ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        view.EditMode = true;
        // The mode added the strip's band and the view grew by it: re-arrange
        // at the NEW size, or the render is cropped and every outline is read
        // off the old geometry.
        LayoutPass(view);

        // Default's LaidOut is [Name, 5H, 7D, Model] over a 2x2 grid, so 0 and 3
        // are opposite corners: the name and the OPUS dial change places, which
        // no eye can mistake for "nothing happened". SetContent runs again
        // because BuildGrid discarded the visuals along with the hosts.
        var swapped = WidgetLayout.Default with { Block = BlockLayout.Default.SwapCells(0, 3) };
        view.ApplyLayout(swapped, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(rows, ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        Save(view, Path.Combine(dir, "edit-mode-after-swap.png"));
    }

    /// The drag chrome where `LaidOut` and `Order` are NOT the same index space.
    ///
    /// Every other edit case uses `WidgetLayout.Default`, i.e.
    /// `NamePlacement.Cell`, where the laid-out positions and `Order` coincide —
    /// so the translation `SwapCells` calls "where this gets written wrong" is
    /// exercised by unit tests only, never through the cell registry into
    /// `CellHit.Cell`. With the name Above, `LaidOut` is the three DIALS while
    /// `Order[0]` is still the name.
    ///
    /// So cell 0 is 5H and cell 2 is OPUS: the ghost is a 60% copy of the 5H
    /// dial parked at the MIDPOINT of the two, and the solid target outline sits
    /// on OPUS. The name line above the dials carries no dashed outline at all —
    /// it is not a laid-out cell and was never registered as a host. The
    /// confusion this would catch reads as chrome on the name line, a target on
    /// the middle dial, or no chrome whatsoever.
    private static void RenderEditDragNameAbove(
        string dir,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        // Row rather than Grid so the three dials are one line, first to last,
        // and "cell 0" and "cell 2" are the ends of it.
        var layout = WidgetLayout.Default with
        {
            Block = BlockLayout.Default with { Flow = LayoutFlow.Row, Name = NamePlacement.Above },
        };

        var rows = AccountRow.ForAll(accounts, snapshots, null, now);

        var view = new WidgetRootView();
        view.ApplyLayout(layout, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(rows, ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        // The one edit case with the strip ABOVE: everything in the window is
        // pushed down by the band, and the outlines are read off arranged
        // positions — so this is where a band that moved the panel without
        // moving the chrome would show up.
        view.StripAbove = true;
        view.EditMode = true;
        // The mode added the strip's band and the view grew by it: re-arrange
        // at the NEW size, or the render is cropped and every outline is read
        // off the old geometry.
        LayoutPass(view);

        view.PreviewDrag(block: 0, from: 0, to: 2);
        view.UpdateLayout();

        Save(view, Path.Combine(dir, "edit-mode-drag-name-above.png"));
    }

    /// Line mode with the service fine and with it degraded. The first is the
    /// case the design argued about: the line states the state instead of
    /// falling silent, because a silent line reads as a lost dial.
    private static void RenderStatusLine(
        string dir,
        string name,
        ServiceStatus status,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        var view = new WidgetRootView();
        view.ApplyLayout(WidgetLayout.Default, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(
            AccountRow.ForAll(accounts, snapshots, null, now),
            status, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        Save(view, Path.Combine(dir, name));
    }

    /// Edit mode surviving a rebuild that MOVES the cells.
    ///
    /// `edit-mode-after-swap.png` cannot discriminate a stale outline cache
    /// from a correct rebuild: a swap permutes cell CONTENTS while the cell
    /// rectangles stay uniform. Switching the status mode to Cell adds a fifth
    /// cell, which reshapes the block — so the outlines here MUST be five, one
    /// per cell, with none left over the four-cell geometry. (This case was
    /// named in review and not built at the time.)
    private static void RenderStatusCellRebuild(
        string dir,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        var rows = AccountRow.ForAll(accounts, snapshots, null, now);

        var view = new WidgetRootView();
        view.ApplyLayout(WidgetLayout.Default, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(rows, ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);
        LayoutPass(view);

        view.EditMode = true;
        LayoutPass(view);

        // The same pair the Status button produces: Sanitize with the new mode
        // is what puts the cell in the order, and ApplyLayout rebuilds around
        // it. SetContent runs again because BuildGrid discarded the visuals.
        var cellMode = WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Cell, ModelDial.Shown);
        view.ApplyLayout(cellMode, StatusMode.Cell, ModelDial.Shown, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(rows, ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);
        LayoutPass(view);

        Save(view, Path.Combine(dir, "edit-mode-status-cell.png"));
    }

    /// A layout in daily use, at the design size and at the side it runs at
    /// (207): three accounts in a row, the dials of each in a column, the name
    /// above them, the service state on the line.
    ///
    /// This is the case the vertical padding is judged on, and it is judged by
    /// counting pixel rows in the PNG rather than by looking: the gap from the
    /// panel's top edge to the name row must equal the gap from the last dial
    /// to the status text, and the gap under the text must equal the gap over
    /// it. "The top and bottom padding are not visually identical; service
    /// operational is stuck right at the bottom" (2026-09-04).
    private static void RenderPadding(
        string dir,
        string name,
        double side,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        var layout = WidgetLayout.Default with
        {
            PanelFlow = LayoutFlow.Row,
            Block = BlockLayout.Default with { Flow = LayoutFlow.Column, Name = NamePlacement.Above },
        };

        var view = new WidgetRootView();
        view.ApplyLayout(layout, StatusMode.Line, ModelDial.Shown, PlanLine.Hidden, accounts.Count, side);
        view.SetContent(
            AccountRow.ForAll(accounts, snapshots, null, now),
            ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        Save(view, Path.Combine(dir, name));
    }

    /// The panel with the model dial switched off. Three cells where the
    /// default has four, so the block is a 2x2 grid with a hole — the name, 5H
    /// and 7D, and no third dial. What the eye checks: no empty ring where OPUS
    /// was, and the panel the same WIDTH as the default (the grid shape does not
    /// change, only what fills it).
    private static void RenderModelHidden(
        string dir,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        // Through Sanitize, not a hand-built order: this is exactly what the
        // toolbar button saves.
        var layout = WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Line, ModelDial.Hidden);

        var view = new WidgetRootView();
        view.ApplyLayout(layout, StatusMode.Line, ModelDial.Hidden, PlanLine.Hidden, accounts.Count, 228);
        view.SetContent(
            AccountRow.ForAll(accounts, snapshots, null, now),
            ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

        LayoutPass(view);

        Save(view, Path.Combine(dir, "model-hidden.png"));
    }

    /// The plan under the account name, in all three placements that draw a
    /// name — "the account text on top, with the subscription tier below it"
    /// (the user, 2026-09-04). Two accounts with the fields the live bodies carried, so the
    /// lines read `Max 20x` and `Team` rather than a placeholder.
    ///
    /// Each is rendered TWICE, `<stem>-off` and `<stem>-on`, and the pair is the
    /// measurement: off is the panel that ships today, and the two files must
    /// differ in HEIGHT by exactly one caption line per block row for Above and
    /// Below, and not at all for Cell — where the name already owns a whole dial
    /// square. What the eye adds: the plan sits under its own account and not
    /// under the neighbour's, the dials have not moved into the text, and the
    /// second line is dimmer and smaller than the name rather than a second name.
    private static void RenderPlanLine(
        string dir,
        string stem,
        NamePlacement placement,
        IReadOnlyList<AccountProfile> accounts,
        IReadOnlyDictionary<string, UsageSnapshot?> snapshots,
        DateTimeOffset now)
    {
        Render(stem + "-off.png", PlanLine.Hidden, editMode: false);
        Render(stem + "-on.png", PlanLine.Shown, editMode: false);

        // The third file is the one case the pair above cannot show. Entering
        // edit mode re-measures the panel, and every re-measure is a chance to
        // drop a setting that was only threaded through the FIRST call: a
        // reserve lost here would put the dials over the plan line while the
        // strip is up and nowhere else. It must be the `-on` panel plus the
        // strip's band, and nothing else moved.
        Render(stem + "-on-edit.png", PlanLine.Shown, editMode: true);

        void Render(string name, PlanLine plan, bool editMode)
        {
            var layout = WidgetLayout.Default with
            {
                PanelFlow = LayoutFlow.Row,
                Block = BlockLayout.Default with { Flow = LayoutFlow.Column, Name = placement },
            };

            var view = new WidgetRootView();
            view.ApplyLayout(layout, StatusMode.Line, ModelDial.Shown, plan, accounts.Count, 228);
            view.SetContent(
                AccountRow.ForAll(accounts, snapshots, null, now),
                ServiceStatus.Operational, dimmed: false, statusLine: null, notice: null);

            LayoutPass(view);

            if (editMode)
            {
                view.EditMode = true;
                // The mode added the strip's band and the view grew by it:
                // re-arrange at the NEW size, or the render is cropped.
                LayoutPass(view);
            }

            Save(view, Path.Combine(dir, name));
        }
    }

    /// The band is drawn on the taskbar, which hides itself whenever the
    /// taskbar is covered — so it needs the same offscreen treatment as the
    /// panel. The dark plate stands in for a taskbar: the text is white with a
    /// shadow and would be invisible on a transparent PNG.
    /// Both band views, each on a strip the colour and height of the taskbar
    /// it docks to, and at the end of the taskbar it would sit at: the three
    /// metrics at the left corner, the per-account groups beside the tray.
    /// A strip the full width of the picture is what makes it read as a
    /// taskbar rather than as a floating black box.
    private static void RenderBand(string dir, IReadOnlyList<AccountRow> rows)
    {
        SaveBand(dir, "band-metrics.png", BandText.MetricEntries(rows[0]), HorizontalAlignment.Left);
        SaveBand(dir, "band.png", BandText.Entries(rows), HorizontalAlignment.Right);
    }

    private static void SaveBand(
        string dir, string name, IReadOnlyList<BandEntry> entries, HorizontalAlignment align)
    {
        const double TaskbarHeight = 40;
        const double StripWidth = 560;

        var content = new TaskbarBandContent
        {
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.SetMetrics(entries, ThemeKind.Dark);
        content.Measure(new Size(double.PositiveInfinity, TaskbarHeight));

        var plate = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
            Child = content,
            Width = StripWidth,
            Height = TaskbarHeight,
            // The clearance the figures keep from the end of a real taskbar.
            Padding = new Thickness(18, 0, 18, 0),
        };

        LayoutPass(plate);

        Save(plate, Path.Combine(dir, name));
    }

    /// Measure, arrange, settle — at the element's own declared size, which
    /// every case here sets before asking for one.
    private static void LayoutPass(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
    }

    private static void Save(FrameworkElement element, string path)
    {
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(element.Width), (int)Math.Ceiling(element.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static UsageSnapshot Snap(double five, double seven, double model, DateTimeOffset now) =>
        new(new Dictionary<string, UsageBucket>
        {
            ["five_hour"] = new(five, now.AddMinutes(72)),
            ["seven_day"] = new(seven, now.AddDays(3)),
            ["seven_day_opus"] = new(model, now.AddDays(3)),
        });
}
