using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private static readonly TimeSpan MsixToolingRegexTimeout = TimeSpan.FromSeconds(1);

    public static void RegisterMsixToolingTests(TestRunner runner)
    {
        runner.RunGuard("MSIX reinstall: helper targets the exact main package identity", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

            string manifestPath = Path.Combine(root, "packaging", "msix", "AppxManifest.xml");
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
    }
}
