// A WPF project's implicit usings do not carry System.IO, and System.Windows.Shapes
// has a Path of its own — so the file system is named explicitly here.
using System.IO;
using ClaudeUsageWidget.App.Windows;
using ClaudeUsageWidget.Core;
using Path = System.IO.Path;
using Screen = System.Windows.Forms.Screen;

namespace ClaudeUsageWidget.App.Tests;

/// <summary>
/// The panel's geometry across a change in the number of accounts — the one
/// moment the window's size changes without anybody dragging it.
/// </summary>
public sealed class DesktopWidgetWindowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    /// A pixel of slack: Left and Width are doubles built from a scale factor,
    /// and the assertion is about the panel being on the screen, not about the
    /// last bit of a division.
    private const double Tolerance = 0.5;

    /// The owner's live layout on 2026-09-08, the one the bug was reported
    /// against: accounts side by side, three dials stacked under each name.
    /// Positions are left out — the test computes them from the real screen.
    private const string SettingsJson = """
    {
      "WidgetVisible": true,
      "WidgetSide": 222,
      "TaskbarBandEnabled": false,
      "StatusMode": "Line",
      "ModelDial": "Shown",
      "PlanLine": "Hidden",
      "Layout": {
        "PanelFlow": "Row",
        "Block": {
          "Flow": "Column",
          "Name": "Above",
          "Order": [ "Name", "FiveHour", "SevenDay", "Model" ],
          "LaidOut": [ "FiveHour", "SevenDay", "Model" ]
        }
      },
      "Accounts": [
        { "Id": "a1", "DisplayName": "personal", "OrganizationId": "org-1", "RetryPausedUntil": null, "ConsecutiveRateLimits": 0 },
        { "Id": "a2", "DisplayName": "work",     "OrganizationId": "org-2", "RetryPausedUntil": null, "ConsecutiveRateLimits": 0 },
        { "Id": "a3", "DisplayName": "low",      "OrganizationId": "org-3", "RetryPausedUntil": null, "ConsecutiveRateLimits": 0 },
        { "Id": "a4", "DisplayName": "shared",   "OrganizationId": "org-4", "RetryPausedUntil": null, "ConsecutiveRateLimits": 0 }
      ]
    }
    """;

    /// <summary>
    /// Adding an account grows the panel to the right. Parked against the right
    /// edge — where a widget lives — that growth has to be answered by moving
    /// the panel left, or the new account is drawn off the screen.
    ///
    /// The regression this pins: ClampToScreen used to run AFTER the repaint,
    /// and the repaint threw, because the frame still cached from the previous
    /// render carried one row fewer than the grid had just been rebuilt for.
    /// The window kept its old Left with its new Width, and the throw also took
    /// out the rest of the add — the login window was never opened.
    /// </summary>
    [Fact]
    public void AddingAnAccountKeepsThePanelInsideTheWorkArea() => Sta.Run(() =>
    {
        var work = PrimaryWorkArea();
        var store = NewStore();

        var four = store.Load();
        // Ten pixels of the panel on the screen: the constructor's clamp then
        // parks it flush against the right edge, which is the position the bug
        // was reported from.
        store.Save(four with { WidgetX = work.Right - 10, WidgetY = work.Top + 100 });

        var window = new DesktopWidgetWindow(store);
        window.Render(Rows(four.Accounts), new UsageState.Loading(), ServiceStatus.Operational, null);

        // The setup itself, asserted rather than assumed: a panel that was not
        // actually against the edge could not show the bug. Skipped on a desktop
        // too narrow to hold the panel at all — a CI runner's virtual display —
        // where the clamp pins the left edge and the overflow has no fix.
        var stageable = window.Width <= work.Width;
        if (stageable) Assert.Equal(work.Right, window.Left + window.Width, Tolerance);
        var widthBefore = window.Width;

        // App.OnAccountAddRequested: the account lands in settings first, then
        // the window is told to rebuild for it — with the four-row frame from
        // the Render above still cached.
        store.Save(store.Load() with
        {
            Accounts = [.. four.Accounts, new AccountProfile("a5", "New account", null, null, 0)],
        });
        window.RebuildLayout(5);

        // Vacuous otherwise: a RebuildLayout that did nothing would satisfy
        // every bound below.
        Assert.True(window.Width > widthBefore,
            $"the panel did not grow for the fifth account: {widthBefore} -> {window.Width}");
        // The left edge holds on any screen — the clamp pins it there even when
        // the panel is wider than the desktop. The right edge is only a promise
        // the clamp can keep when the panel fits.
        Assert.True(window.Left >= work.Left - Tolerance,
            $"the panel hangs {work.Left - window.Left:F1} px past the left edge of the work area");
        if (window.Width <= work.Width)
            Assert.True(window.Left + window.Width <= work.Right + Tolerance,
                $"the panel hangs {window.Left + window.Width - work.Right:F1} px past the right edge of the work area");
        else
            Assert.True(stageable is false, "a panel that grew past the whole desktop cannot be clamped into it");
    });

    /// <summary>
    /// The same stale frame in the other direction: removing an account shrinks
    /// the grid under a cached frame that is now too long. Nothing about the
    /// geometry is at risk here — the throw is.
    /// </summary>
    [Fact]
    public void RemovingAnAccountRebuildsWithoutThrowing() => Sta.Run(() =>
    {
        var work = PrimaryWorkArea();
        var store = NewStore();

        var four = store.Load();
        store.Save(four with { WidgetX = work.Left + 100, WidgetY = work.Top + 100 });

        var window = new DesktopWidgetWindow(store);
        window.Render(Rows(four.Accounts), new UsageState.Loading(), ServiceStatus.Operational, null);
        var widthBefore = window.Width;

        store.Save(store.Load() with { Accounts = four.Accounts.Take(3).ToList() });
        window.RebuildLayout(3);

        Assert.True(window.Width < widthBefore,
            $"the panel did not shrink after the removal: {widthBefore} -> {window.Width}");
    });

    /// The work area the window will clamp itself into. Named rather than
    /// `Screen.PrimaryScreen!`: a null primary screen is a broken environment,
    /// and saying so beats a NullReferenceException from inside a test body.
    private static System.Drawing.Rectangle PrimaryWorkArea() =>
        (Screen.PrimaryScreen ?? throw new InvalidOperationException(
            "no primary screen — these tests need a desktop session")).WorkingArea;

    private static SettingsStore NewStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cuw-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, SettingsJson);
        return new SettingsStore(path);
    }

    /// One row per account with no figures behind it — the panel's geometry is
    /// reserved from the layout, never from whether a poll has answered.
    private static IReadOnlyList<AccountRow> Rows(IReadOnlyList<AccountProfile> accounts) =>
        AccountRow.ForAll(
            accounts,
            accounts.ToDictionary(a => a.Id, _ => (UsageSnapshot?)null),
            null,
            Now);
}
