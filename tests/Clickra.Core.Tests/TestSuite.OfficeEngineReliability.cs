using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string NullDisplay = "<null>";

    public static void RegisterOfficeEngineReliabilityTests(TestRunner runner)
    {
        runner.Run("Office readiness maps commands and supported app types", () =>
        {
            Assert.Equal(
                "Word.Application",
                OfficeEngineDetector.GetMicrosoftOfficeProgId("Word") ?? NullDisplay);
            Assert.Equal(
                "Excel.Application",
                OfficeEngineDetector.GetMicrosoftOfficeProgId("Excel") ?? NullDisplay);
            Assert.Equal(
                "PowerPoint.Application",
                OfficeEngineDetector.GetMicrosoftOfficeProgId("PowerPoint") ?? NullDisplay);
            Assert.True(
                OfficeEngineDetector.GetMicrosoftOfficeProgId("Unknown") is null,
                "Unknown Office application types must not map to a COM ProgID.");
        });

        runner.Run("Office readiness registry probe executes for supported app types", () =>
        {
            // Installed Office availability is environment-dependent, so these are smoke calls.
            // The mapping semantics are asserted separately above.
            _ = OfficeEngineDetector.IsMicrosoftOfficeReady("Word");
            _ = OfficeEngineDetector.IsMicrosoftOfficeReady("Excel");
            _ = OfficeEngineDetector.IsMicrosoftOfficeReady("PowerPoint");
            Assert.False(
                OfficeEngineDetector.IsMicrosoftOfficeReady("Unknown"),
                "Unknown Office application types must not be reported as registered.");
        });

        runner.Run("Office readiness maps conversion commands to Office applications", () =>
        {
            Assert.Equal("Word", OfficeEngineDetector.GetOfficeAppForCommand("word2pdf") ?? NullDisplay);
            Assert.Equal("Excel", OfficeEngineDetector.GetOfficeAppForCommand("excel2pdf") ?? NullDisplay);
            Assert.Equal("PowerPoint", OfficeEngineDetector.GetOfficeAppForCommand("ppt2pdf") ?? NullDisplay);
            Assert.True(
                OfficeEngineDetector.GetOfficeAppForCommand("merge-pdf") is null,
                "Non-Office commands must not require an Office application.");
        });

        runner.Run("Office readiness applies the configured engine policy once", () =>
        {
            string original = ClickraStorage.GetSetting(ClickraSettings.OfficeEngine);
            try
            {
                ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.DefaultOfficeEngineAuto);
                Assert.True(OfficeEngineDetector.IsSelectedEngineReady(true, false), "Auto accepts Microsoft Office.");
                Assert.True(OfficeEngineDetector.IsSelectedEngineReady(false, true), "Auto accepts LibreOffice.");
                Assert.False(OfficeEngineDetector.IsSelectedEngineReady(false, false), "Auto rejects when no engine is available.");

                ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineMicrosoft);
                Assert.True(OfficeEngineDetector.IsSelectedEngineReady(true, false), "Microsoft mode requires Microsoft Office.");
                Assert.False(OfficeEngineDetector.IsSelectedEngineReady(false, true), "Microsoft mode must not silently select LibreOffice.");
                Assert.Equal("error_microsoftoffice_not_ready", OfficeEngineDetector.GetUnavailableErrorKey());

                ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineLibreOffice);
                Assert.True(OfficeEngineDetector.IsSelectedEngineReady(false, true), "LibreOffice mode requires LibreOffice.");
                Assert.False(OfficeEngineDetector.IsSelectedEngineReady(true, false), "LibreOffice mode must not silently select Microsoft Office.");
                Assert.Equal("error_libreoffice_not_ready", OfficeEngineDetector.GetUnavailableErrorKey());
            }
            finally
            {
                ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, original);
            }
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
