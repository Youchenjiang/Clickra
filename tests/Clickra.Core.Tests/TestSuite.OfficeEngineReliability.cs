using Clickra.Core.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string NullDisplay = "<null>";
    private const string OfficeTestLanguage = "en-US";

    private static void WithRestoredOfficeSettings(Action action)
    {
        string originalEngine = ClickraStorage.GetSetting(ClickraSettings.OfficeEngine);
        string originalLanguage = ClickraStorage.GetSetting(ClickraSettings.Language);
        string originalLibreOfficePath = ClickraStorage.GetSetting(ClickraSettings.LibreOfficePath);
        string originalRemovalPending = ClickraStorage.GetSetting(ClickraSettings.LibreOfficeRemovalPendingRestart);
        try
        {
            action();
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, originalEngine);
            ClickraStorage.SaveSetting(ClickraSettings.Language, originalLanguage);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, originalLibreOfficePath);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, originalRemovalPending);
        }
    }

    private static void SetUnavailableOfficeTestSettings(string path)
    {
        ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineLibreOffice);
        ClickraStorage.SaveSetting(ClickraSettings.Language, OfficeTestLanguage);
        ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, path);
        ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueTrue);
    }

    public static void RegisterOfficeEngineReliabilityTests(TestRunner runner)
    {
        runner.Run("Excel to PDF use case owns legacy source-directory planning", () =>
            AssertOfficeUseCasePlanning(
                new ExcelToPdfUseCase(),
                ExcelToPdfUseCase.CommandName,
                "budget.xlsx",
                "placeholder"));

        runner.Run("PowerPoint to PDF use case owns legacy source-directory planning", () =>
            AssertOfficeUseCasePlanning(
                new PptToPdfUseCase(),
                PptToPdfUseCase.CommandName,
                "slides.pptx",
                "placeholder"));

        runner.Run("Word to PDF use case owns legacy source-directory planning", () =>
            AssertOfficeUseCasePlanning(
                new WordToPdfUseCase(),
                WordToPdfUseCase.CommandName,
                "report.docx",
                "fixture"));

        runner.Run("Office conversion planning snapshots saved execution settings", () =>
            WithRestoredOfficeSettings(() =>
            {
                SetUnavailableOfficeTestSettings(@"C:\snapshot\soffice.exe");

                RunWithTempDirectory(tempDir =>
                {
                    string input = Path.Combine(tempDir, "snapshot.docx");
                    File.WriteAllText(input, "fixture");
                    ConversionPlan plan = new WordToPdfUseCase().Plan(new ConversionRequest(
                        WordToPdfUseCase.CommandName,
                        new[] { input },
                        TrackTaskLifecycle: false));

                    ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineMicrosoft);
                    ClickraStorage.SaveSetting(ClickraSettings.Language, "zh-TW");
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, @"C:\changed\soffice.exe");
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);

                    Assert.Equal(ClickraSettings.OfficeEngineLibreOffice, plan.NormalizedOptions["office_engine"].ToString() ?? "");
                    Assert.Equal(OfficeTestLanguage, plan.NormalizedOptions["office_language"].ToString() ?? "");
                    Assert.Equal(@"C:\snapshot\soffice.exe", plan.NormalizedOptions["libreoffice_path"].ToString() ?? "");
                    Assert.True(
                        plan.NormalizedOptions["libreoffice_removal_pending_restart"] is bool removalPending && removalPending,
                        "Office conversion must retain the removal-pending value captured during planning.");
                });
            }));

        runner.Run("Office execution enforces readiness from the planned settings snapshot", () =>
            WithRestoredOfficeSettings(() =>
            {
                SetUnavailableOfficeTestSettings(@"C:\planned\soffice.exe");

                RunWithTempDirectory(tempDir =>
                {
                    string input = Path.Combine(tempDir, "not-executed.docx");
                    File.WriteAllText(input, "fixture");
                    var useCase = new WordToPdfUseCase();
                    ConversionPlan plan = useCase.Plan(new ConversionRequest(
                        WordToPdfUseCase.CommandName,
                        new[] { input },
                        TrackTaskLifecycle: false));

                    // Mutating live settings after planning must not make execution use a different policy snapshot.
                    ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineMicrosoft);
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);

                    var interaction = new DelegateConversionInteraction(
                        (_, _, _, _) => Task.FromResult<string?>(null),
                        (_, _, _) => Task.FromResult<string?>(null),
                        (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
                    ConversionResult result = useCase.ExecuteAsync(plan, interaction, null)
                        .GetAwaiter()
                        .GetResult();

                    Assert.True(
                        result.Status == ConversionResultStatus.Failed,
                        $"Expected readiness failure, got {result.Status}.");
                    Assert.Equal(
                        Localization.T("error_libreoffice_not_ready", OfficeTestLanguage),
                        result.Error ?? "");
                    Assert.False(File.Exists(plan.Outputs[0]),
                        "Office readiness failure must stop before processor execution creates output.");
                });
            }));

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

        runner.Run("Office readiness applies an explicit engine policy", () =>
        {
            Assert.True(OfficeEngineDetector.IsSelectedEngineReady(ClickraSettings.DefaultOfficeEngineAuto, true, false), "Auto accepts Microsoft Office.");
            Assert.True(OfficeEngineDetector.IsSelectedEngineReady(ClickraSettings.DefaultOfficeEngineAuto, false, true), "Auto accepts LibreOffice.");
            Assert.False(OfficeEngineDetector.IsSelectedEngineReady(ClickraSettings.DefaultOfficeEngineAuto, false, false), "Auto rejects when no engine is available.");

            Assert.True(OfficeEngineDetector.IsSelectedEngineReady(ClickraSettings.OfficeEngineMicrosoft, true, false), "Microsoft mode requires Microsoft Office.");
            Assert.False(OfficeEngineDetector.IsSelectedEngineReady(ClickraSettings.OfficeEngineMicrosoft, false, true), "Microsoft mode must not silently select LibreOffice.");
            Assert.Equal("error_microsoftoffice_not_ready", OfficeEngineDetector.GetUnavailableErrorKey(ClickraSettings.OfficeEngineMicrosoft));

            Assert.True(OfficeEngineDetector.IsSelectedEngineReady(ClickraSettings.OfficeEngineLibreOffice, false, true), "LibreOffice mode requires LibreOffice.");
            Assert.False(OfficeEngineDetector.IsSelectedEngineReady(ClickraSettings.OfficeEngineLibreOffice, true, false), "LibreOffice mode must not silently select Microsoft Office.");
            Assert.Equal("error_libreoffice_not_ready", OfficeEngineDetector.GetUnavailableErrorKey(ClickraSettings.OfficeEngineLibreOffice));
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

        runner.Run("Office fallback policy recognizes recoverable Microsoft launch failures", () =>
        {
            Exception[] recoverable =
            {
                new InvalidOperationException("Office interop failed."),
                new NotSupportedException("Office application is unsupported."),
                new TimeoutException("Office interop timed out."),
                new System.ComponentModel.Win32Exception(),
                new IOException("Office output cannot be written."),
                new UnauthorizedAccessException("Office output access denied."),
                new System.Security.SecurityException()
            };

            foreach (Exception exception in recoverable)
            {
                Assert.True(
                    PowerShellHelper.IsRecoverableMicrosoftOfficeFailure(exception),
                    $"{exception.GetType().Name} should allow the automatic engine to recover through LibreOffice.");
            }

            Assert.False(
                PowerShellHelper.IsRecoverableMicrosoftOfficeFailure(
                    new PowerShellHelper.OfficeProcessTerminationException("Timed-out Office process still running.")),
                "A process that could not be terminated must never trigger concurrent LibreOffice fallback.");

            Assert.False(
                PowerShellHelper.IsRecoverableMicrosoftOfficeFailure(new OperationCanceledException()),
                "Cancellation must remain terminal and must not launch a fallback engine.");
            Assert.False(
                PowerShellHelper.IsRecoverableMicrosoftOfficeFailure(new ArgumentException("Invalid Office conversion input.")),
                "Programming/input errors must not be hidden by a fallback engine.");
        });
    }
}
