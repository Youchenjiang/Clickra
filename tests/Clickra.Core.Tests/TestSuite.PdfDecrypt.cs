using Clickra.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

static partial class TestSuite
{
    public static void RegisterPdfDecryptTests(TestRunner runner)
    {
        runner.Run("PDF decrypt removes protection with the correct password", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-{Guid.NewGuid():N}.pdf");
            const string password = "clickra-test-password";

            try
            {
                using (var document = new PdfDocument())
                {
                    document.AddPage();
                    document.SecuritySettings.UserPassword = password;
                    document.SecuritySettings.OwnerPassword = "clickra-test-owner";
                    document.Save(input);
                }

                FileProcessor.DecryptPdf(input, output, password);

                Assert.True(File.Exists(output), "Expected decrypted PDF to be written.");
                using var decrypted = PdfReader.Open(output, PdfDocumentOpenMode.Import);
                Assert.True(decrypted.PageCount == 1, "Expected decrypted PDF to preserve its page.");
                Assert.True(decrypted.SecurityHandler == null || decrypted.SecurityHandler.Elements.Count == 0,
                    "Expected decrypted PDF to have no security handler.");
            }
            finally
            {
                TryDeleteDecryptFixture(input);
                TryDeleteDecryptFixture(output);
            }
        });
    }

    private static void TryDeleteDecryptFixture(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
