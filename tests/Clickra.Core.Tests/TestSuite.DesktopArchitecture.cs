using System;
using System.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string DesktopProjectDir = "Clickra.Desktop";

    public static void RegisterDesktopArchitectureTests(TestRunner runner)
    {
        runner.RunGuard("Universal desktop: project contracts enforce Avalonia 11 and NativeAOT specifications", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

            // 1. Verify Desktop project exists and targets net8.0
            string projPath = Path.Combine(root, "src", DesktopProjectDir, "Clickra.Desktop.csproj");
            Assert.True(File.Exists(projPath), "Clickra.Desktop.csproj must exist.");

            string projText = File.ReadAllText(projPath);
            Assert.True(projText.Contains("<TargetFramework>net8.0</TargetFramework>", StringComparison.Ordinal),
                "Clickra.Desktop must target universal net8.0.");
            Assert.True(projText.Contains("<PublishAot>true</PublishAot>", StringComparison.Ordinal),
                "Clickra.Desktop must enable PublishAot for NativeAOT compilation.");
            Assert.True(projText.Contains("Avalonia", StringComparison.Ordinal) && projText.Contains("11.", StringComparison.Ordinal),
                "Clickra.Desktop must reference Avalonia 11 packages.");
            Assert.True(projText.Contains("Clickra.Core.csproj", StringComparison.Ordinal),
                "Clickra.Desktop must reference Clickra.Core.");
            Assert.False(projText.Contains("Clickra.Fluent", StringComparison.Ordinal),
                "Clickra.Desktop must not reference Windows-specific Clickra.Fluent.");
            Assert.False(projText.Contains("ClickraShell", StringComparison.Ordinal),
                "Clickra.Desktop must not reference Windows-specific ClickraShell.");

            // 2. Verify specifications consistently reference Clickra.Desktop instead of legacy Clickra.App
            string roadmapPath = Path.Combine(root, "docs", "ROADMAP.md");
            if (File.Exists(roadmapPath))
            {
                Assert.False(File.ReadAllText(roadmapPath).Contains("Clickra.App", StringComparison.Ordinal),
                    "docs/ROADMAP.md must reference Clickra.Desktop instead of legacy Clickra.App.");
            }

            string specPath = Path.Combine(root, "docs", "specs", "v4_cross_platform_unification.md");
            if (File.Exists(specPath))
            {
                Assert.False(File.ReadAllText(specPath).Contains("Clickra.App", StringComparison.Ordinal),
                    "v4_cross_platform_unification.md must reference Clickra.Desktop instead of legacy Clickra.App.");
            }
        });

        runner.RunGuard("Universal desktop: build script and manifest enforce standalone execution contracts", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

            // 1. Verify build script
            string scriptPath = Path.Combine(root, "scripts", "build_desktop.ps1");
            Assert.True(File.Exists(scriptPath), "scripts/build_desktop.ps1 must exist.");

            string scriptText = File.ReadAllText(scriptPath);
            Assert.True(scriptText.Contains("publish", StringComparison.OrdinalIgnoreCase) &&
                        scriptText.Contains("win-x64", StringComparison.Ordinal),
                "build_desktop.ps1 must publish win-x64 NativeAOT standalone executable.");

            // 2. Verify manifest DPI awareness
            string manifestPath = Path.Combine(root, "src", DesktopProjectDir, "app.manifest");
            Assert.True(File.Exists(manifestPath), "app.manifest must exist.");

            string manifestText = File.ReadAllText(manifestPath);
            Assert.True(manifestText.Contains("PerMonitorV2", StringComparison.Ordinal),
                "Desktop app.manifest must declare PerMonitorV2 DPI awareness.");
        });
    }
}
