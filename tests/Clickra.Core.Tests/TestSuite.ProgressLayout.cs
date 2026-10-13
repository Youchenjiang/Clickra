using Clickra.UI.Layout;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterProgressLayoutTests(TestRunner runner)
    {
        runner.Run("Native progress layout: tray paint and inclusive hit bounds", () =>
        {
            LayoutRect r = ProgressLayout.TrayButton;
            Assert.Equal(456, r.X);
            Assert.Equal(36, r.Y);
            Assert.Equal(28, r.Width);
            Assert.Equal(28, r.Height);
            Assert.True(ProgressLayout.TrayButton.ContainsInclusive(484, 64), "Legacy lower-right corner is clickable.");
            Assert.True(!ProgressLayout.TrayButton.ContainsInclusive(485, 64), "Outside right edge is excluded.");
            Assert.True(new LayoutRect(10, 20, 30, 40).ContainsInclusive(40, 60), "Inclusive includes right/bottom.");
            Assert.True(!new LayoutRect(10, 20, 30, 40).Contains(40, 60), "Normal Contains excludes right/bottom.");
            Assert.True(!new LayoutRect(10, 20, 30, 40).ContainsInclusive(41, 60), "Next x pixel excluded.");
            Assert.Equal(448, ProgressLayout.MessageMaxWidth);
            Assert.Equal(152, ProgressLayout.ScrollbarDrawY);
            Assert.True(ProgressLayout.MessageAvailableWidth(0) == 448f, "Unrestricted message width is 448.");
            Assert.True(ProgressLayout.MessageAvailableWidth(18) == 420f, "Percentage label reserves width and gap.");
            Assert.True(ProgressLayout.ScrollbarHit(456, 158, 420f),
                "Scrollbar includes its legacy right/bottom boundary.");
            Assert.True(!ProgressLayout.ScrollbarHit(456, 159, 420f),
                "Scrollbar excludes the first pixel below.");
        });
        runner.Run("Native about layout: dynamic widths and 32-pixel height are shared", () =>
        {
            LayoutRect r = DashboardLayout.AboutButtonRect(152, 240, 135);
            Assert.Equal(32, DashboardLayout.AboutButtonHeight);
            Assert.Equal(135, r.Width);
            Assert.Equal(272, r.Bottom);
            Assert.True(DashboardLayout.AboutButtonHit(152.5f, 240.5f, 135.25f, 153, 241),
                "Fractional text widths are retained for hit testing.");
            Assert.True(!DashboardLayout.AboutButtonHit(152.5f, 240.5f, 135.25f, 288, 241),
                "Outside the dynamic width is excluded.");
        });
    }
}
