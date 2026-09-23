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
    }
}
