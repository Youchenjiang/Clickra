using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterOfficeEngineReliabilityTests(TestRunner runner)
    {
        runner.Run("Office readiness maps supported app types before COM lookup", () =>
        {
            Assert.Equal(
                "Word.Application",
                PowerShellHelper.GetMicrosoftOfficeProgId("Word") ?? "<null>");
            Assert.Equal(
                "Excel.Application",
                PowerShellHelper.GetMicrosoftOfficeProgId("Excel") ?? "<null>");
            Assert.Equal(
                "PowerPoint.Application",
                PowerShellHelper.GetMicrosoftOfficeProgId("PowerPoint") ?? "<null>");
            Assert.True(
                PowerShellHelper.GetMicrosoftOfficeProgId("Unknown") is null,
                "Unknown Office application types must not map to a COM ProgID.");
        });

        runner.Run("Office readiness COM probe executes for supported app types", () =>
        {
            // Installed Office availability is environment-dependent, so these are smoke calls.
            // The mapping semantics are asserted separately above.
            _ = PowerShellHelper.IsMicrosoftOfficeReady("Word");
            _ = PowerShellHelper.IsMicrosoftOfficeReady("Excel");
            _ = PowerShellHelper.IsMicrosoftOfficeReady("PowerPoint");
            Assert.False(
                PowerShellHelper.IsMicrosoftOfficeReady("Unknown"),
                "Unknown Office application types must not be reported as registered.");
        });

        runner.Run("Office fallback policy never retries a cancelled job", () =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.False(
                PowerShellHelper.ShouldFallBackToLibreOffice("auto", "Word", cancelled.Token),
                "A cancelled Office job must not launch LibreOffice.");
            Assert.False(
                PowerShellHelper.ShouldFallBackToLibreOffice("microsoft", "Word", CancellationToken.None),
                "A pinned Microsoft engine must report its own failure.");
            Assert.True(
                PowerShellHelper.ShouldFallBackToLibreOffice("auto", "Word", CancellationToken.None),
                "An uncancelled automatic Word conversion may recover through LibreOffice.");
        });
    }
}
