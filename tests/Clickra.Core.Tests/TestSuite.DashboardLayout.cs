using System;
using System.IO;
using Clickra.Core.Layout;

namespace Clickra.Core.Tests;

/// <summary>
/// The dashboard keeps every row height, row stride and block height in one table
/// (<c>Clickra.Core.Layout</c>) so painting, hit-testing and the scroll height cannot drift
/// apart. The table is pure arithmetic, so the stack itself is asserted here rather than
/// through a source scan; the scans below only pin the plumbing (who consumes what).
/// </summary>
static partial class TestSuite
{
    private const string EventsClickFileName = "DashboardWindow.Events.Click.cs";
    private const string EventsFileName = "DashboardWindow.Events.cs";

    public static void RegisterDashboardLayoutTests(TestRunner runner)
    {
        runner.Run("Dashboard layout: the table keeps the measurements the dashboard has always drawn",
            TestDashboardLayoutTableNumbers);
        runner.Run("Dashboard layout: the history column stacks queue, parked and history without overlap",
            TestHistoryColumnStackInvariants);
        runner.Run("Dashboard layout: the history column reproduces the arithmetic it replaced",
            TestHistoryColumnMatchesTheOldFormulas);
        runner.Run("Dashboard layout: a row added above shifts every block below by exactly one stride",
            TestHistoryColumnShiftsLaterBlocks);
        runner.Run("Dashboard layout: an empty parked block takes no vertical space",
            TestHistoryColumnEmptyParkedBlock);
        runner.Run("Dashboard layout: sidebar tabs round-trip between paint position and hit band",
            TestSidebarTabRoundTrip);
        runner.Run("Dashboard layout: detail tap bands are disjoint and the time row is not scrollable",
            TestDetailFieldTapBands);
        runner.Run("Dashboard layout: the convert grid, its cards and the start button do not overlap",
            TestConvertGridGeometry);
        runner.RunGuard("AOT convert workspace: compact command groups stay within first-screen density",
            TestAotConvertFirstScreenDensity);
        runner.RunGuard("AOT settings workspace: responsive columns preserve geometry and hit parity",
            TestAotSettingsResponsiveGeometry);
        runner.Run("Dashboard layout: dropdown popup rows round-trip between paint and hit-testing",
            TestDropdownPopupGeometry);
        runner.Run("Dashboard layout: the parked adjust buttons sit inside their row and leave the deadline room",
            TestParkedActionButtonGeometry);
        runner.Run("Dashboard layout: every consumer takes its geometry from the table",
            TestDashboardConsumersUseTheTable);
        runner.Run("Dashboard layout: the sidebar paints as many tabs as the table hit-tests",
            TestSidebarTabCountMatchesPaint);
    }

    /// <summary>
    /// Extracting the numbers into a table was a move, not a redesign: these are the values the
    /// dashboard drew before, so a change here is a visible layout change and not an accident.
    /// </summary>
    private static void TestDashboardLayoutTableNumbers()
    {
        Assert.Equal(90, DashboardLayout.ContentTop);
        Assert.Equal(44, DashboardLayout.RowHeight);
        Assert.Equal(160, DashboardLayout.ExpandedRowHeight);
        Assert.Equal(8, DashboardLayout.RowGap);
        Assert.Equal(52, DashboardLayout.RowStride);
        Assert.Equal(30, DashboardLayout.ParkedHeaderHeight);
        Assert.Equal(20, DashboardLayout.SectionPadding);
        Assert.Equal(460, DashboardLayout.MinContentHeight);

        Assert.Equal(120, DashboardLayout.SidebarTabTop);
        Assert.Equal(40, DashboardLayout.SidebarTabHeight);
        Assert.Equal(48, DashboardLayout.SidebarTabStride);
        Assert.Equal(5, DashboardLayout.SidebarTabCount);

        Assert.Equal(95, DashboardLayout.ConvertZoneTop);
        Assert.Equal(72, DashboardLayout.ConvertZoneHeight);
        Assert.Equal(176, DashboardLayout.ConvertGridTop);
        Assert.Equal(20, DashboardLayout.ConvertGroupHeaderHeight);
        Assert.Equal(34, DashboardLayout.ConvertCardHeight);
        Assert.Equal(6, DashboardLayout.ConvertCardGap);
        Assert.Equal(14, DashboardLayout.ConvertGroupGap);
        Assert.Equal(32, DashboardLayout.ConvertStartButtonHeight);
        Assert.Equal(8, DashboardLayout.ConvertStartButtonGap);
        Assert.Equal(3, DashboardLayout.ConvertGroupCount);

        // 下拉選單與清單：控制項 240x30、列距 26、語言清單 180 高且同時顯示 5 列。
        Assert.Equal(240, DashboardLayout.DropdownWidth);
        Assert.Equal(30, DashboardLayout.DropdownHeight);
        Assert.Equal(26, DashboardLayout.DropdownItemStride);
        Assert.Equal(24, DashboardLayout.DropdownItemHeight);
        Assert.Equal(180, DashboardLayout.LanguagePopupHeight);
        Assert.Equal(5, DashboardLayout.LanguagePopupVisibleRows);
        Assert.Equal(38, DashboardLayout.LanguagePopupListTop);
        Assert.Equal(4, DashboardLayout.PdfPopupListTop);
        Assert.Equal(8, DashboardLayout.PdfPopupHeight(0));

        // 展開列的明細：分隔線在收合列高，標籤 54/80/106/132，捲軸是標籤再加 17。
        Assert.Equal(44, DashboardLayout.DetailDividerY);
        Assert.Equal(54, DashboardLayout.DetailFieldY(0));
        Assert.Equal(80, DashboardLayout.DetailFieldY(1));
        Assert.Equal(106, DashboardLayout.DetailFieldY(2));
        Assert.Equal(132, DashboardLayout.DetailFieldY(3));
        Assert.Equal(71, DashboardLayout.DetailScrollbarY(0));
        Assert.Equal(97, DashboardLayout.DetailScrollbarY(1));
        Assert.Equal(149, DashboardLayout.DetailScrollbarY(2));
        Assert.Equal(3, DashboardLayout.DetailScrollableFieldCount);
    }

