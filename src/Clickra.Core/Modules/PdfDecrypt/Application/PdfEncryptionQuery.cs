using Clickra.Core.Modules.PdfDecrypt.Processing;

namespace Clickra.Core.Modules.PdfDecrypt.Application;

/// <summary>
/// Application query boundary for detecting whether a PDF document is encrypted or password-protected
/// without exposing processors directly to callers.
/// </summary>
public static class PdfEncryptionQuery
{
    public static bool IsEncrypted(string filePath) => PdfDecryptProcessor.IsEncrypted(filePath);
}
