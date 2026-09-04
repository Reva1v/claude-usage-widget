namespace ClaudeUsageWidget.Core.Tests;

public class FlowGridTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    public void GridIsTwoColumnsSoTheThirdAndFourthShareAShape(int count, int rows, int columns) =>
        Assert.Equal((rows, columns), FlowGrid.Shape(LayoutFlow.Grid, count));

    [Fact]
    public void RowIsOneRow() => Assert.Equal((1, 4), FlowGrid.Shape(LayoutFlow.Row, 4));

    [Fact]
    public void ColumnIsOneColumn() => Assert.Equal((4, 1), FlowGrid.Shape(LayoutFlow.Column, 4));

    [Fact]
    public void NothingToLayOutIsNoShape() => Assert.Equal((0, 0), FlowGrid.Shape(LayoutFlow.Grid, 0));
}

public class GridTracksTests
{
    [Fact]
    public void NothingToLayOutIsNoTracks() => Assert.Empty(GridTracks.Build(0, 68, 10));

    [Fact]
    public void OneItemIsOneTrackWithNoGap() => Assert.Equal([68d], GridTracks.Build(1, 68, 10));

    [Fact]
    public void TheGapIsItsOwnTrackBetweenItems() =>
        Assert.Equal([68d, 10d, 68d], GridTracks.Build(2, 68, 10));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheTracksSumToWhatTheMetricsMeasured(int count)
    {
        // The panel window is sized from BlockMetrics/PanelMetrics, so a grid
        // that adds up to anything else either clips or leaves dead air. The
        // trailing gap in `size + gap` tracks did both.
        var expected = count * 68 + (count - 1) * 10;

        Assert.Equal(expected, GridTracks.Build(count, 68, 10).Sum(), 6);
    }

    [Fact]
    public void ItemNSitsOnEveryOtherTrack()
    {
        Assert.Equal(0, GridTracks.TrackOf(0));
        Assert.Equal(2, GridTracks.TrackOf(1));
        Assert.Equal(6, GridTracks.TrackOf(3));
    }
}

public class LayoutSettingsTests
{
    [Fact]
    public void NullFallsBackToTheDefault() =>
        Assert.Equal(WidgetLayout.Default, WidgetLayout.Sanitize(null, StatusMode.Line, ModelDial.Shown));

    [Fact]
    public void ARepeatedItemIsNotAPermutationSoTheOrderIsDiscarded()
    {
        var raw = WidgetLayout.Default with
        {
            Block = BlockLayout.Default with
            {
                Order = [BlockItem.FiveHour, BlockItem.FiveHour, BlockItem.SevenDay, BlockItem.Model],
            },
        };

        Assert.Equal(BlockLayout.DefaultOrder, WidgetLayout.Sanitize(raw, StatusMode.Line, ModelDial.Shown).Block.Order);
    }

    [Fact]
    public void AnOrderWithoutTheSevenDayDialIsDiscarded()
    {
        var raw = WidgetLayout.Default with
        {
            Block = BlockLayout.Default with { Order = [BlockItem.FiveHour, BlockItem.Name] },
        };

        Assert.Equal(BlockLayout.DefaultOrder, WidgetLayout.Sanitize(raw, StatusMode.Line, ModelDial.Shown).Block.Order);
    }

    [Fact]
    public void AGenuinePermutationSurvives()
    {
        IReadOnlyList<BlockItem> order =
            [BlockItem.FiveHour, BlockItem.Name, BlockItem.Model, BlockItem.SevenDay];
        var raw = WidgetLayout.Default with { Block = BlockLayout.Default with { Order = order } };

        Assert.Equal(order, WidgetLayout.Sanitize(raw, StatusMode.Line, ModelDial.Shown).Block.Order);
    }

    [Fact]
    public void TheStatusDialIsAnOptionalFifthCell()
    {
        // It used to be a fixed cell, then a status LINE that says nothing while
        // the service is fine — which reads as "the dial is gone". As a cell it
        // is visible whenever it is asked for.
        var order = BlockLayout.DefaultOrder.Append(BlockItem.Status).ToList();
        var raw = WidgetLayout.Default with { Block = BlockLayout.Default with { Order = order } };

        Assert.Equal(order, WidgetLayout.Sanitize(raw, StatusMode.Cell, ModelDial.Shown).Block.Order);
        Assert.Equal(5, WidgetLayout.Sanitize(raw, StatusMode.Cell, ModelDial.Shown).Block.LaidOut.Count);
    }