    private static void TestHistoryColumnStackInvariants()
    {
        var stack = HistoryLayout.Build(activeCount: 3, parkedCount: 4, historyCount: 6, expandedHistoryIndex: 2);
        HistoryBlock active = HistoryLayout.Find(stack, HistoryBlockKind.ActiveQueue);
        HistoryBlock parked = HistoryLayout.Find(stack, HistoryBlockKind.Parked);
        HistoryBlock history = HistoryLayout.Find(stack, HistoryBlockKind.History);

        Assert.Equal(3, active.RowCount);
        Assert.Equal(4, parked.RowCount);
        Assert.Equal(6, history.RowCount);
        Assert.True(active.Top < parked.Top && parked.Top < history.Top,
            "Blocks must stack top-down in paint order.");

        AssertRowsDoNotOverlap(active, "active queue");
        AssertRowsDoNotOverlap(parked, "parked block");
        AssertRowsDoNotOverlap(history, "history list");

        // 待繼續任務有一個標題行，它不是列：停在區塊標題上的游標不會展開任何一列，
        // 而第一列必定從標題行之下開始。
        Assert.Equal(parked.Top + DashboardLayout.ParkedHeaderHeight, parked.RowTop(0));
        Assert.Equal(-1, parked.RowAt(parked.Top));
        Assert.Equal(
            DashboardLayout.ParkedHeaderHeight + (parked.RowCount - 1) * DashboardLayout.RowStride + DashboardLayout.RowHeight,
            parked.Height);

        // 進行中佇列沒有標題行，第一列就在區塊起點。
        Assert.Equal(active.Top, active.RowTop(0));
        Assert.Equal(0, active.RowAt(active.Top));

        foreach (HistoryBlock block in stack)
        {
            for (int i = 0; i < block.RowCount; i++)
            {
                int top = block.RowTop(i);
                Assert.Equal(i, block.RowAt(top));
                Assert.Equal(i, block.RowAt(top + block.RowHeight(i) - 1));
                Assert.Equal(0, block.RowRelativeY(i, top));

                // 每一列都在自己的區塊內。
                Assert.True(top >= block.Top && top + block.RowHeight(i) <= block.Bottom,
                    $"Row {i} of {block.Kind} escapes its block.");
            }

            // 列與列之間的間隙不屬於任何一列：點在間隙上不該展開某一列。
            for (int i = 1; i < block.RowCount; i++)
            {
                Assert.Equal(-1, block.RowAt(block.RowTop(i) - 1));
            }

            // 區塊之外也不是列。
            Assert.Equal(-1, block.RowAt(block.Top - 1));
            Assert.Equal(-1, block.RowAt(block.Bottom));
        }

        for (int i = 0; i < history.RowCount; i++)
        {
            Assert.Equal(i == 2 ? DashboardLayout.ExpandedRowHeight : DashboardLayout.RowHeight, history.RowHeight(i));
        }
    }

    /// <summary>
    /// The arithmetic this replaced, copied verbatim from the dashboard:
    /// <c>startY = 90 + activeCount * 52 + (parkedCount == 0 ? 0 : 30 + parkedCount * 52)</c> and
    /// <c>height = max(460, startY + Σ(rowHeight_i + 8) + 20)</c>. A move that changes a pixel
    /// shows up here.
    /// </summary>
    private static void TestHistoryColumnMatchesTheOldFormulas()
    {
        foreach ((int active, int parked, int history, int expanded) in new[]
                 {
                     (0, 0, 0, -1), (1, 0, 3, -1), (2, 3, 5, 1), (0, 2, 1, 0), (4, 1, 8, 7)
                 })
        {
            var stack = HistoryLayout.Build(active, parked, history, expanded);
            HistoryBlock historyBlock = HistoryLayout.Find(stack, HistoryBlockKind.History);

            int expectedStart = 90 + active * 52 + (parked == 0 ? 0 : 30 + parked * 52);
            Assert.Equal(expectedStart, historyBlock.Top);

            int expectedHeight = expectedStart + 20;
            int cursor = expectedStart;
            for (int i = 0; i < history; i++)
            {
                Assert.Equal(cursor, historyBlock.RowTop(i));
                expectedHeight += (i == expanded ? 160 : 44) + 8;
                cursor += (i == expanded ? 160 : 44) + 8;
            }

            Assert.Equal(Math.Max(460, expectedHeight), HistoryLayout.ContentHeight(stack));
        }
    }

