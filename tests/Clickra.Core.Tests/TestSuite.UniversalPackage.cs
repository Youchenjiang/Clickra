using System;
using System.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string CoreProjectDir = "Clickra.Core";

    public static void RegisterUniversalPackageTests(TestRunner runner)
    {
        runner.RunGuard("Universal package: project contracts enforce multi-targeting and .NET tool specifications", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

            // 1. Verify Core project multi-targeting
            string coreProjPath = Path.Combine(root, "src", CoreProjectDir, "Clickra.Core.csproj");
            string coreProjText = File.ReadAllText(coreProjPath);

            Assert.True(coreProjText.Contains("<TargetFrameworks>net8.0;net8.0-windows10.0.19041.0</TargetFrameworks>", StringComparison.Ordinal),
                "Clickra.Core must multi-target net8.0 and net8.0-windows10.0.19041.0.");
            Assert.True(coreProjText.Contains("<EnableWindowsTargeting>true</EnableWindowsTargeting>", StringComparison.Ordinal),
                "Clickra.Core must enable Windows targeting for cross-platform restore.");
            Assert.True(coreProjText.Contains("<NoWarn>$(NoWarn);CA1416</NoWarn>", StringComparison.Ordinal),
                "Clickra.Core must suppress CA1416 on net8.0 for guarded platform calls.");

            // 2. Verify CLI project multi-targeting and .NET tool properties
            string cliProjPath = Path.Combine(root, "src", CliProjectDirectory, "Clickra.csproj");
            string cliProjText = File.ReadAllText(cliProjPath);

            Assert.True(cliProjText.Contains("<TargetFramework>net8.0</TargetFramework>", StringComparison.Ordinal),
                "Clickra.CLI must target universal net8.0 for tool packaging and standalone NativeAOT compilation.");
            Assert.True(cliProjText.Contains("<PackAsTool>true</PackAsTool>", StringComparison.Ordinal),
                "Clickra.CLI must configure PackAsTool for net8.0 tool creation.");
            Assert.True(cliProjText.Contains("<ToolCommandName>clickra</ToolCommandName>", StringComparison.Ordinal),
                "Clickra.CLI must set ToolCommandName to clickra.");
            Assert.True(cliProjText.Contains("<PackageId>Clickra</PackageId>", StringComparison.Ordinal),
                "Clickra.CLI must set PackageId to Clickra.");
            Assert.True(cliProjText.Contains("<PackageLicenseExpression>MIT</PackageLicenseExpression>", StringComparison.Ordinal),
                "Clickra.CLI must specify MIT license expression for universal distribution.");
            Assert.True(cliProjText.Contains("<PackageReadmeFile>README.md</PackageReadmeFile>", StringComparison.Ordinal),
                "Clickra.CLI must bundle README.md as package documentation.");

            // 3. Verify Directory.Build.targets icongen guard
            string targetsPath = Path.Combine(root, "Directory.Build.targets");
            string targetsText = File.ReadAllText(targetsPath);
            Assert.True(targetsText.Contains("IsOSPlatform('windows')", StringComparison.Ordinal) &&
                        targetsText.Contains("!Exists", StringComparison.Ordinal),
                "Directory.Build.targets must guard icon generation to prevent concurrent build collisions.");
        });

        runner.RunGuard("Universal package: CI and release workflows declare multi-platform NativeAOT and package targets", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

            // 1. Verify CI workflow cross-platform jobs
            string ciPath = Path.Combine(root, ".github", "workflows", "ci.yml");
            string ciText = File.ReadAllText(ciPath);

            Assert.True(ciText.Contains("cross-platform:", StringComparison.Ordinal),
                "ci.yml must define a cross-platform matrix job.");
            Assert.True(ciText.Contains("linux-x64", StringComparison.Ordinal) &&
                        ciText.Contains("osx-arm64", StringComparison.Ordinal),
                "ci.yml must validate NativeAOT builds for linux-x64 and osx-arm64.");
            Assert.True(ciText.Contains("Pack .NET Global Tool", StringComparison.Ordinal),
                "ci.yml must validate .NET tool packaging.");
            Assert.True(ciText.Contains("needs: [build-components, cross-platform]", StringComparison.Ordinal),
                "ci.yml build gate must require both component builds and cross-platform verification.");

            // 2. Verify Release workflow distribution channels
            string releasePath = Path.Combine(root, ".github", "workflows", "release.yml");
            string releaseText = File.ReadAllText(releasePath);

            Assert.True(releaseText.Contains("cross-cli-publish:", StringComparison.Ordinal),
                "release.yml must publish cross-platform NativeAOT CLI binaries.");
            Assert.True(releaseText.Contains("pack-tool:", StringComparison.Ordinal),
                "release.yml must pack .NET global tool package.");
            Assert.True(releaseText.Contains("clickra-cli-linux-x64.tar.gz", StringComparison.Ordinal) &&
                        releaseText.Contains("clickra-cli-osx-arm64.tar.gz", StringComparison.Ordinal) &&
                        releaseText.Contains("clickra-cli-win-x64.zip", StringComparison.Ordinal),
                "release.yml must attach multi-platform NativeAOT archives to GitHub Release Assets.");
            Assert.True(releaseText.Contains("nuget.pkg.github.com", StringComparison.Ordinal),
                "release.yml must publish NuGet package to GitHub Packages.");
        });

        runner.RunGuard("Universal package: platform preprocessor guards isolate Windows-specific rendering and WIC APIs", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

            string wicPath = Path.Combine(root, "src", CoreProjectDir, "Modules", "Img", "Shared", "Infrastructure", "WicImageHelper.cs");
            string wicText = File.ReadAllText(wicPath);
            Assert.True(wicText.Contains("#if WINDOWS", StringComparison.Ordinal) &&
                        wicText.Contains("Windows.Graphics.Imaging", StringComparison.Ordinal),
                "WicImageHelper must enclose WinRT WIC namespaces and codecs in #if WINDOWS.");

            string pdfThumbPath = Path.Combine(root, "src", CoreProjectDir, "Rendering", "PdfPageThumbnailRenderer.cs");
            string pdfThumbText = File.ReadAllText(pdfThumbPath);
            Assert.True(pdfThumbText.Contains("#if WINDOWS", StringComparison.Ordinal) &&
                        pdfThumbText.Contains("Windows.Data.Pdf.PdfDocument", StringComparison.Ordinal),
                "PdfPageThumbnailRenderer must enclose Windows.Data.Pdf in #if WINDOWS.");

            string pdfToImgPath = Path.Combine(root, "src", CoreProjectDir, "Modules", "Pdf", "ToImage", "Processing", "PdfToImageProcessor.cs");
            string pdfToImgText = File.ReadAllText(pdfToImgPath);
            Assert.True(pdfToImgText.Contains("#if WINDOWS", StringComparison.Ordinal) &&
                        pdfToImgText.Contains("TryRenderWithWindowsPdf", StringComparison.Ordinal),
                "PdfToImageProcessor must enclose TryRenderWithWindowsPdf in #if WINDOWS.");
        });
    }
}