    [Fact]
    public void AnOrderMissingADialIsDiscardedEvenThoughItsItemsAreDistinct()
    {
        // Distinctness alone is not the invariant: the two window dials are what
        // the widget is for, and a hand-edited file must not be able to drop
        // one. (The model dial is optional and absent here on purpose — what
        // makes this order corrupt is the missing 7D.)
        var raw = WidgetLayout.Default with
        {
            Block = BlockLayout.Default with { Order = [BlockItem.Name, BlockItem.FiveHour, BlockItem.Status] },
        };

        Assert.Equal(BlockLayout.DefaultOrder, WidgetLayout.Sanitize(raw, StatusMode.Line, ModelDial.Shown).Block.Order);
    }

    [Fact]
    public void TogglingAnItemAddsItAtTheEndAndRemovesItWhereverItSits()
    {
        var withStatus = BlockLayout.Default.Toggle(BlockItem.Status);
        Assert.Equal(BlockItem.Status, withStatus.Order[^1]);

        Assert.DoesNotContain(BlockItem.Status, withStatus.Toggle(BlockItem.Status).Order);
        Assert.Equal(BlockLayout.DefaultOrder, withStatus.Toggle(BlockItem.Status).Order);
    }

    [Fact]
    public void SanitizeKeepsTheFlowsItWasGiven()
    {
        var raw = new WidgetLayout(LayoutFlow.Column, BlockLayout.Default with { Flow = LayoutFlow.Row });
        var clean = WidgetLayout.Sanitize(raw, StatusMode.Line, ModelDial.Shown);

        Assert.Equal(LayoutFlow.Column, clean.PanelFlow);
        Assert.Equal(LayoutFlow.Row, clean.Block.Flow);
    }

    [Fact]
    public void TheNameIsLaidOutOnlyWhenItIsACell()
    {
        Assert.Equal(4, BlockLayout.Default.LaidOut.Count);
        Assert.Equal(3, (BlockLayout.Default with { Name = NamePlacement.Above }).LaidOut.Count);
        Assert.DoesNotContain(
            BlockItem.Name,
            (BlockLayout.Default with { Name = NamePlacement.Hidden }).LaidOut);
    }

    [Fact]
    public void TheOrderOfTheDialsSurvivesDroppingTheName()
    {
        var layout = BlockLayout.Default with
        {
            Name = NamePlacement.Above,
            Order = [BlockItem.Model, BlockItem.Name, BlockItem.SevenDay, BlockItem.FiveHour],
        };

        Assert.Equal([BlockItem.Model, BlockItem.SevenDay, BlockItem.FiveHour], layout.LaidOut);
    }

    [Fact]
    public void SwappingTwoCellsExchangesThemInTheOrder()
    {
        // Default order is [Name, FiveHour, SevenDay, Model] and the name is a
        // cell, so the laid-out positions and the order positions coincide here.
        var swapped = BlockLayout.Default.SwapCells(0, 3);

        Assert.Equal(
            [BlockItem.Model, BlockItem.FiveHour, BlockItem.SevenDay, BlockItem.Name],
            swapped.Order);
    }

    [Fact]
    public void ASwapIsSymmetricAndASwapWithItselfChangesNothing()
    {
        // Compare the ORDERS, not the records: BlockLayout is a record whose
        // Order field is an IReadOnlyList, and record equality compares that
        // field by reference — two separately built lists holding the same
        // items are never equal. `Assert.Equal` on the lists themselves is
        // xUnit's collection comparison, which is what this means to assert.
        Assert.Equal(
            BlockLayout.Default.SwapCells(1, 2).Order,
            BlockLayout.Default.SwapCells(2, 1).Order);

        // A swap with itself returns `this`, so the records really are equal.
        Assert.Equal(BlockLayout.Default, BlockLayout.Default.SwapCells(2, 2));
    }