    private static void TestHistoryColumnShiftsLaterBlocks()
    {
        var baseline = HistoryLayout.Build(1, 1, 3, -1);
        int baselineTop = HistoryLayout.Find(baseline, HistoryBlockKind.History).Top;

        // 上方多一列進行中任務，或暫存區多一列，下面都會各下移一個列距。
        Assert.Equal(baselineTop + DashboardLayout.RowStride,
            HistoryLayout.Find(HistoryLayout.Build(2, 1, 3, -1), HistoryBlockKind.History).Top);
        Assert.Equal(baselineTop + DashboardLayout.RowStride,
            HistoryLayout.Find(HistoryLayout.Build(1, 2, 3, -1), HistoryBlockKind.History).Top);

        // 沒有暫存任務時不留標題行的空位，歷史列表緊接在進行中佇列之後。
        var noParked = HistoryLayout.Build(1, 0, 3, -1);
        Assert.Equal(
            HistoryLayout.Find(noParked, HistoryBlockKind.ActiveQueue).RowTop(0) + DashboardLayout.RowStride,
            HistoryLayout.Find(noParked, HistoryBlockKind.History).Top);
    }

    private static void TestHistoryColumnEmptyParkedBlock()
    {
        var stack = HistoryLayout.Build(0, 0, 0, -1);
        HistoryBlock parked = HistoryLayout.Find(stack, HistoryBlockKind.Parked);

        Assert.True(parked.IsEmpty, "No parked tasks means nothing to draw, not an empty frame.");
        Assert.Equal(0, parked.Height);
        Assert.Equal(0, parked.RowCount);
        Assert.Equal(-1, parked.RowAt(parked.Top));
        Assert.Equal(parked.Top, HistoryLayout.NextTop(parked));

        Assert.Equal(DashboardLayout.ContentTop, parked.Top);
        Assert.Equal(DashboardLayout.ContentTop, HistoryLayout.Find(stack, HistoryBlockKind.History).Top);
        Assert.Equal(DashboardLayout.MinContentHeight, HistoryLayout.ContentHeight(stack));
    }

    private static void TestSidebarTabRoundTrip()
    {
        for (int index = 0; index < DashboardLayout.SidebarTabCount; index++)
        {
            int top = DashboardLayout.SidebarTabY(index);
            Assert.Equal(index, DashboardLayout.SidebarTabAt(top));
            Assert.Equal(index, DashboardLayout.SidebarTabAt(top + DashboardLayout.SidebarTabHeight - 1));

            // 籤之間的行距與籤列之外都不是籤。
            Assert.Equal(-1, DashboardLayout.SidebarTabAt(top - 1));
            Assert.Equal(-1, DashboardLayout.SidebarTabAt(top + DashboardLayout.SidebarTabHeight));
        }

        Assert.Equal(-1, DashboardLayout.SidebarTabAt(DashboardLayout.SidebarTabTop - 1));
        Assert.Equal(-1, DashboardLayout.SidebarTabAt(
            DashboardLayout.SidebarTabY(DashboardLayout.SidebarTabCount - 1) + DashboardLayout.SidebarTabStride));

        // 選取指示條在籤內，不會溢到隔壁。
        Assert.Equal(DashboardLayout.SidebarTabAccentInset * 2 + DashboardLayout.SidebarTabAccentHeight,
            DashboardLayout.SidebarTabHeight);
    }

    private static void TestDetailFieldTapBands()
    {
        for (int field = 0; field < DashboardLayout.DetailScrollableFieldCount; field++)
        {
            int top = DashboardLayout.DetailScrollbarY(field) - DashboardLayout.DetailScrollbarOffset - DashboardLayout.DetailFieldTapTop;
            int bottom = top + DashboardLayout.DetailFieldTapHeight;

            Assert.Equal(field, DashboardLayout.DetailScrollFieldAt(top));
            Assert.Equal(field, DashboardLayout.DetailScrollFieldAt(bottom - 1));
            Assert.Equal(-1, DashboardLayout.DetailScrollFieldAt(top - 1));
            Assert.Equal(-1, DashboardLayout.DetailScrollFieldAt(bottom));
        }

        // 每個標籤都落在自己的帶內。
        Assert.Equal(0, DashboardLayout.DetailScrollFieldAt(DashboardLayout.DetailFieldY(0)));
        Assert.Equal(1, DashboardLayout.DetailScrollFieldAt(DashboardLayout.DetailFieldY(1)));
        Assert.Equal(2, DashboardLayout.DetailScrollFieldAt(DashboardLayout.DetailFieldY(3)));

        // 時間列（第三列）只被填入、不捲動，因此不屬於任何帶。
        Assert.Equal(-1, DashboardLayout.DetailScrollFieldAt(DashboardLayout.DetailFieldY(2)));

        // 帶與帶之間有間隔，而且最後一帶仍在展開列內。
        Assert.True(DashboardLayout.DetailFieldTapHeight < DashboardLayout.DetailFieldStride,
            "Detail tap bands must be narrower than the row stride or two fields would share a band.");
        Assert.True(DashboardLayout.DetailFieldY(3) - DashboardLayout.DetailFieldTapTop + DashboardLayout.DetailFieldTapHeight
                    <= DashboardLayout.ExpandedRowHeight,
            "The last detail band must stay inside the expanded row.");
    }

