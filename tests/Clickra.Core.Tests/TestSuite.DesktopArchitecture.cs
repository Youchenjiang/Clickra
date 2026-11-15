using System;
using System.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string DesktopProjectDir = "Clickra.Desktop";

    public static void RegisterDesktopArchitectureTests(TestRunner runner)
    {
        runner.RunGuard("Universal desktop: project contracts enforce Avalonia 11 and NativeAOT specifications", GuardDesktopProjectContracts);
        runner.RunGuard("Universal desktop: build script and manifest enforce standalone execution contracts", GuardDesktopBuildAndManifest);
        runner.RunGuard("Universal desktop: launcher preserves packaged fluent priority and isolates desktop preview contracts", GuardDesktopLauncherRouting);
        runner.RunGuard("Universal desktop: motion and shell navigation wire destination highlighting and dynamic motion preference contracts", GuardDesktopMotionAndNavigation);
    }

    private static void GuardDesktopProjectContracts()
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
    }

    private static void GuardDesktopBuildAndManifest()
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
    }

    private static void GuardDesktopLauncherRouting()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string launcherPath = Path.Combine(root, "src", "ClickraLauncher", "Program.cs");
        Assert.True(File.Exists(launcherPath), "ClickraLauncher Program.cs must exist.");

        string launcherSource = File.ReadAllText(launcherPath);
        int fluentIdx = launcherSource.IndexOf("TryActivateFluent", StringComparison.Ordinal);
        int desktopIdx = launcherSource.IndexOf("Fallback to Clickra.Desktop", StringComparison.Ordinal);
        Assert.True(fluentIdx >= 0 && desktopIdx >= 0 && fluentIdx < desktopIdx,
            "ClickraLauncher must prioritize packaged Fluent activation over Desktop fallback during preview phase.");
        Assert.True(launcherSource.Contains("CLICKRA_DESKTOP", StringComparison.Ordinal) ||
                    launcherSource.Contains("--desktop", StringComparison.Ordinal),
            "ClickraLauncher must support explicit opt-in preview for Clickra.Desktop.");
        Assert.True(launcherSource.Contains("cleanArgs.Length > 0", StringComparison.Ordinal),
            "ClickraLauncher must isolate argument-bearing conversion invocations to Clickra.exe.");
    }

    private static void GuardDesktopMotionAndNavigation()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        // 1. Motion.cs must dynamically evaluate motion preference without caching stale session values
        string motionPath = Path.Combine(root, "src", DesktopProjectDir, "Controls", "Motion.cs");
        Assert.True(File.Exists(motionPath), "Motion.cs must exist.");
        string motionSource = File.ReadAllText(motionPath);
        Assert.True(motionSource.Contains("public static bool Enabled => DetectEnabled();", StringComparison.Ordinal),
            "Motion.Enabled must dynamically evaluate system motion preference.");

        // 2. SettingsPage must subscribe to SettingsSectionRequested and bring destination into view
        string settingsPagePath = Path.Combine(root, "src", DesktopProjectDir, "Views", "Pages", "SettingsPage.axaml.cs");
        Assert.True(File.Exists(settingsPagePath), "SettingsPage.axaml.cs must exist.");
        string settingsSource = File.ReadAllText(settingsPagePath);
        Assert.True(settingsSource.Contains("SettingsSectionRequested", StringComparison.Ordinal),
            "SettingsPage must subscribe to SettingsSectionRequested.");
        Assert.True(settingsSource.Contains("ContextMenuGroup.BringIntoView()", StringComparison.Ordinal),
            "SettingsPage must scroll destination section into view on request.");

        // 3. THIRD-PARTY-NOTICES.txt must contain SIL Open Font License 1.1 full text
        string noticesPath = Path.Combine(root, "THIRD-PARTY-NOTICES.txt");
        Assert.True(File.Exists(noticesPath), "THIRD-PARTY-NOTICES.txt must exist.");
        string noticesText = File.ReadAllText(noticesPath);
        Assert.True(noticesText.Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal),
            "THIRD-PARTY-NOTICES.txt must include full SIL OFL 1.1 text.");
    }
}