    [Fact]
    public void APositionOutsideTheLaidOutCellsChangesNothing()
    {
        // The hit test can only produce a real cell, but a layout with the name
        // Above lays out three cells while Order still holds four items, so an
        // index that is valid for one is not automatically valid for the other.
        var above = BlockLayout.Default with { Name = NamePlacement.Above };

        Assert.Equal(above, above.SwapCells(0, 3));
        Assert.Equal(above, above.SwapCells(-1, 1));
    }

    [Fact]
    public void SwappingUsesLaidOutPositionsNotOrderPositions()
    {
        // With the name Above, LaidOut is [FiveHour, SevenDay, Model] while
        // Order is [Name, FiveHour, SevenDay, Model]. Swapping the first and
        // last CELLS must exchange FiveHour and Model and leave Name where it
        // is — index arithmetic would have swapped Name and SevenDay.
        var above = BlockLayout.Default with { Name = NamePlacement.Above };

        var swapped = above.SwapCells(0, 2);

        Assert.Equal(
            [BlockItem.Name, BlockItem.Model, BlockItem.SevenDay, BlockItem.FiveHour],
            swapped.Order);
        Assert.Equal([BlockItem.Model, BlockItem.SevenDay, BlockItem.FiveHour], swapped.LaidOut);
    }

    [Fact]
    public void EverySwapSurvivesSanitize()
    {
        // The editor writes through the same save path as the tray, so a swap
        // that Sanitize rejects would silently reset the layout to default.
        var withStatus = BlockLayout.Default.Toggle(BlockItem.Status);

        for (var a = 0; a < withStatus.LaidOut.Count; a++)
        for (var b = 0; b < withStatus.LaidOut.Count; b++)
        {
            var swapped = withStatus.SwapCells(a, b);
            var layout = WidgetLayout.Default with { Block = swapped };

            Assert.Equal(swapped.Order, WidgetLayout.Sanitize(layout, StatusMode.Cell, ModelDial.Shown).Block.Order);
        }
    }

    [Fact]
    public void CellModeGivesTheOrderAStatusCellAndLineModeTakesItAway()
    {
        // One decision, one switch. A user with StatusMode = Line and Status
        // still in Order would see the service state twice.
        var cell = WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Cell, ModelDial.Shown);
        Assert.Equal(BlockItem.Status, cell.Block.Order[^1]);
        Assert.Equal(5, cell.Block.LaidOut.Count);

