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
            Assert.True(VisualSplitterLayout.HitInclusive(VisualSplitterLayout.ModeButton(0), 176, 128), "Right and bottom edge inclusive");
            Assert.False(VisualSplitterLayout.HitInclusive(VisualSplitterLayout.ModeButton(0), 177, 128), "One pixel beyond is excluded");
            Assert.Equal(136, VisualSplitterLayout.PagesButton(true).X);
            Assert.Equal(132, VisualSplitterLayout.PagesHitButton(true).X);
            Assert.Equal(158, VisualSplitterLayout.SegmentCard(0, false).Y);
            Assert.Equal(180, VisualSplitterLayout.SegmentCard(0, true).Y);
            Assert.Equal(410, VisualSplitterLayout.NavButton(2, false).X);
            Assert.Equal(454, VisualSplitterLayout.NavButton(1, false).X);
            Assert.Equal(452, VisualSplitterLayout.NavHitButton(1, false).X);
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
    }
}