    private static void TestConvertGridGeometry()
    {
        const int contentX = 200;
        const int logW = 1000;
        LayoutRect zone = DashboardLayout.ConvertZoneRect(contentX, logW);
        Assert.True(zone.Width > 0 && zone.Height == DashboardLayout.ConvertZoneHeight,
            "The drop zone must fit inside the content area.");

        int[] groupSizes = { 3, 5, 8 };
        for (int group = 0; group < DashboardLayout.ConvertGroupCount; group++)
        {
            int columns = DashboardLayout.ConvertGroupColumns(group);
            for (int item = 0; item < groupSizes[group]; item++)
            {
                LayoutRect card = DashboardLayout.ConvertCardRect(group, item, zone.X, zone.Width, groupSizes);

                // 格線在拖放區之下，不是疊在它上面。
                Assert.True(card.Y >= zone.Bottom, "The command grid must sit below the drop zone.");

                if (item >= columns)
                {
                    LayoutRect above = DashboardLayout.ConvertCardRect(group, item - columns, zone.X, zone.Width, groupSizes);
                    Assert.Equal(above.Bottom + DashboardLayout.ConvertCardGap, card.Y);
                }

                if (item % columns > 0)
                {
                    LayoutRect left = DashboardLayout.ConvertCardRect(group, item - 1, zone.X, zone.Width, groupSizes);
                    Assert.True(left.Right <= card.X, "Command columns must not overlap.");
                }

                // 分類標題與該分類第一張卡片的左緣對齊。
                if (item == 0)
                    Assert.Equal(card.X, DashboardLayout.ConvertGroupX(group, zone.X, zone.Width, groupSizes));
            }
        }

        LayoutRect lastCard = DashboardLayout.ConvertCardRect(2, groupSizes[2] - 1, zone.X, zone.Width, groupSizes);
        LayoutRect startButton = DashboardLayout.ConvertStartButtonRect(zone.X, zone.Width, groupSizes);
        Assert.Equal(lastCard.Y + DashboardLayout.ConvertCardStride + DashboardLayout.ConvertStartButtonGap, startButton.Y);
        Assert.True(startButton.Y >= lastCard.Bottom, "The start button must clear the last card.");
        Assert.Equal(zone.Width, startButton.Width);
        Assert.Equal(zone.X, startButton.X);

        // 清除鈕畫在拖放區內，命中判定與繪製同一個矩形。
        LayoutRect clear = DashboardLayout.ConvertClearButtonRect(logW);
        Assert.True(zone.Contains(clear.X, clear.Y) && zone.Contains(clear.Right - 1, clear.Bottom - 1),
            "The clear button must sit inside the drop zone.");

        // 歷史頁的清除鈕同樣由表中取得。
        LayoutRect historyClear = DashboardLayout.HistoryClearButtonRect(logW);
        Assert.Equal(DashboardLayout.HistoryClearButtonWidth, historyClear.Width);
        Assert.Equal(DashboardLayout.HistoryClearButtonHeight, historyClear.Height);
    }

    private static void TestAotConvertFirstScreenDensity()
    {
        int[] groupSizes = { 3, 5, 8 };
        Assert.Equal(3, DashboardLayout.ConvertGroupColumns(0));
        Assert.Equal(5, DashboardLayout.ConvertGroupColumns(1));
        Assert.Equal(4, DashboardLayout.ConvertGroupColumns(2));
        Assert.Equal(1, DashboardLayout.ConvertGroupRows(0, groupSizes[0]));
        Assert.Equal(1, DashboardLayout.ConvertGroupRows(1, groupSizes[1]));
        Assert.Equal(2, DashboardLayout.ConvertGroupRows(2, groupSizes[2]));

        LayoutRect zone = DashboardLayout.ConvertZoneRect(contentX: 260, logW: 1520);
        LayoutRect start = DashboardLayout.ConvertStartButtonRect(zone.X, zone.Width, groupSizes);
        Assert.True(zone.Bottom < DashboardLayout.ConvertGridTop,
            "The compact drop zone must leave visible separation before command groups.");
        Assert.True(start.Bottom <= DashboardLayout.MinContentHeight - 8,
            $"AOT convert controls must stay inside the minimum dashboard content height; start bottom was {start.Bottom}.");

        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");
        string paint = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.Convert.cs"));
        string hitTest = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.HitTesting.cs"));
        Assert.True(paint.Contains("ConvertCardRect(group, local, zoneX, zoneW, ConvertCommandGroupSizes)", StringComparison.Ordinal),
            "AOT convert painting must use DashboardLayout command geometry.");
        Assert.True(hitTest.Contains("ConvertCardRect(group, local, zone.X, zone.Width, ConvertCommandGroupSizes)", StringComparison.Ordinal),
            "AOT convert hit-testing must use the same DashboardLayout command geometry as painting.");
    }