        var back = WidgetLayout.Sanitize(cell, StatusMode.Line, ModelDial.Shown);
        Assert.Equal(BlockLayout.DefaultOrder, back.Block.Order);
    }

    [Fact]
    public void TheModeIsAppliedToTheFallbackOrderToo()
    {
        // Structure first, mode second: a corrupt order falls back to the
        // default and the default then gains its status cell. The other way
        // round hands back a default with no cell while the mode says Cell.
        var raw = WidgetLayout.Default with
        {
            Block = BlockLayout.Default with { Order = [BlockItem.FiveHour, BlockItem.FiveHour] },
        };

        Assert.Equal(
            BlockLayout.DefaultOrder.Append(BlockItem.Status).ToList(),
            WidgetLayout.Sanitize(raw, StatusMode.Cell, ModelDial.Shown).Block.Order);
    }

    [Fact]
    public void SanitizingTwiceInTheSameModeChangesNothing()
    {
        // Every save runs through Sanitize, so a rule that is not idempotent
        // would grow a second status cell on the second save.
        var once = WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Cell, ModelDial.Shown);

        Assert.Equal(once.Block.Order, WidgetLayout.Sanitize(once, StatusMode.Cell, ModelDial.Shown).Block.Order);
    }

    [Fact]
    public void HidingTheModelDialTakesTheCellOutAndShowingItPutsItBack()
    {
        // The work account has no per-model bucket worth a square, and the
        // user wanted the option rather than the removal (2026-09-04). The
        // percentage is not lost with the cell: the tray tooltip and the
        // taskbar band read DialModel.All, which never consulted the layout.
        var hidden = WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Line, ModelDial.Hidden);
        Assert.Equal([BlockItem.Name, BlockItem.FiveHour, BlockItem.SevenDay], hidden.Block.Order);

        var back = WidgetLayout.Sanitize(hidden, StatusMode.Line, ModelDial.Shown);
        Assert.Equal(BlockLayout.DefaultOrder, back.Block.Order);
    }

    [Fact]
    public void AnOrderWithoutTheModelDialIsNoLongerCorrupt()
    {
        // Model is at most once now, like Status — an order that lacks it is
        // exactly what Hidden writes, and rejecting it would reset the layout
        // on the next read.
        IReadOnlyList<BlockItem> order = [BlockItem.FiveHour, BlockItem.Name, BlockItem.SevenDay];
        var raw = WidgetLayout.Default with { Block = BlockLayout.Default with { Order = order } };

        Assert.Equal(order, WidgetLayout.Sanitize(raw, StatusMode.Line, ModelDial.Hidden).Block.Order);
    }

    [Fact]
    public void SanitizingTwiceWithTheModelDialHiddenChangesNothing()
    {
        var once = WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Line, ModelDial.Hidden);

        Assert.Equal(
            once.Block.Order,
            WidgetLayout.Sanitize(once, StatusMode.Line, ModelDial.Hidden).Block.Order);
    }

    [Fact]
    public void ASwapOfTheModelCellSurvivesSanitizeWhileTheDialIsShown()
    {
        // Default order is [Name, FiveHour, SevenDay, Model]: 0 and 3 exchange
        // the name and the model dial, and Shown must not undo it just because
        // it re-checks that the cell is there.
        var swapped = BlockLayout.Default.SwapCells(0, 3);
        var layout = WidgetLayout.Default with { Block = swapped };

        Assert.Equal(
            swapped.Order,
            WidgetLayout.Sanitize(layout, StatusMode.Line, ModelDial.Shown).Block.Order);
    }

    [Fact]
    public void TheModelDialIsAppliedToTheFallbackOrderToo()
    {
        // Structure first, then both optional cells — the same order the status
        // mode follows, so a corrupt file lands on the default and the default
        // then loses the dial the setting says is off.
        var raw = WidgetLayout.Default with
        {
            Block = BlockLayout.Default with { Order = [BlockItem.FiveHour, BlockItem.FiveHour] },
        };

        Assert.Equal(
            [BlockItem.Name, BlockItem.FiveHour, BlockItem.SevenDay],
            WidgetLayout.Sanitize(raw, StatusMode.Line, ModelDial.Hidden).Block.Order);
    }

    [Fact]
    public void ASwapOfTheStatusCellStillSurvivesSanitize()
    {
        // The editor writes through the same path, and Cell mode must not undo
        // a swap just because it re-checks that the cell is present.
        var swapped = BlockLayout.Default.Toggle(BlockItem.Status).SwapCells(0, 4);
        var layout = WidgetLayout.Default with { Block = swapped };

        Assert.Equal(swapped.Order, WidgetLayout.Sanitize(layout, StatusMode.Cell, ModelDial.Shown).Block.Order);
    }
}

public class StatusModeTests
{
    [Fact]
    public void ASavedModeWins()
    {
        var withCell = WidgetLayout.Default with { Block = BlockLayout.Default.Toggle(BlockItem.Status) };

        Assert.Equal(StatusMode.Line, StatusModes.Resolve(StatusMode.Line, withCell));
        Assert.Equal(StatusMode.Cell, StatusModes.Resolve(StatusMode.Cell, WidgetLayout.Default));
    }

    [Fact]
    public void AFileWithoutTheSettingReadsTheModeOffTheOrder()
    {
        // The setting is new; the files are not. Defaulting to Line for
        // everybody would delete the status cell of anyone who had switched it
        // on from the tray item this design removes.
        var withCell = WidgetLayout.Default with { Block = BlockLayout.Default.Toggle(BlockItem.Status) };

        Assert.Equal(StatusMode.Cell, StatusModes.Resolve(null, withCell));
        Assert.Equal(StatusMode.Line, StatusModes.Resolve(null, WidgetLayout.Default));
        Assert.Equal(StatusMode.Line, StatusModes.Resolve(null, null));
    }

