namespace ClaudeUsageWidget.Core.Tests;

public class PanelViewTests
{
    [Fact]
    public void ClassicIsATwoByTwoGridWithoutNameAndWithStatusDial()
    {
        var layout = PanelViews.LayoutFor(PanelView.Classic);

        Assert.Equal(LayoutFlow.Grid, layout.Block.Flow);
        Assert.Equal(NamePlacement.Hidden, layout.Block.Name);
        Assert.Equal(
            [BlockItem.FiveHour, BlockItem.SevenDay, BlockItem.Model, BlockItem.Status],
            layout.Block.Order);
        Assert.Equal(StatusMode.Cell, PanelViews.StatusFor(PanelView.Classic));
    }

    [Fact]
    public void AccountsIsTheDefaultLayoutWithAStatusLine()
    {
        Assert.Equal(WidgetLayout.Default, PanelViews.LayoutFor(PanelView.Accounts));
        Assert.Equal(StatusMode.Line, PanelViews.StatusFor(PanelView.Accounts));
    }

    [Theory]
    [InlineData(0, PanelView.Classic)]
    [InlineData(1, PanelView.Classic)]
    [InlineData(2, PanelView.Accounts)]
    [InlineData(4, PanelView.Accounts)]
    public void DefaultFollowsTheAccountCount(int accounts, PanelView expected) =>
        Assert.Equal(expected, PanelViews.DefaultFor(accounts));

    [Fact]
    public void CurrentRecognisesBothViewsAndNothingInBetween()
    {
        Assert.Equal(PanelView.Classic,
            PanelViews.Current(PanelViews.LayoutFor(PanelView.Classic), StatusMode.Cell));
        Assert.Equal(PanelView.Accounts,
            PanelViews.Current(WidgetLayout.Default, StatusMode.Line));
        Assert.Null(PanelViews.Current(WidgetLayout.Default, StatusMode.Cell));
        Assert.Null(PanelViews.Current(PanelViews.LayoutFor(PanelView.Classic), StatusMode.Line));
    }

    [Fact]
    public void AFileWithoutLayoutResolvesToTheViewForItsAccountCount()
    {
        var data = new WidgetSettingsData();

        var one = LayoutResolution.Resolve(data, 1);
        var two = LayoutResolution.Resolve(data, 2);

        Assert.Equal(PanelView.Classic, PanelViews.Current(one.Layout, one.Status));
        Assert.Equal(PanelView.Accounts, PanelViews.Current(two.Layout, two.Status));
    }

    [Fact]
    public void ASavedLayoutIsKeptWhateverTheAccountCount()
    {
        var data = new WidgetSettingsData { Layout = WidgetLayout.Default, StatusMode = StatusMode.Line };

        var resolved = LayoutResolution.Resolve(data, 1);

        Assert.Equal(PanelView.Accounts, PanelViews.Current(resolved.Layout, resolved.Status));
    }

    [Fact]
    public void ResolveHonoursTheModelDialSwitch()
    {
        var data = new WidgetSettingsData { ModelDial = ModelDial.Hidden };

        var resolved = LayoutResolution.Resolve(data, 1);

        Assert.DoesNotContain(BlockItem.Model, resolved.Layout.Block.Order);
        Assert.Contains(BlockItem.Status, resolved.Layout.Block.Order);
    }

    [Fact]
    public void ApplyWritesBothFieldsAndKeepsTheOtherSwitches()
    {
        var data = new WidgetSettingsData { ModelDial = ModelDial.Hidden, PlanLine = PlanLine.Shown };

        var classic = LayoutResolution.Apply(data, PanelView.Classic);
        var rows = LayoutResolution.Apply(classic, PanelView.Accounts);

        Assert.Equal(StatusMode.Cell, classic.StatusMode);
        Assert.Equal(NamePlacement.Hidden, classic.Layout!.Block.Name);
        Assert.DoesNotContain(BlockItem.Model, classic.Layout.Block.Order);
        Assert.Equal(PlanLine.Shown, classic.PlanLine);

        Assert.Equal(StatusMode.Line, rows.StatusMode);
        Assert.Equal(NamePlacement.Cell, rows.Layout!.Block.Name);
        Assert.DoesNotContain(BlockItem.Status, rows.Layout.Block.Order);
    }

    [Fact]
    public void ClassicPanelHasNoCaptionBandUnderTheGrid()
    {
        var classic = PanelMetrics.For(PanelViews.LayoutFor(PanelView.Classic), 1, WidgetSettings.DefaultSide);
        var rows = PanelMetrics.For(WidgetLayout.Default, 1, WidgetSettings.DefaultSide);

        Assert.Equal(classic.Padding, classic.StatusBand, 6);
        Assert.True(rows.StatusBand > classic.StatusBand);
        // Padding, one block of two dial rows, padding — the pre-account square.
        Assert.Equal(classic.Padding * 2 + classic.Block.Height, classic.Height, 6);
    }
}