    private static void TestAotSettingsResponsiveGeometry()
    {
        const int contentX = 200;
        const int wideLogW = 640;

        Assert.False(SettingsLayout.IsWide(SettingsLayout.WideBreakpoint - 1),
            "Widths below the Settings breakpoint must keep the single-column layout.");
        Assert.True(SettingsLayout.IsWide(SettingsLayout.WideBreakpoint),
            "The Settings breakpoint itself must enable the two-column layout.");

        int columnWidth = SettingsLayout.ColumnWidth(contentX, wideLogW);
        int leftX = SettingsLayout.ColumnX(contentX, wideLogW, 0);
        int rightX = SettingsLayout.ColumnX(contentX, wideLogW, 1);
        Assert.Equal(contentX, leftX);
        Assert.Equal(leftX + columnWidth + SettingsLayout.ColumnGap, rightX);
        Assert.Equal(wideLogW - SettingsLayout.ContentRightMargin, rightX + columnWidth);
        int responsiveSliderWidth = SettingsLayout.SliderWidthFor(columnWidth);
        Assert.True(responsiveSliderWidth + 2 * SettingsLayout.SliderHitHorizontalPadding <= columnWidth,
            "A wide Settings column must fit a slider and its full hit target.");
        Assert.True(responsiveSliderWidth < SettingsLayout.SliderWidth,
            "The 200% DPI viewport must shorten wide-column sliders instead of forcing horizontal scrolling.");

        LayoutRect toggle = SettingsLayout.ToggleRectWithin(leftX, columnWidth, SettingsLayout.ContentTop);
        Assert.True(toggle.X >= leftX && toggle.Right <= leftX + columnWidth,
            "A column toggle must stay inside its own column.");

        LayoutRect leftOverview = SettingsLayout.CardRect(
            leftX, SettingsLayout.ContentTop, columnWidth, SettingsLayout.OverviewCardHeight);
        LayoutRect rightOverview = SettingsLayout.CardRect(
            rightX, SettingsLayout.ContentTop, columnWidth, SettingsLayout.OverviewCardHeight);
        Assert.True(leftOverview.Right < rightOverview.X,
            "Overview cards must be separated by the responsive column gap.");

        LayoutRect compression = SettingsLayout.CardRect(
            leftX,
            leftOverview.Bottom + SettingsLayout.CardGap,
            wideLogW - leftX - SettingsLayout.ContentRightMargin,
            SettingsLayout.CompressionCardHeight);
        Assert.Equal(leftX, compression.X);
        Assert.Equal(wideLogW - SettingsLayout.ContentRightMargin, compression.Right);
        int compressionInnerWidth = columnWidth - 2 * SettingsLayout.CardPadding;
        int compressionSliderWidth = SettingsLayout.SliderWidthFor(compressionInnerWidth);
        Assert.True(compressionSliderWidth + 2 * SettingsLayout.SliderHitHorizontalPadding <= columnWidth,
            "The PDF quality slider must stay inside the left half of the full-width compression card.");
        LayoutRect secondaryToggle = SettingsLayout.ToggleRectWithin(
            rightX + SettingsLayout.CardPadding,
            compressionInnerWidth,
            compression.Y + SettingsLayout.CompressionSecondaryTop);
        Assert.True(secondaryToggle.X >= rightX && secondaryToggle.Right <= compression.Right,
            "PDF structure toggles must stay inside the right half of the full-width compression card.");

        int firstLanguageDropdownBottom = 36 + DashboardLayout.DropdownHeight;
        int secondLanguageTitleTop = 12 + SettingsLayout.OverviewRowGap;
        Assert.True(firstLanguageDropdownBottom < secondLanguageTitleTop,
            "Wide language controls need visible space between the first dropdown and the PDF language label.");
        int secondLanguageDropdownBottom = 36 + SettingsLayout.OverviewRowGap + DashboardLayout.DropdownHeight;
        Assert.True(secondLanguageDropdownBottom < SettingsLayout.OverviewCardHeight,
            "Both language dropdowns must fit inside the overview card with bottom padding.");

        int stackedLanguageHeight = SettingsLayout.LanguageSectionHeight + SettingsLayout.PdfLanguageSectionHeight;
        int pairedLanguageHeight = Math.Max(SettingsLayout.LanguageSectionHeight, SettingsLayout.PdfLanguageSectionHeight);
        Assert.True(pairedLanguageHeight < stackedLanguageHeight,
            "Wide language controls must consume one row instead of two stacked sections.");
        Assert.True(SettingsLayout.PrimaryToggleSectionHeight < SettingsLayout.ToggleSectionHeight,
            "Primary Settings toggles must retain the compact first-screen rhythm.");
        Assert.True(SettingsLayout.ContentBottomPadding <= 32,
            "Settings trailing padding must stay compact at high DPI instead of creating a mostly empty viewport.");
        Assert.True(SettingsLayout.ChoiceCardExpandedHeight > SettingsLayout.ChoiceCardHeight,
            "A custom output path needs extra room inside the same output card rather than escaping below it.");
        Assert.True(SettingsLayout.EngineLibreOfficeCardHeight > SettingsLayout.EngineCardHeight,
            "LibreOffice management actions need an expanded engine card instead of spilling into the next section.");
        Assert.True(SettingsLayout.RetentionCardHeight >= 120,
            "Task retention title, current value, and presets must fit in one bounded card.");
        string dir = DashboardDir();
        string paint = File.ReadAllText(Path.Combine(dir, "DashboardWindow.Paint.Settings.cs"));
        string dashboardPaint = File.ReadAllText(Path.Combine(dir, "DashboardWindow.Paint.cs"));
        string hitTest = File.ReadAllText(Path.Combine(dir, "DashboardWindow.HitTesting.cs"));
        string click = File.ReadAllText(Path.Combine(dir, EventsClickFileName));
        string events = File.ReadAllText(Path.Combine(dir, EventsFileName));

        Assert.True(paint.Contains("SettingsLayout.IsWide((int)logW)", StringComparison.Ordinal),
            "AOT Settings painting must branch on logical width through SettingsLayout.");
        Assert.True(dashboardPaint.Contains("DrawSettingsTab(g, logW, virtLogH, contentX)", StringComparison.Ordinal),
            "AOT Settings must receive the real viewport width rather than the 760px virtual canvas.");
        Assert.True(dashboardPaint.Contains("_activeTab != 3 && logW < 760", StringComparison.Ordinal),
            "Responsive Settings must not expose the legacy virtual-canvas horizontal scrollbar.");
        Assert.True(paint.Contains("SettingsLayout.ColumnX((int)contentX, (int)logW, 1)", StringComparison.Ordinal),
            "Wide Settings groups must take the second-column X from SettingsLayout.");
        Assert.True(paint.Contains("SettingsLayout.OverviewCardHeight", StringComparison.Ordinal)
                    && paint.Contains("SettingsLayout.CompressionCardHeight", StringComparison.Ordinal),
            "Wide Settings must use bounded overview and compression cards rather than free-running sections.");
        Assert.True(paint.Contains("y + SettingsLayout.ContentBottomPadding", StringComparison.Ordinal),
            "AOT Settings content height must use the compact shared trailing padding.");
        Assert.False(paint.Contains("y + 80f", StringComparison.Ordinal),
            "AOT Settings must not restore the oversized trailing padding that created an empty high-DPI viewport.");
        Assert.True(paint.Contains("int fullWidth = (int)(logW - contentX - SettingsLayout.ContentRightMargin)", StringComparison.Ordinal)
                    && paint.Contains("int pdfSecondaryX = SettingsLayout.ColumnX((int)contentX, (int)logW, 1)", StringComparison.Ordinal),
            "Wide PDF compression must use the full card width and place secondary controls in the second column.");
        Assert.True(paint.Contains("SettingsLayout.ChoiceCardExpandedHeight", StringComparison.Ordinal)
                    && paint.Contains("SettingsLayout.EngineCardHeight", StringComparison.Ordinal)
                    && paint.Contains("SettingsLayout.RetentionCardHeight", StringComparison.Ordinal),
            "Wide Settings groups must keep custom output, Office engine, and task retention inside bounded cards.");
        Assert.True(paint.Contains("retentionContentX = retentionCard.X + SettingsLayout.CardPadding", StringComparison.Ordinal)
                    && paint.Contains("engineContentX = wideSettings", StringComparison.Ordinal),
            "Wide Settings card contents must use the shared card inset instead of falling back to naked page coordinates.");
        AssertContainsAll(
            paint,
            "Retention controls must use the compact preset gap and the distinct separator before the preset group.",
            "curX += wStep + SettingsLayout.RetentionPresetGap",
            "curX += wStep + SettingsLayout.RetentionGroupGap",
            "curX += btnW + SettingsLayout.RetentionPresetGap");
        AssertContainsAll(
            paint,
            "AOT Settings must explicitly skip per-conversion image compression controls.",
            "descriptor.Key.Equals(ClickraSettings.ImageCompressLevel",
            "descriptor.Key.Equals(ClickraSettings.ImageCompressMaxDimension");
        AssertContainsNone(
            paint,
            "AOT Settings must not expose image compression profiles as global preferences.",
            "AotImageCompressionPresets");
        AssertContainsNone(
            paint,
            "AOT Settings must not advertise the Fluent add-on until it has a production-ready distribution path.",
            "setting_fluent_title",
            "FluentRuntimeHelper");
        AssertContainsNone(
            click,
            "Hidden Fluent Settings UI must not leave a stale clickable element behind.",
            "element == 40",
            "case 40:");

        foreach (string source in new[] { hitTest, click, events })
            AssertContainsAll(
                source,
                "Dropdown hit, popup, and hover consumers must use the painted responsive X coordinates.",
                "_langDropdownX",
                "_pdfLangDropdownX");

        Assert.False(hitTest.Contains("DropdownButtonRect((int)contentX, _langDropdownY)", StringComparison.Ordinal),
            "Hit testing must not assume the language dropdown stays in the first column.");
        Assert.False(events.Contains("DropdownPopupRect((int)GetContentX(logW), _pdfLangDropdownY", StringComparison.Ordinal),
            "Dropdown hover must not reconstruct the PDF dropdown X from the page origin.");
    }