    [Fact]
    public void AnOrderThatDeserialisedToNullIsNotACrash()
    {
        // Sanitize's IsUsable already anticipates this file; Resolve runs
        // BEFORE Sanitize on every read, so it has to survive the same one.
        var broken = WidgetLayout.Default with { Block = BlockLayout.Default with { Order = null! } };

        Assert.Equal(StatusMode.Line, StatusModes.Resolve(null, broken));
    }
}

public class LayoutCycleTests
{
    [Fact]
    public void FlowsWalkGridRowColumnAndWrap()
    {
        Assert.Equal(LayoutFlow.Row, LayoutCycle.Next(LayoutFlow.Grid));
        Assert.Equal(LayoutFlow.Column, LayoutCycle.Next(LayoutFlow.Row));
        Assert.Equal(LayoutFlow.Grid, LayoutCycle.Next(LayoutFlow.Column));
    }

    [Fact]
    public void TheNameWalksAllFourPlacements()
    {
        Assert.Equal(NamePlacement.Above, LayoutCycle.Next(NamePlacement.Cell));
        Assert.Equal(NamePlacement.Below, LayoutCycle.Next(NamePlacement.Above));
        Assert.Equal(NamePlacement.Hidden, LayoutCycle.Next(NamePlacement.Below));
        Assert.Equal(NamePlacement.Cell, LayoutCycle.Next(NamePlacement.Hidden));
    }

    [Fact]
    public void TheStatusModeIsATwoValueToggle()
    {
        Assert.Equal(StatusMode.Line, LayoutCycle.Next(StatusMode.Cell));
        Assert.Equal(StatusMode.Cell, LayoutCycle.Next(StatusMode.Line));
    }

    [Fact]
    public void TheModelDialIsATwoValueToggle()
    {
        Assert.Equal(ModelDial.Hidden, LayoutCycle.Next(ModelDial.Shown));
        Assert.Equal(ModelDial.Shown, LayoutCycle.Next(ModelDial.Hidden));
    }

    [Fact]
    public void AValueOutsideTheEnumStartsTheCycleOver()
    {
        // A hand-edited settings file can hold anything. The button's job is to
        // get the panel back to something drawable, not to throw.
        Assert.Equal(LayoutFlow.Grid, LayoutCycle.Next((LayoutFlow)42));
    }
}

public class PanelMetricsTests
{
    private static PanelMetrics Default(int accounts) =>
        PanelMetrics.For(WidgetLayout.Default, accounts, WidgetSettings.DefaultSide);

    [Fact]
    public void OneAccountInTheDefaultLayoutIsTheOriginalSquare()
    {
        // 12 padding + 2*68 + 10 gap + 12 padding = 170, the size the widget
        // shipped with before accounts existed.
        var m = Default(1);

        Assert.Equal(170, m.Width, 6);
        Assert.Equal(2, m.Block.Rows);
        Assert.Equal(2, m.Block.Columns);
    }

    [Fact]
    public void ThreeAndFourAccountsShareAPanelShape()
    {
        Assert.Equal(Default(3).Width, Default(4).Width, 6);
        Assert.Equal(Default(3).Height, Default(4).Height, 6);
    }

    [Fact]
    public void APanelRowGrowsWidthAPanelColumnGrowsHeight()
    {
        var row = PanelMetrics.For(
            WidgetLayout.Default with { PanelFlow = LayoutFlow.Row }, 3, WidgetSettings.DefaultSide);
        var column = PanelMetrics.For(
            WidgetLayout.Default with { PanelFlow = LayoutFlow.Column }, 3, WidgetSettings.DefaultSide);

        Assert.True(row.Width > column.Width);
        Assert.True(column.Height > row.Height);
    }

    [Fact]
    public void NameAboveCostsALineAndAGapAndFreesACell()
    {
        var cell = Default(1);
        var above = PanelMetrics.For(
            WidgetLayout.Default with { Block = BlockLayout.Default with { Name = NamePlacement.Above } },
            1, WidgetSettings.DefaultSide);

        Assert.Equal(above.Block.Height - cell.Block.Height, above.Block.NameHeight + above.Gap, 6);
        // Three dials in a 2-column grid still need two rows — the freed cell
        // is a hole, not a saving.
        Assert.Equal(2, above.Block.Rows);
    }

