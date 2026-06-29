using System.Text;
using Clickra.Core.Processors;

static partial class TestSuite
{
    public static void RegisterLibreOfficeEngineTests(TestRunner runner)
    {
        runner.Run("LibreOffice engine package uses official Windows MSI metadata", () =>
        {
            var manifest = LibreOfficeEngineInstaller.BuiltInManifest;
            var package = manifest.LibreOffice;
            Assert.Equal(1, manifest.Schema);
            Assert.Equal("26.2.4", package.Version);
            Assert.Equal("Windows x86-64 MSI", package.Edition);
            Assert.Equal("MPL-2.0", package.License);
            Assert.True(package.DownloadBytes > 300L * 1024L * 1024L, "Expected download size metadata for LibreOffice MSI.");
            Assert.Equal("202f26cda071c5aa4996a5a28412fddceb3891dceb0366982c62650456c0730f", package.Sha256);
        });

        runner.Run("LibreOffice engine installer rejects non-ASCII paths", () =>
        {
            Assert.True(LibreOfficeEngineInstaller.IsAsciiPath(@"C:\ProgramData\Clickra\Engines\LibreOffice"), "Expected ASCII path to be accepted.");
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
    }
}