    private static void AssertContainsAll(string source, string message, params string[] patterns)
    {
        foreach (string pattern in patterns)
            Assert.True(source.Contains(pattern, StringComparison.Ordinal), message);
    }

    private static void AssertContainsNone(string source, string message, params string[] patterns)
    {
        foreach (string pattern in patterns)
            Assert.False(source.Contains(pattern, StringComparison.Ordinal), message);
    }

    /// <summary>
    /// Both dropdowns draw their popup above the control and answer clicks and hover from the same
    /// rows. The popup height, the row stride and the row under the cursor used to be recomputed in
    /// the paint file, the click handler and the hover handler; the round-trip below is what keeps a
    /// row you can see from being a row you cannot click.
    /// </summary>
    private static void TestDropdownPopupGeometry()
    {
        const int contentX = 200;
        const int controlY = 400;
        const int itemCount = 5;

        LayoutRect control = DashboardLayout.DropdownButtonRect(contentX, controlY);
        Assert.Equal(DashboardLayout.DropdownWidth, control.Width);
        Assert.Equal(DashboardLayout.DropdownHeight, control.Height);
        Assert.Equal(controlY, control.Y);

        // PDF 清單：每一列畫在哪，就查得到哪一列，而且都在彈出框內。
        LayoutRect pdfPopup = DashboardLayout.DropdownPopupRect(
            contentX, controlY, DashboardLayout.PdfPopupHeight(itemCount));
        Assert.Equal(controlY, pdfPopup.Bottom);
        for (int i = 0; i < itemCount; i++)
        {
            int itemY = DashboardLayout.DropdownItemY(pdfPopup.Y, DashboardLayout.PdfPopupListTop, i);
            Assert.True(pdfPopup.Contains(pdfPopup.X + 1, itemY), $"PDF popup row {i} must be inside the popup.");
            Assert.Equal(i, DashboardLayout.DropdownItemAt(
                pdfPopup.Y, DashboardLayout.PdfPopupListTop, pdfPopup.Height, itemY));
        }

        // 框底的留白不對應任何一列（舊碼把它算成下一列，靠 itemCount 檢查掩著）。
        Assert.Equal(-1, DashboardLayout.DropdownItemAt(
            pdfPopup.Y, DashboardLayout.PdfPopupListTop, pdfPopup.Height, controlY - 1));
        // 彈出框上方不是清單。
        Assert.Equal(-1, DashboardLayout.DropdownItemAt(
            pdfPopup.Y, DashboardLayout.PdfPopupListTop, pdfPopup.Height, pdfPopup.Y));

        // 語言清單：搜尋框佔住清單上方的區域，那一帶不是列。
        LayoutRect langPopup = DashboardLayout.DropdownPopupRect(
            contentX, controlY, DashboardLayout.LanguagePopupHeight);
        LayoutRect search = DashboardLayout.DropdownSearchRect(langPopup.X, langPopup.Y);
        Assert.True(langPopup.Contains(search.X, search.Y) && langPopup.Contains(search.Right - 1, search.Bottom - 1),
            "The search box must sit inside the popup.");
        Assert.Equal(search.Bottom + DashboardLayout.DropdownSearchInset,
            langPopup.Y + DashboardLayout.LanguagePopupListTop);
        for (int y = search.Y; y < search.Bottom; y++)
        {
            Assert.Equal(-1, DashboardLayout.DropdownItemAt(
                langPopup.Y, DashboardLayout.LanguagePopupListTop, langPopup.Height, y));
        }

        // 每一列都待在同一個位置，而且都畫得出來。
        Assert.True(DashboardLayout.LanguagePopupListTop
                        + DashboardLayout.LanguagePopupVisibleRows * DashboardLayout.DropdownItemStride
                    <= DashboardLayout.LanguagePopupHeight,
            "Every visible language row must fit inside the popup, or the last one would be clipped.");
        for (int i = 0; i < DashboardLayout.LanguagePopupVisibleRows; i++)
        {
            int itemY = DashboardLayout.DropdownItemY(langPopup.Y, DashboardLayout.LanguagePopupListTop, i);
            Assert.True(langPopup.Contains(langPopup.X + 1, itemY), $"Language row {i} must be inside the popup.");
            Assert.Equal(i, DashboardLayout.DropdownItemAt(
                langPopup.Y, DashboardLayout.LanguagePopupListTop, langPopup.Height, itemY));
        }

        // 看不見的下一列（被 maxVisible 截掉）不能被點到。
        Assert.Equal(-1, DashboardLayout.DropdownItemAt(
            langPopup.Y, DashboardLayout.LanguagePopupListTop, langPopup.Height,
            langPopup.Y + DashboardLayout.LanguagePopupListTop
                       + DashboardLayout.LanguagePopupVisibleRows * DashboardLayout.DropdownItemStride));
    }

