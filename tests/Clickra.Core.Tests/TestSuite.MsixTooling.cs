using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private static readonly TimeSpan MsixToolingRegexTimeout = TimeSpan.FromSeconds(1);
    private const string PackagingDirName = "packaging";
    private const string AppxManifestFileName = "AppxManifest.xml";

    public static void RegisterMsixToolingTests(TestRunner runner)
    {
        runner.RunGuard("MSIX reinstall: helper targets the exact main package identity", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

            string manifestPath = Path.Combine(root, PackagingDirName, "msix", AppxManifestFileName);
            var manifest = XDocument.Load(manifestPath);
            string? manifestIdentity = manifest.Root?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Identity")?
                .Attribute("Name")?
                .Value;
            Assert.False(string.IsNullOrWhiteSpace(manifestIdentity),
                "AppxManifest.xml must declare a package identity name.");

            string helperPath = Path.Combine(root, "scripts", "reinstall_msix.ps1");
            string helper = File.ReadAllText(helperPath);
            Match packageName = Regex.Match(
                helper,
                @"\$packageName\s*=\s*""(?<name>[^""]+)""",
                RegexOptions.None,
                MsixToolingRegexTimeout);
            Assert.True(packageName.Success,
                "reinstall_msix.ps1 must declare the package identity used by Get-AppxPackage.");
            Assert.True(string.Equals(packageName.Groups["name"].Value, manifestIdentity, StringComparison.Ordinal),
                "reinstall_msix.ps1 must target the exact main MSIX identity from AppxManifest.xml, not a display name or wildcard.");
        });

        runner.RunGuard("ClickraLauncher: activation arguments with whitespace are quoted", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

            string launcherPath = Path.Combine(root, "src", "ClickraLauncher", "Program.cs");
            string source = File.ReadAllText(launcherPath);

            Assert.True(source.Contains("QuoteArgument", StringComparison.Ordinal),
                "ClickraLauncher must quote activation arguments to protect paths and tokens containing spaces.");
            Assert.True(source.Contains("backslashes * 2", StringComparison.Ordinal),
                "ClickraLauncher must escape trailing backslashes before closing quotes.");
            Assert.True(source.Contains("psi.ArgumentList.Add(arg)", StringComparison.Ordinal),
                "ClickraLauncher must pass arguments via ArgumentList for native process launches.");
        });

        runner.RunGuard("MSIX manifest does not hijack default file associations", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

            string manifestPath = Path.Combine(root, PackagingDirName, "msix", AppxManifestFileName);
            string manifestText = File.ReadAllText(manifestPath);

            Assert.False(manifestText.Contains("windows.fileTypeAssociation", StringComparison.OrdinalIgnoreCase),
                "AppxManifest.xml must not declare windows.fileTypeAssociation to prevent hijacking default file open handlers.");
        });

        runner.RunGuard("MSIX manifests match Directory.Build.props version", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

            string propsPath = Path.Combine(root, "src", "Directory.Build.props");
            string propsText = File.ReadAllText(propsPath);
            Match versionMatch = Regex.Match(
                propsText,
                @"<Version>(?<version>[^<]+)</Version>",
                RegexOptions.None,
                MsixToolingRegexTimeout);
            Assert.True(versionMatch.Success, "src/Directory.Build.props must declare <Version>.");
            string expectedVersion = versionMatch.Groups["version"].Value.Trim();

            string[] manifestRelativePaths =
            [
                Path.Combine(PackagingDirName, "msix", AppxManifestFileName),
                Path.Combine(PackagingDirName, "msix", "AppxManifest.Fluent.xml"),
                Path.Combine("src", "resources", AppxManifestFileName),
            ];

            foreach (string relativePath in manifestRelativePaths)
            {
                string path = Path.Combine(root, relativePath);
                Assert.True(File.Exists(path), $"Manifest file must exist: {relativePath}");
                string manifestText = File.ReadAllText(path);
                Match match = Regex.Match(
                    manifestText,
                    @"Version=""(?<version>[^""]+)""",
                    RegexOptions.None,
                    MsixToolingRegexTimeout);
                Assert.True(match.Success, $"{relativePath} must declare Version attribute.");
                Assert.Equal(expectedVersion, match.Groups["version"].Value.Trim());
            }
        });

        runner.RunGuard("MSIX packaging layout requires app icons and shell menu icons", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

            string buildCommonPath = Path.Combine(root, "scripts", "build_common.ps1");
            Assert.True(File.Exists(buildCommonPath), "scripts/build_common.ps1 must exist.");
            string buildCommon = File.ReadAllText(buildCommonPath);

            Assert.True(buildCommon.Contains("app.ico", StringComparison.Ordinal)
                        && buildCommon.Contains("app.png", StringComparison.Ordinal)
                        && buildCommon.Contains("menu-*.ico", StringComparison.Ordinal),
                "build_common.ps1 must copy and verify app.ico, app.png, and menu-*.ico in packaging layout.");

            Assert.True(buildCommon.Contains("Test-LayoutComplete", StringComparison.Ordinal)
                        && buildCommon.Contains("\"app.ico\"", StringComparison.Ordinal)
                        && buildCommon.Contains("\"app.png\"", StringComparison.Ordinal),
                "Test-LayoutComplete in build_common.ps1 must require app.ico and app.png in the layout.");

            string releaseWorkflowPath = Path.Combine(root, ".github", "workflows", "release.yml");
            Assert.True(File.Exists(releaseWorkflowPath), ".github/workflows/release.yml must exist.");
            string releaseWorkflow = File.ReadAllText(releaseWorkflowPath);

            Assert.True(releaseWorkflow.Contains("IconGen.csproj", StringComparison.Ordinal),
                "release.yml assemble-msix job must execute IconGen.csproj to generate icon assets before packaging.");
        });
    }
}