    [Fact]
    public void ThreeDialsInARowWithTheNameAboveIsWideAndShort()
    {
        var m = PanelMetrics.For(
            WidgetLayout.Default with
            {
                Block = BlockLayout.Default with { Flow = LayoutFlow.Row, Name = NamePlacement.Above },
            },
            1, WidgetSettings.DefaultSide);

        // 12 + 3*68 + 2*10 + 12 = 248
        Assert.Equal(248, m.Width, 6);
        Assert.Equal(1, m.Block.Rows);
        Assert.Equal(3, m.Block.Columns);
    }

    [Fact]
    public void ScalingKeepsTheAspectRatio()
    {
        var small = PanelMetrics.For(WidgetLayout.Default, 4, WidgetSettings.MinSide);
        var large = PanelMetrics.For(WidgetLayout.Default, 4, WidgetSettings.MaxSide);

        Assert.Equal(small.Width / small.Height, large.Width / large.Height, 6);
    }

    [Fact]
    public void ClampsTheSideLikeTheSquarePanelDid()
    {
        Assert.Equal(PanelMetrics.For(WidgetLayout.Default, 4, WidgetSettings.MinSide), Default4(10));
        Assert.Equal(PanelMetrics.For(WidgetLayout.Default, 4, WidgetSettings.MaxSide), Default4(10_000));

        static PanelMetrics Default4(double side) => PanelMetrics.For(WidgetLayout.Default, 4, side);
    }

    [Fact]
    public void ZeroAccountsStillHasAUsablePanel()
    {
        var empty = Default(0);

        Assert.True(empty.Width > 0);
        Assert.True(empty.Height > 0);
    }

    [Fact]
    public void BlocksAreSeparatedMoreThanTheDialsInsideThem()
    {
        // Equal gaps made four 2x2 squares read as one 4x4 grid, and the name
        // stopped looking like it belonged to its own dials. Seen on an
        // offscreen render, not deduced.
        var m = Default(4);

        Assert.True(m.BlockGap > m.Gap);
        Assert.Equal(m.Gap * 2, m.BlockGap, 6);
    }

    [Fact]
    public void TheGapMatchesTheThemeGap()
    {
        // Theme.Gap(scale) is 10 * scale. The panel and the dials must share one
        // rhythm; they were 8 and 10 for a day and it showed.
        Assert.Equal(10, Default(1).Gap, 6);
    }

    [Fact]
    public void TheGapAboveTheContentEqualsTheGapUnderIt()
    {
        // The user's report, 2026-09-04: «отступы сверху и снизу визуально не
        // идентичные; service operational прилип в самом низу». The panel used
        // to centre the dial grid in everything but its padding and then draw
        // the status line 2 px off the bottom edge — 34 px over the names and
        // 3 px under the text, measured at his side of 207.
        //
        // Every name placement and both status modes, because the content the
        // top gap is measured against changes with the first and the block's
        // cell count with the second.
        foreach (var name in Enum.GetValues<NamePlacement>())
        foreach (var mode in new[] { StatusMode.Cell, StatusMode.Line })
        {
            var layout = WidgetLayout.Sanitize(
                WidgetLayout.Default with
                {
                    PanelFlow = LayoutFlow.Row,
                    Block = BlockLayout.Default with { Flow = LayoutFlow.Column, Name = name },
                },
                mode,
                ModelDial.Shown);

            var m = PanelMetrics.For(layout, 3, 207);

            Assert.Equal(m.TopGap, m.BottomGap, 6);
        }
    }

    [Fact]
    public void ThePanelIsTopPaddingPlusTheContentPlusTheStatusBand()
    {
        // No slack anywhere else: the band is the whole bottom budget, so the
        // grid sits flush under the top padding and the arithmetic the view
        // lays out is the arithmetic the window was sized from.
        var m = PanelMetrics.For(WidgetLayout.Default, 3, WidgetSettings.DefaultSide);
        var content = m.Rows * m.Block.Height + (m.Rows - 1) * m.BlockGap;

        Assert.Equal(m.Padding + content + m.StatusBand, m.Height, 6);
        // The band is the caption line with one padding above it and one below,
        // which is what makes all four edges of the panel read the same.
        Assert.Equal(m.Padding, m.BottomGap, 6);
        Assert.Equal(m.CaptionLine + 2 * m.Padding, m.StatusBand, 6);
    }

