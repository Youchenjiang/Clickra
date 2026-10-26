using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

/// <summary>
/// Thrown when translation cannot proceed because offline translation mode is active.
/// Inherits from InvalidOperationException for backwards compatibility.
/// </summary>
public sealed class OfflineTranslationException : InvalidOperationException
{
    public OfflineTranslationException(string message) : base(message) { }
    public OfflineTranslationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Offline translation provider that blocks outbound network requests and surfaces
/// a localized diagnostic message when translation is attempted in offline mode.
/// </summary>
public sealed class OfflineTranslator : ITranslationEngine
{
    public const string ProviderName = "offline";

    public string Name => ProviderName;

    public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new OfflineTranslationException(Localization.T("pdf_error_translation_offline"));
    }

    public Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new OfflineTranslationException(Localization.T("pdf_error_translation_offline"));
    }
}
