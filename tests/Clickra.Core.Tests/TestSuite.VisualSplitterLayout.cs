using Clickra.UI.Layout;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterVisualSplitterLayoutTests(TestRunner runner)
    {
        runner.Run("Visual splitter layout: legacy paint dimensions and inclusive hit edges", () =>
        {
            Assert.Equal(36, VisualSplitterLayout.ModeButton(0).X);
            Assert.Equal(184, VisualSplitterLayout.ModeButton(1).X);
            Assert.Equal(332, VisualSplitterLayout.ModeButton(2).X);
            Assert.Equal(152, VisualSplitterLayout.ModeButton(2).Width);
            Assert.Equal(26, VisualSplitterLayout.ModeButton(0).Height);
            Assert.True(VisualSplitterLayout.ModeButton(0).ContainsInclusive(176, 128), "Right and bottom edge inclusive");
            Assert.False(VisualSplitterLayout.ModeButton(0).ContainsInclusive(177, 128), "One pixel beyond is excluded");
            Assert.Equal(136, VisualSplitterLayout.PagesButton(true).X);
            Assert.Equal(158, VisualSplitterLayout.SegmentCard(0, false).Y);
            Assert.Equal(180, VisualSplitterLayout.SegmentCard(0, true).Y);
            Assert.Equal(410, VisualSplitterLayout.NavButton(2, false).X);
            Assert.Equal(454, VisualSplitterLayout.NavButton(1, false).X);
            Assert.Equal(454, VisualSplitterLayout.NavHitButton(1, false).X);
            Assert.Equal(336, VisualSplitterLayout.ActionButton(3).X);
            Assert.Equal(366, VisualSplitterLayout.ZoomButton(0).Y);
        });
        runner.Run("Visual splitter layout: action and mode buttons do not overlap", () =>
        {
            for (int i = 0; i < 2; i++)
                Assert.True(VisualSplitterLayout.ModeButton(i).Right < VisualSplitterLayout.ModeButton(i + 1).X, "Modes must not overlap");
            for (int i = 0; i < 4; i++)
                Assert.True(VisualSplitterLayout.ActionButton(i).Right < VisualSplitterLayout.ActionButton(i + 1).X, "Actions must not overlap");
        });
        runner.Run("Visual splitter layout: drawn controls stay fully clickable", () =>
        {
            foreach (bool fixedPages in new[] { false, true })
            {
                for (int i = 0; i < 3; i++)
                {
                    LayoutRect drawn = VisualSplitterLayout.NavButton(i, fixedPages);
                    LayoutRect hit = VisualSplitterLayout.NavHitButton(i, fixedPages);
                    Assert.True(hit.ContainsInclusive(drawn.X, drawn.Y) && hit.ContainsInclusive(drawn.Right, drawn.Bottom),
                        $"Navigation button {i} must be clickable across its whole painted area.");
                }
                LayoutRect preview = VisualSplitterLayout.PreviewArea(fixedPages);
                LayoutRect hitPreview = VisualSplitterLayout.PreviewHitArea(fixedPages);
                Assert.True(hitPreview.ContainsInclusive(preview.X, preview.Y) && hitPreview.ContainsInclusive(preview.Right, preview.Bottom),
                    "Preview hit area must enclose its painted frame.");
            }
            Assert.True(!VisualSplitterLayout.PagesButton(true).ContainsInclusive(132, 140), "Blank area left of plus is excluded.");
            Assert.True(VisualSplitterLayout.PagesButton(true).ContainsInclusive(160, 148), "Full painted plus button is included.");
            Assert.True(VisualSplitterLayout.PagesButton(false).ContainsInclusive(60, 148), "Full painted minus button is included.");
        });
    }
}