    /// <summary>
    /// The table only helps if every consumer asks it. This pins who reads what, and that the
    /// hand-rolled arithmetic these files used to carry is gone from all of them.
    /// </summary>
    private static void TestDashboardConsumersUseTheTable()
    {
        string dir = DashboardDir();

        foreach ((string file, string expected) in new[]
                 {
                     ("DashboardWindow.cs", "HistoryLayout.Build("),
                     ("DashboardWindow.Paint.History.cs", "HistoryLayout.Find("),
                     ("DashboardWindow.Paint.cs", "DashboardLayout.SidebarTabY("),
                     ("DashboardWindow.Convert.cs", "DashboardLayout.ConvertCardRect("),
                     ("DashboardWindow.HitTesting.cs", "DashboardLayout.SidebarTabAt("),
                     (EventsClickFileName, "DashboardLayout.DetailScrollFieldAt("),
                     (EventsFileName, "DashboardLayout.DetailScrollFieldAt("),
                     ("DashboardWindow.Paint.Dropdowns.cs", "DashboardLayout.DropdownItemY("),
                     ("DashboardWindow.Paint.Settings.cs", "DashboardLayout.DropdownButtonRect("),
                 })
        {
            string source = File.ReadAllText(Path.Combine(dir, file));
            Assert.True(source.Contains(expected, StringComparison.Ordinal),
                $"{file} must take its geometry from {expected}.");
        }

        // 這兩個檔案負責命中：一個是滑鼠點擊、一個是滾輪，兩者都必須走同一個欄位判定。
        foreach (string file in new[] { EventsClickFileName, EventsFileName })
        {
            string source = File.ReadAllText(Path.Combine(dir, file));
            Assert.True(source.Contains("DashboardLayout.DetailScrollFieldAt(", StringComparison.Ordinal),
                $"{file} must resolve detail fields through the shared tap bands.");
        }

        // 下拉清單的列號也只有一份：繪製、點擊與 hover 都問同一個對應。
        foreach (string file in new[] { "DashboardWindow.Paint.Dropdowns.cs", EventsClickFileName, EventsFileName })
        {
            string source = File.ReadAllText(Path.Combine(dir, file));
            Assert.True(source.Contains("DashboardLayout.DropdownItem", StringComparison.Ordinal),
                $"{file} must resolve dropdown rows through the shared layout.");
        }

        // 舊的自算算式不得回來：任何一份都足以讓繪製與命中等分。
        foreach (string file in Directory.GetFiles(dir, "DashboardWindow*.cs"))
        {
            string name = Path.GetFileName(file);
            string source = File.ReadAllText(file);
            foreach (string gone in new[]
                     {
                         "GetHistoryListStartY", "HistoryRowStride", "ParkedBlockHeight", "ParkedBlockHeaderHeight",
                         "GetHistoryRowHeight", "GetDetailFieldIndexFromY", "GetHistoryDetailWheelFieldIndex",
                         "isExpanded ? 160 : 44", "* 52",
                         "popupY + 38", "popupY + 4", "* 26", "popupH = 180", "240, 30",
                     })
            {
                Assert.False(source.Contains(gone, StringComparison.Ordinal),
                    $"{name} still carries its own layout arithmetic ({gone}); it belongs in the Core table.");
            }
        }
    }

