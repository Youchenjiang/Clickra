using Clickra.UI.Layout;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterMarkdownOptionsLayoutTests(TestRunner runner)
    {
        runner.Run("Markdown options layout: exact legacy button positions", () =>
        {
            Assert.Equal(442, MarkdownOptionsLayout.ClientHeight);
            AssertMarkdownRect(new LayoutRect(36, 126, 448, 36), MarkdownOptionsLayout.HintRect);
            AssertMarkdownRect(new LayoutRect(36, 210, 106, 30), MarkdownOptionsLayout.LabelRect(210));
            AssertMarkdownRect(new LayoutRect(154, 210, 132, 30), MarkdownOptionsLayout.TemplateBrowseRect);
            AssertMarkdownRect(new LayoutRect(296, 210, 188, 30), MarkdownOptionsLayout.TemplateStatusRect);
            AssertMarkdownRect(new LayoutRect(254, 398, 120, 32), MarkdownOptionsLayout.ConfirmRect);
            AssertMarkdownRect(new LayoutRect(386, 398, 98, 32), MarkdownOptionsLayout.CancelRect);
            foreach (int y in new[] { 168, 252, 336 })
            {
                AssertMarkdownRect(new LayoutRect(154, y, 162, 30), MarkdownOptionsLayout.RowButtonRect(y, 2, 0));
                AssertMarkdownRect(new LayoutRect(322, y, 162, 30), MarkdownOptionsLayout.RowButtonRect(y, 2, 1));
            }
            foreach (int y in new[] { 210, 294 })
            {
                int width = (330 - 12) / 3;
                for (int i = 0; i < 3; i++)
                    AssertMarkdownRect(new LayoutRect(154 + i * (width + 6), y, width, 30),
                        MarkdownOptionsLayout.RowButtonRect(y, 3, i));
            }
        });

        runner.Run("Markdown options layout: inclusive edges and gaps match legacy clicks", () =>
        {
            foreach ((int y, int count) in new[] { (168, 2), (210, 3), (252, 2), (294, 3), (336, 2) })
            {
                for (int i = 0; i < count; i++)
                {
                    LayoutRect rect = MarkdownOptionsLayout.RowButtonRect(y, count, i);
                    int x = rect.X;
                    Assert.True(MarkdownOptionsLayout.TryHitRow(x, y, y, count, out int topIndex) && topIndex == i,
                        $"Row {y}, button {i} must include its upper-left corner.");
                    Assert.True(MarkdownOptionsLayout.TryHitRow(x, y + 30, y, count, out int bottomIndex) && bottomIndex == i,
                        $"Row {y}, button {i} must include its bottom edge.");
                    Assert.True(rect.ContainsInclusive(x, y + 30), "Bottom edge is inclusive.");
                    Assert.True(!rect.ContainsInclusive(x, y + 31), "Below the bottom edge is excluded.");
                    if (i < count - 1)
                    {
                        int gapX = rect.Right + 2;
                        Assert.True(!MarkdownOptionsLayout.TryHitRow(gapX, y + 15, y, count, out _),
                            $"Row {y} must preserve the gap between buttons.");
                    }
                }
                Assert.True(!MarkdownOptionsLayout.TryHitRow(154, y - 1, y, count, out _), "Above row excluded.");
                Assert.True(!MarkdownOptionsLayout.TryHitRow(154, y + 31, y, count, out _), "Below row excluded.");
            }
            foreach (LayoutRect button in new[] { MarkdownOptionsLayout.TemplateBrowseRect, MarkdownOptionsLayout.ConfirmRect, MarkdownOptionsLayout.CancelRect })
            {
                Assert.True(button.ContainsInclusive(button.Right, button.Bottom),
                    "Legacy action buttons include the exact right-bottom corner.");
                Assert.True(!button.ContainsInclusive(button.Right + 1, button.Bottom),
                    "Beyond right edge excluded.");
            }
        });
    }

    private static void AssertMarkdownRect(LayoutRect expected, LayoutRect actual)
    {
        Assert.True(expected.X == actual.X && expected.Y == actual.Y &&
            expected.Width == actual.Width && expected.Height == actual.Height,
            $"Expected rectangle ({expected.X}, {expected.Y}, {expected.Width}, {expected.Height}), " +
            $"actual ({actual.X}, {actual.Y}, {actual.Width}, {actual.Height}).");
    }
}
