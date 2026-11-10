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

    public static ConversionValidationResult ValidateNotEncrypted(ConversionRequest request, string localizationKey)
    {
        if (request.InputFiles.Any(f => System.IO.File.Exists(f) && IsEncrypted(f)))
        {
            string lang = ExecutionLocalizationOptions.ReadLanguage(request.Options);
            return ConversionValidationResult.Failure(Localization.T(localizationKey, lang));
        }

        return ConversionValidationResult.Success();
    }
}