    private static void TestSidebarTabCountMatchesPaint()
    {
        string paint = File.ReadAllText(Path.Combine(DashboardDir(), "DashboardWindow.Paint.cs"));

        Assert.Equal(DashboardLayout.SidebarTabCount, CountOccurrences(paint, "DrawTabButton(g, "));

        // 籤的 Y 不得寫在呼叫端（那正是原本與命中檔各存一份的東西）。
        foreach (int tabY in new[] { 120, 168, 216, 264, 312 })
        {
            Assert.False(paint.Contains($", {tabY}, sidebarW)", StringComparison.Ordinal),
                $"Tab position {tabY} must come from DashboardLayout.SidebarTabY, not from the call site.");
        }

        // 畫出來的索引必須正好是 0..N-1，順序就是籤的順序。
        for (int index = 0; index < DashboardLayout.SidebarTabCount; index++)
        {
            Assert.True(paint.Contains($", {index}, sidebarW)", StringComparison.Ordinal),
                $"The sidebar must draw tab index {index}; the table hit-tests that many.");
        }
    }

    /// <summary>待繼續任務列右端的四個動作按鈕（縮短、延長、繼續、取消）：必須落在自己的列裡、彼此不重疊，
    /// 而且靠右的期限文字還要有位置 —— 否則「還剩幾天」那句話會被按鈕蓋掉。</summary>
    private static void TestParkedActionButtonGeometry()
    {
        const int contentX = 200;
        const int rowW = 520;
        int rowTop = DashboardLayout.ContentTop;
        int rowHeight = DashboardLayout.RowHeight;

        LayoutRect shorten = DashboardLayout.ParkedRowActionRect(contentX, rowW, rowTop, rowHeight, 0);
        LayoutRect extend = DashboardLayout.ParkedRowActionRect(contentX, rowW, rowTop, rowHeight, 1);
        LayoutRect resume = DashboardLayout.ParkedRowActionRect(contentX, rowW, rowTop, rowHeight, 2);
        LayoutRect cancel = DashboardLayout.ParkedRowActionRect(contentX, rowW, rowTop, rowHeight, 3);

        foreach ((string name, LayoutRect rect) in new[] { ("shorten", shorten), ("extend", extend), ("resume", resume), ("cancel", cancel) })
        {
            Assert.True(rect.X >= contentX && rect.Right <= contentX + rowW,
                $"The {name} button must stay inside the row.");
            Assert.True(rect.Y >= rowTop && rect.Bottom <= rowTop + rowHeight,
                $"The {name} button must stay inside the row's height.");
            Assert.True(rect.Height <= rowHeight && rect.Width > 0,
                $"The {name} button must fit the row height.");
        }

        Assert.True(shorten.Right + DashboardLayout.ParkedActionButtonGap <= extend.X,
            "The shorten and extend buttons must not overlap; the gap is part of the table.");
        Assert.True(extend.Right + DashboardLayout.ParkedActionButtonGap <= resume.X,
            "The extend and resume buttons must not overlap; the gap is part of the table.");
        Assert.True(resume.Right + DashboardLayout.ParkedActionButtonGap <= cancel.X,
            "The resume and cancel buttons must not overlap; the gap is part of the table.");
        Assert.True(cancel.Right <= contentX + rowW - DashboardLayout.ParkedActionRightMargin,
            "The buttons must keep the row's right margin.");

        int ttlRight = DashboardLayout.ParkedRowTtlRight(contentX, rowW);
        Assert.True(ttlRight + DashboardLayout.ParkedActionTextGap <= shorten.X,
            "The deadline text must stop before the adjust buttons, with the table's gap in between.");
        Assert.True(ttlRight > contentX + rowW / 2,
            "The deadline belongs on the right half of the row, next to the buttons that change it.");
    }

    private static string DashboardDir()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");
        return Path.Combine(root, "src", "Clickra.CLI", "Dashboard");
    }

    private static void AssertRowsDoNotOverlap(HistoryBlock block, string what)
    {
        for (int i = 1; i < block.RowCount; i++)
        {
            Assert.True(block.RowTop(i) >= block.RowTop(i - 1) + block.RowHeight(i - 1) + DashboardLayout.RowGap,
                $"{what}: row {i} overlaps the row above it.");
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
