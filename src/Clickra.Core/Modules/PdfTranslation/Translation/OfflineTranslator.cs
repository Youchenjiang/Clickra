using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

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
        throw new InvalidOperationException(Localization.T("pdf_error_translation_offline"));
    }

    public Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(Localization.T("pdf_error_translation_offline"));
    }
}
