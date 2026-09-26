using System.Text;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterLibreOfficeEngineTests(TestRunner runner)
    {
        runner.Run("LibreOffice engine package uses official Windows MSI metadata", () =>
        {
            var manifest = LibreOfficeEngineInstaller.BuiltInManifest;
            var package = manifest.LibreOffice;
            Assert.True(manifest.Schema == 1, "Expected manifest schema 1.");
            Assert.Equal("26.2.6", package.Version);
            Assert.Equal("Windows x86-64 MSI", package.Edition);
            Assert.Equal("MPL-2.0", package.License);
            Assert.Equal(373_252_096, package.DownloadBytes); // skipcq: CS-R1005
            Assert.Equal("f9877032fd908beb9c0ddf06df4af5c2e85f419c42e14876c4cce5aae5fb2660", package.Sha256);
        });

        runner.Run("LibreOffice engine installer rejects non-ASCII paths", () =>
        {
            Assert.True(LibreOfficeEngineInstaller.IsAsciiPath(@"C:\ProgramData\Clickra\Engines\LibreOffice"), "Expected ASCII path to be accepted.");
            Assert.True(LibreOfficeEngineInstaller.IsAsciiPath(string.Empty), "Expected empty paths to be ASCII.");
            Assert.True(LibreOfficeEngineInstaller.IsAsciiPath(@"C:\Program Files (x86)\LibreOffice"), "Expected ASCII punctuation to be accepted.");
            Assert.True(!LibreOfficeEngineInstaller.IsAsciiPath(@"C:\使用者\Clickra\Engines\LibreOffice"), "Expected non-ASCII path to be rejected.");
        });

        runner.Run("LibreOffice engine installer uses the system LibreOffice location", () =>
        {
            string installRoot = LibreOfficeEngineInstaller.GetDefaultInstallRoot();
            Assert.True(Path.IsPathRooted(installRoot), "Expected a rooted install path.");
            Assert.True(installRoot.EndsWith("LibreOffice", StringComparison.OrdinalIgnoreCase), "Expected the LibreOffice install directory suffix.");
        });

        runner.Run("LibreOffice engine installer resolves extracted portable root", () =>
        {
            string downloadDir = @"C:\Users\Test User\AppData\Local\Clickra\downloads";
            Assert.Equal(Path.Combine(downloadDir, "LibreOfficePortable"), LibreOfficeEngineInstaller.GetExtractedPortableRoot(downloadDir));
        });

        runner.Run("LibreOffice engine installer verifies SHA256 hashes", () =>
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"clickra-hash-{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(tempFile, "clickra", Encoding.UTF8);
                string hash = LibreOfficeEngineInstaller.ComputeSha256(tempFile);
                Assert.True(LibreOfficeEngineInstaller.VerifySha256(tempFile, hash), "Expected matching hash to verify.");
                Assert.True(!LibreOfficeEngineInstaller.VerifySha256(tempFile, new string('0', 64)), "Expected wrong hash to fail.");
                Assert.True(!LibreOfficeEngineInstaller.VerifySha256(tempFile, string.Empty), "Expected empty hash to fail.");
                Assert.True(!LibreOfficeEngineInstaller.VerifySha256(tempFile, "   "), "Expected whitespace hash to fail.");
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        });

        runner.Run("LibreOffice engine installer resolves PortableApps soffice path", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-lo-{Guid.NewGuid():N}");
            string soffice = Path.Combine(root, "LibreOfficePortable", "App", "libreoffice", "program", "soffice.exe");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(soffice)!);
                File.WriteAllText(soffice, "");
                Assert.Equal(soffice, LibreOfficeEngineInstaller.ResolvePortableSofficePath(root));
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        });

        runner.Run("LibreOffice helper validates complete executable layout", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-lo-layout-{Guid.NewGuid():N}");
            string programDir = Path.Combine(root, "program");
            string soffice = Path.Combine(programDir, "soffice.exe");
            try
            {
                Directory.CreateDirectory(programDir);
                File.WriteAllText(soffice, "launcher");
                File.WriteAllText(Path.Combine(programDir, "soffice.bin"), "bin");
                File.WriteAllText(Path.Combine(programDir, "mergedlo.dll"), "dll");
                File.WriteAllText(Path.Combine(programDir, "sal3.dll"), "dll");
                File.WriteAllText(Path.Combine(programDir, "cppu3.dll"), "dll");
                File.WriteAllText(Path.Combine(programDir, "cppuhelper3MSC.dll"), "dll");

                Assert.True(LibreOfficeHelper.LooksLikeLibreOfficeExecutable(soffice), "Expected complete LibreOffice layout to validate.");
                File.Delete(Path.Combine(programDir, "sal3.dll"));
                Assert.True(!LibreOfficeHelper.LooksLikeLibreOfficeExecutable(soffice), "Expected incomplete LibreOffice layout to fail validation.");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        });

        runner.Run("LibreOffice helper validates console launcher layout", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-lo-com-layout-{Guid.NewGuid():N}");
            string programDir = Path.Combine(root, "program");
            string soffice = Path.Combine(programDir, "soffice.com");
            try
            {
                CreateLibreOfficeProgramLayout(programDir, "soffice.com");

                Assert.True(LibreOfficeHelper.LooksLikeLibreOfficeExecutable(soffice), "Expected complete soffice.com layout to validate.");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        });

        runner.Run("LibreOffice helper resolves configured executable paths", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-lo-configured-exe-{Guid.NewGuid():N}");
            string programDir = Path.Combine(root, "program");
            string soffice = Path.Combine(programDir, "soffice.exe");
            try
            {
                CreateLibreOfficeProgramLayout(programDir);

                Assert.True(LibreOfficeHelper.TryResolveExecutable(soffice, out string resolved), "Expected configured executable to resolve.");
                Assert.Equal(soffice, resolved);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        });

        runner.Run("LibreOffice helper resolves configured install directories", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-lo-configured-dir-{Guid.NewGuid():N}");
            string programDir = Path.Combine(root, "program");
            string soffice = Path.Combine(programDir, "soffice.exe");
            try
            {
                CreateLibreOfficeProgramLayout(programDir);

                Assert.True(LibreOfficeHelper.TryResolveExecutable(root, out string resolved), "Expected configured install root to resolve.");
                Assert.Equal(soffice, resolved);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        });

        runner.Run("LibreOffice helper falls back to environment path", () =>
        {
            string? oldEnv = Environment.GetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH");
            string root = Path.Combine(Path.GetTempPath(), $"clickra-lo-env-dir-{Guid.NewGuid():N}");
            string programDir = Path.Combine(root, "program");
            string soffice = Path.Combine(programDir, "soffice.exe");
            try
            {
                CreateLibreOfficeProgramLayout(programDir);
                Environment.SetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH", root);

                Assert.True(LibreOfficeHelper.TryResolveExecutable(null, out string resolved), "Expected environment install root to resolve.");
                Assert.Equal(soffice, resolved);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH", oldEnv);
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        });

        runner.Run("LibreOffice helper hides executable while removal is pending restart", () =>
        {
            string oldPath = ClickraStorage.GetSetting("LibreOfficePath");
            string oldPending = ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart");
            string? oldEnv = Environment.GetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH");
            string root = Path.Combine(Path.GetTempPath(), $"clickra-lo-pending-{Guid.NewGuid():N}");
            string programDir = Path.Combine(root, "program");
            string soffice = Path.Combine(programDir, "soffice.exe");
            try
            {
                CreateLibreOfficeProgramLayout(programDir);
                Environment.SetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH", "");
                ClickraStorage.SaveSetting("LibreOfficePath", soffice);
                ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", "true");

                Assert.Equal("", LibreOfficeHelper.GetResolvedExecutablePath());

                ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", "false");
                Assert.Equal(soffice, LibreOfficeHelper.GetResolvedExecutablePath());
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH", oldEnv);
                ClickraStorage.SaveSetting("LibreOfficePath", oldPath);
                ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", oldPending);
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        });

        runner.Run("LibreOffice helper supports only Office document engines", () =>
        {
            Assert.True(LibreOfficeHelper.CanConvert("Word"), "Expected Word to be supported.");
            Assert.True(LibreOfficeHelper.CanConvert("Excel"), "Expected Excel to be supported.");
            Assert.True(LibreOfficeHelper.CanConvert("PowerPoint"), "Expected PowerPoint to be supported.");
            Assert.True(!LibreOfficeHelper.CanConvert("Pdf"), "Expected PDF to be unsupported.");

            Assert.Throws<NotSupportedException>(() =>
                LibreOfficeHelper.ExportToPdf(
                    "Pdf",
                    "input.pdf",
                    "output.pdf",
                    0,
                    1,
                    null,
                CancellationToken.None));
        });

        runner.Run("LibreOffice uninstall refuses an installation Clickra did not make", () =>
        {
            string oldValue = ClickraStorage.GetSetting(ClickraSettings.LibreOfficeInstalledByClickra);
            try
            {
                LibreOfficeEngineInstaller.MarkInstalledByClickra(false);
                Assert.False(LibreOfficeEngineInstaller.WasInstalledByClickra(),
                    "An unmarked installation must not be treated as Clickra's.");

                // The guard has to fire before any installer work, which also keeps this safe on a
                // developer machine that really has a LibreOffice installed.
                InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
                    LibreOfficeEngineInstaller.UninstallSystemLibreOfficeAsync(CancellationToken.None).GetAwaiter().GetResult());
                Assert.True(refusal.Message.Contains("not installed by Clickra", StringComparison.OrdinalIgnoreCase),
                    $"The refusal must say who owns the installation, got: {refusal.Message}");

                LibreOfficeEngineInstaller.MarkInstalledByClickra(true);
                Assert.True(LibreOfficeEngineInstaller.WasInstalledByClickra(),
                    "A LibreOffice Clickra installed must be recorded as Clickra's.");

                LibreOfficeEngineInstaller.MarkInstalledByClickra(false);
                Assert.False(LibreOfficeEngineInstaller.WasInstalledByClickra(),
                    "Removing it again must clear the provenance flag.");
            }
            finally
            {
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeInstalledByClickra, oldValue);
            }
        });

        runner.Run("LibreOffice ownership guard protects both UIs while CLI exposes ownership state", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string fluent = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
            string cliEvents = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.Events.Click.cs"));
            string cliPaint = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.Paint.Settings.cs"));
            string settings = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "ClickraSettings.cs"));

            // Both UIs ultimately route removal through the Core method above, whose first action is the
            // ownership check. Fluent's broader settings-registry migration is intentionally a later PR.
            Assert.True(fluent.Contains("LibreOfficeEngineInstaller.UninstallSystemLibreOfficeAsync", StringComparison.Ordinal),
                "Fluent must route removal through the ownership-guarded Core uninstaller.");

            Assert.True(cliEvents.Contains("LibreOfficeEngineInstaller.WasInstalledByClickra()", StringComparison.Ordinal),
                "The legacy CLI must refuse a user-managed LibreOffice before presenting its uninstall path.");
            Assert.True(cliEvents.Contains("LibreOfficeEngineInstaller.MarkInstalledByClickra", StringComparison.Ordinal),
                "The legacy CLI must record provenance through the shared accessor.");
            Assert.False(cliEvents.Contains("SaveSetting(\"LibreOfficeInstalledByClickra\"", StringComparison.Ordinal),
                "The legacy CLI must not write the provenance key directly.");

            Assert.True(cliPaint.Contains("LibreOfficeEngineInstaller.WasInstalledByClickra()", StringComparison.Ordinal) &&
                        cliPaint.Contains("setting_libreoffice_external_hint", StringComparison.Ordinal),
                "The CLI settings page must hide the uninstall button and explain why.");

            Assert.True(settings.Contains("public const string LibreOfficeInstalledByClickra", StringComparison.Ordinal),
                "The shared ownership key must be registered in ClickraSettings.");

            foreach (string key in new[] { "setting_libreoffice_external_note", "setting_libreoffice_external_hint" })
            foreach (string lang in new[] { "zh-TW", "zh-CN", "en-US", "ja-JP", "ko-KR" })
            {
                Assert.True(Localization.T(key, lang) != key, $"{key} must be translated in {lang}.");
            }
        });
    }

    private static void CreateLibreOfficeProgramLayout(string programDir, string launcherName = "soffice.exe")
    {
        Directory.CreateDirectory(programDir);
        File.WriteAllText(Path.Combine(programDir, launcherName), "launcher");
        File.WriteAllText(Path.Combine(programDir, "soffice.bin"), "bin");
        File.WriteAllText(Path.Combine(programDir, "mergedlo.dll"), "dll");
        File.WriteAllText(Path.Combine(programDir, "sal3.dll"), "dll");
        File.WriteAllText(Path.Combine(programDir, "cppu3.dll"), "dll");
        File.WriteAllText(Path.Combine(programDir, "cppuhelper3MSC.dll"), "dll");
    }
}
