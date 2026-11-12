using System;
using System.IO;
using System.Runtime.InteropServices;
using Clickra.Core.Modules.Sys.Settings.Presentation;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterCliHeadlessTests(TestRunner runner)
    {
        runner.Run("CLI headless: source guards ensure cross-platform safe execution and option handling", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

            string cliStartupPath = Path.Combine(root, "src", CliProjectDirectory, "Cli", "ClickraStartup.cs");
            string cliStartupCode = File.ReadAllText(cliStartupPath);

            Assert.True(cliStartupCode.Contains("--headless", StringComparison.Ordinal),
                "ClickraStartup must recognize the --headless command-line flag.");
            Assert.True(cliStartupCode.Contains("SettingsController.QuietMode || !OperatingSystem.IsWindows()", StringComparison.Ordinal),
                "ClickraStartup must default to headless quiet execution on non-Windows platforms.");
            Assert.True(cliStartupCode.Contains("--info", StringComparison.Ordinal) &&
                        cliStartupCode.Contains("--diagnostics", StringComparison.Ordinal),
                "ClickraStartup must support --info and --diagnostics flags.");
            Assert.True(cliStartupCode.Contains("PrintPlatformDiagnostics()", StringComparison.Ordinal),
                "ClickraStartup must implement platform and engine diagnostics.");

            string cliMainPath = Path.Combine(root, "src", CliProjectDirectory, "Cli", "ClickraCli.cs");
            string cliMainCode = File.ReadAllText(cliMainPath);

            Assert.True(cliMainCode.Contains("ShowProgressWindow(", StringComparison.Ordinal),
                "ClickraCli must route GUI progress window invocations through ShowProgressWindow.");
            Assert.True(cliMainCode.Contains("OperatingSystem.IsWindows()", StringComparison.Ordinal),
                "ClickraCli must guard GUI progress window execution with OperatingSystem.IsWindows().");

            string cliArgsPath = Path.Combine(root, "src", CliProjectDirectory, "Cli", "ClickraCli.Arguments.cs");
            string cliArgsCode = File.ReadAllText(cliArgsPath);

            Assert.True(cliArgsCode.Contains("!OperatingSystem.IsWindows() || args.Length == 0", StringComparison.Ordinal),
                "AttachParentConsoleForCli must guard console attachment with OperatingSystem.IsWindows().");
            Assert.True(cliArgsCode.Contains("[Progress]", StringComparison.Ordinal),
                "WriteConsoleProgress must render a formatted progress indicator.");
            Assert.True(cliArgsCode.Contains("FormatInteractiveProgress", StringComparison.Ordinal) &&
                        cliArgsCode.Contains("BuildPlainBar", StringComparison.Ordinal),
                "WriteConsoleProgress must delegate formatting to dedicated helpers to preserve low complexity.");
            Assert.True(cliArgsCode.Contains("Math.Clamp(consoleWidth - 25", StringComparison.Ordinal),
                "FormatInteractiveProgress must adapt progress bar width dynamically to prevent line wrapping at narrow console widths.");
        });

        runner.Run("CLI headless: platform and office engine readiness queries succeed without throwing", () =>
        {
            var readiness = SettingsPresentationController.Shared.GetOfficeReadiness();
            Assert.True(readiness is not null, "Office readiness presentation state must not be null.");
            Assert.True(readiness!.PreferredReadyEngine is not null, "Preferred ready engine must not be null.");

            var loState = SettingsPresentationController.Shared.GetLibreOfficeState();
            Assert.True(loState is not null, "LibreOffice presentation state must not be null.");

            Assert.False(string.IsNullOrWhiteSpace(RuntimeInformation.OSDescription),
                "RuntimeInformation.OSDescription must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(RuntimeInformation.FrameworkDescription),
                "RuntimeInformation.FrameworkDescription must not be empty.");
        });
    }
}
