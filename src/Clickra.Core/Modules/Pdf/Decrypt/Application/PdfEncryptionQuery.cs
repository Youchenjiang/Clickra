using System.IO;
using System.Linq;
using Clickra.Core.Application;
using Clickra.Core.Modules.Pdf.Decrypt.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Pdf.Decrypt.Application;

/// <summary>
/// Application query boundary for detecting whether a PDF document is encrypted or password-protected
/// without exposing processors directly to callers.
/// </summary>
public static class PdfEncryptionQuery
{
    public static bool IsEncrypted(string filePath) => PdfDecryptProcessor.IsEncrypted(filePath);

    /// <summary>True when the PDF cannot be opened without a password; false for one that only restricts printing
    /// or copying (it is still encrypted, see <see cref="IsEncrypted"/>).</summary>
    public static bool NeedsOpenPassword(string filePath) => PdfDecryptProcessor.NeedsOpenPassword(filePath);
}
