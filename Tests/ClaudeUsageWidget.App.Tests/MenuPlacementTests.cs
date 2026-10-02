using System.Drawing;
using ClaudeUsageWidget.App.Tray;

namespace ClaudeUsageWidget.App.Tests;

/// <summary>
/// Where the tray menu ends up once it is open. NotifyIcon opens it with its
/// bottom edge AT the cursor — on the taskbar — and constrains it to the
/// screen, not the work area, so the lower rows land on the taskbar strip.
/// </summary>
public sealed class MenuPlacementTests
{
    // A 1920x1080 screen with a 48 px taskbar at the bottom.
    private static readonly Rectangle BottomWorkArea = new(0, 0, 1920, 1032);

    [Fact]
    public void AMenuHangingOverABottomTaskbarIsLiftedOntoItsTopEdge()
    {
        // Opened by a right-click at (1700, 1060): bottom-right at the cursor.
        var opened = new Rectangle(1700 - 260, 1060 - 600, 260, 600);

        var placed = MenuPlacement.Within(opened, BottomWorkArea);

        Assert.Equal(new Rectangle(1440, 1032 - 600, 260, 600), placed);
    }

    [Fact]
    public void AMenuAlreadyInsideTheWorkAreaStaysWhereItIs()
    {
        var opened = new Rectangle(400, 200, 260, 600);

        Assert.Equal(opened, MenuPlacement.Within(opened, BottomWorkArea));
    }

    [Fact]
    public void ATopTaskbarPushesTheMenuDownBelowIt()
    {
        var workArea = new Rectangle(0, 48, 1920, 1032);
        var opened = new Rectangle(1440, 20, 260, 600);

        Assert.Equal(new Rectangle(1440, 48, 260, 600), MenuPlacement.Within(opened, workArea));
    }

    [Fact]
    public void ASideTaskbarPushesTheMenuOffIt()
    {
        var rightTaskbar = new Rectangle(0, 0, 1872, 1080);
        var opened = new Rectangle(1700, 300, 260, 600);

        Assert.Equal(new Rectangle(1872 - 260, 300, 260, 600), MenuPlacement.Within(opened, rightTaskbar));
    }

    [Fact]
    public void AMenuTallerThanTheWorkAreaKeepsItsTopRowsOnScreen()
    {
        var opened = new Rectangle(1440, 500, 260, 1200);

        Assert.Equal(new Rectangle(1440, 0, 260, 1200), MenuPlacement.Within(opened, BottomWorkArea));
    }

    [Fact]
    public void ASecondMonitorWorkAreaIsHonouredInItsOwnCoordinates()
    {
        // Monitor to the left of the primary, taskbar at its bottom.
        var leftMonitor = new Rectangle(-1920, 0, 1920, 1032);
        var opened = new Rectangle(-300, 480, 260, 600);

        Assert.Equal(new Rectangle(-300, 432, 260, 600), MenuPlacement.Within(opened, leftMonitor));
    }
}