    [Fact]
    public void TheStatusBandIsReservedEvenWhenTheLineHasNothingToSay()
    {
        // In Cell mode the line is silent until something goes wrong, and in
        // either mode the edit hint appears the moment the mode is entered. The
        // panel must not change height for either, so the band is reserved from
        // the metrics and never from the text.
        var cell = PanelMetrics.For(
            WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Cell, ModelDial.Shown),
            2, WidgetSettings.DefaultSide);
        var line = PanelMetrics.For(
            WidgetLayout.Sanitize(WidgetLayout.Default, StatusMode.Line, ModelDial.Shown),
            2, WidgetSettings.DefaultSide);

        Assert.Equal(line.StatusBand, cell.StatusBand, 6);
        Assert.Equal(cell.TopGap, cell.BottomGap, 6);
    }

    [Fact]
    public void EditModeGrowsTheWindowByTheStripAndLeavesThePanelAlone()
    {
        // The strip is a band OUTSIDE the panel (amendment 2026-09-04): the
        // WINDOW grows by the reserve, and the view pushes the rounded border
        // away from the strip by exactly the same number — so the panel keeps
        // the size it has with the mode off, and the window's Top moves with it
        // to keep the screen position too. The user's complaint was the panel
        // visibly resizing and then sitting elsewhere on the way out.
        var normal = PanelMetrics.For(WidgetLayout.Default, 2, WidgetSettings.DefaultSide);
        var editing = PanelMetrics.For(WidgetLayout.Default, 2, WidgetSettings.DefaultSide, editMode: true);

        Assert.Equal(normal.Width, editing.Width, 6);
        Assert.Equal(normal.Height, editing.Height - editing.ToolbarReserve, 6);
        Assert.Equal(0, normal.ToolbarReserve, 6);
        Assert.Equal(editing.Toolbar.Height, editing.ToolbarReserve, 6);
    }

    [Fact]
    public void NineButtonsFitOnOneRowAtEverySide()
    {
        // Done, accounts, dials, name, plan, status, model, lock, hide.
        // The eight-button claim this replaces called 16 pt the ceiling and a
        // ninth button impossible at any pitch — true only while the BUTTON was
        // fixed at 16: 9*16 + 8*2 = 160 against the default panel's inner width
        // of 170 - 2*12 = 146. Shrinking the button to 14 gives 9*14 + 8*2 = 142,
        // and both numbers scale from the same side, so the fit holds across the
        // whole range rather than at the size it was measured on.
        foreach (var side in new[] { WidgetSettings.MinSide, WidgetSettings.DefaultSide, WidgetSettings.MaxSide })
        {
            var m = PanelMetrics.For(WidgetLayout.Default, 1, side, editMode: true);

            Assert.Equal(1, m.Toolbar.Rows);
            Assert.Equal(9, m.Toolbar.Columns);
            Assert.True(
                m.Toolbar.Columns * m.Toolbar.ButtonSize + (m.Toolbar.Columns - 1) * m.Toolbar.Gap
                    <= m.Width - 2 * m.Padding,
                $"the strip overflows the panel at side {side}");
        }
    }

    [Fact]
    public void ANinthButtonAtTheOldSizeWouldNotHaveFit()
    {
        // The negative half of the claim above, so "14 fits" is a measurement
        // and not a coincidence. At the 16 pt the eight-button strip used, nine
        // buttons need 9*16 + 8*2 = 160 against the default panel's 146 pt of
        // inner width — which is why the size came down rather than the count
        // staying at eight. (Zero pitch would technically fit 144 in 146;
        // buttons with no gap at all are not a strip, and the pitch is shared
        // with the row that ships.)
        const double OldButtonSize = 16;
        var m = PanelMetrics.For(WidgetLayout.Default, 1, WidgetSettings.DefaultSide, editMode: true);
        var inner = m.Width - 2 * m.Padding;

        Assert.True(
            ToolbarMetrics.ButtonCount * OldButtonSize
                + (ToolbarMetrics.ButtonCount - 1) * ToolbarMetrics.BaseButtonGap > inner,
            "nine 16 pt buttons were supposed to overflow the default panel");
        Assert.True(
            ToolbarMetrics.ButtonCount * ToolbarMetrics.BaseButtonSize
                + (ToolbarMetrics.ButtonCount - 1) * ToolbarMetrics.BaseButtonGap <= inner);
    }

    [Fact]
    public void ThePlanLineIsReservedUnderTheNameRow()
    {
        // The plan is a second line under the account name. With the name Above
        // or Below it is a row of the block, so the block must grow by exactly
        // one caption line — otherwise the dials climb into the text. The name
        // in a CELL already owns a whole dial square and needs no reserve.
        foreach (var name in new[] { NamePlacement.Above, NamePlacement.Below })
        {
            var hidden = Row(name, PlanLine.Hidden);
            var shown = Row(name, PlanLine.Shown);

            Assert.Equal(hidden.Block.NameHeight + shown.CaptionLine, shown.Block.NameHeight, 6);
            Assert.Equal(hidden.Height + shown.CaptionLine, shown.Height, 6);
        }

        foreach (var name in new[] { NamePlacement.Cell, NamePlacement.Hidden })
        {
            Assert.Equal(Row(name, PlanLine.Hidden).Height, Row(name, PlanLine.Shown).Height, 6);
        }

        static PanelMetrics Row(NamePlacement name, PlanLine plan) =>
            PanelMetrics.For(
                WidgetLayout.Default with { Block = BlockLayout.Default with { Name = name } },
                2, WidgetSettings.DefaultSide, editMode: false, planLine: plan);
    }

    [Fact]
    public void ThePlanLineKeepsThePanelsTopAndBottomIdentical()
    {
        // The band arithmetic must survive the extra row: the reserve is added
        // to the BLOCK, not taken out of the padding, so the equality the
        // 2026-09-04 rebuild established still holds with the line on.
        foreach (var name in Enum.GetValues<NamePlacement>())
        {
            var m = PanelMetrics.For(
                WidgetLayout.Default with
                {
                    PanelFlow = LayoutFlow.Row,
                    Block = BlockLayout.Default with { Flow = LayoutFlow.Column, Name = name },
                },
                3, 207, editMode: false, planLine: PlanLine.Shown);

            Assert.Equal(m.TopGap, m.BottomGap, 6);
        }
    }

    [Fact]
    public void APanelNarrowerThanTheStripWrapsToTwoRows()
    {
        // One account with its dials in a column is one dial wide — narrower
        // than nine buttons however tightly they are packed. The wrap is a
        // fallback, not a fit: five buttons still overflow this panel's inner
        // width, and the panel is never widened to hold them. What the wrap
        // buys is a narrower row, and a reserve that matches the rows drawn.
        var narrow = PanelMetrics.For(
            WidgetLayout.Default with { Block = BlockLayout.Default with { Flow = LayoutFlow.Column } },
            1, WidgetSettings.MinSide, editMode: true);

        Assert.Equal(2, narrow.Toolbar.Rows);
        Assert.Equal(5, narrow.Toolbar.Columns);
        Assert.True(
            narrow.Toolbar.Columns * narrow.Toolbar.ButtonSize + 3 * narrow.Toolbar.Gap
                < ToolbarMetrics.ButtonCount * narrow.Toolbar.ButtonSize
                    + (ToolbarMetrics.ButtonCount - 1) * narrow.Toolbar.Gap);
    }

    [Fact]
    public void TheReserveIsTheDrawnRowsPlusOneGap()
    {
        // The strip is a band of its own: the reserve has to be exactly what
        // the view lays out, or the toolbar sits on the dials or floats.
        var m = PanelMetrics.For(WidgetLayout.Default, 1, WidgetSettings.MaxSide, editMode: true);

        Assert.Equal(
            m.Toolbar.Rows * m.Toolbar.ButtonSize + (m.Toolbar.Rows - 1) * m.Toolbar.Gap + m.Gap,
            m.ToolbarReserve, 6);
    }
}
