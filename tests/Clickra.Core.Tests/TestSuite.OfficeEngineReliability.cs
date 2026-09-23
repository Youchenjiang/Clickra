using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterOfficeEngineReliabilityTests(TestRunner runner)
    {
        runner.Run("Office readiness probe supports NativeAOT-safe COM lookup", () =>
        {
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
