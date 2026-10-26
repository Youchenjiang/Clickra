using Clickra.Core.Modules.PdfTranslation.Model;
using Clickra.Core.Modules.PdfTranslation.Translation;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline.TextProcessing;

internal static class PdfParagraphTranslationStage
{
    public static PdfTranslationStageReport TranslatePages(
        IReadOnlyList<List<PdfParagraph>> pageParagraphs,
        string inputPath,
        string targetLang,
        string language,
        string dataDirectory,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken) =>
        TranslatePages(
            TranslationEngineFactory.Create(),
            pageParagraphs,
            inputPath,
            targetLang,
            language,
            dataDirectory,
            onProgress,
            cancellationToken);

    public static PdfTranslationStageReport TranslatePages(
        ITranslationEngine translator,
        IReadOnlyList<List<PdfParagraph>> pageParagraphs,
        string inputPath,
        string targetLang,
        string language,
        string dataDirectory,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        var report = new PdfTranslationStageReport { Provider = translator.Name };
        object logLock = new object();
        int totalPages = pageParagraphs.Count;

        for (int p = 0; p < totalPages; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paragraphs = pageParagraphs[p];
            if (paragraphs.Count == 0) continue;

            onProgress?.Invoke(
                30 + (int)(p * 40.0 / totalPages),
                100,
                string.Format(Localization.T("pdf_progress_translating_page", language), p + 1, totalPages));

            var paragraphsToTranslate = new List<PdfParagraph>();
            var textsToTranslate = new List<string>();

            foreach (var para in paragraphs)
            {
                if (para.IsBypassed)
                {
                    para.TranslatedText = para.TextWithPlaceholders;
                    report.BypassedParagraphs++;
                }
                else
                {
                    paragraphsToTranslate.Add(para);
                    textsToTranslate.Add(string.IsNullOrWhiteSpace(para.TranslationTextWithStyles)
                        ? para.TextWithPlaceholders
                        : para.TranslationTextWithStyles);
                }
            }

            if (textsToTranslate.Count > 0)
            {
                try
                {
                    var results = PdfTranslationBatchRunner.TranslatePageBatches(
                        translator,
                        textsToTranslate,
                        targetLang,
                        new PdfTranslationBatchContext(p, totalPages, onProgress, language),
                        cancellationToken);
                    if (results.Count == paragraphsToTranslate.Count)
                    {
                        for (int i = 0; i < results.Count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(results[i]))
                            {
                                throw new InvalidOperationException(
                                    $"Translation provider returned an empty result for paragraph {i + 1} on page {p + 1}.");
                            }

                            // A blank provider response is a hard failure. Never
                            // substitute source text here: doing so creates a PDF
                            // that looks complete while silently leaving one
                            // paragraph untranslated.
                            string rawResult = results[i];
                            PdfParagraph paragraph = paragraphsToTranslate[i];
                            string translationSource = textsToTranslate[i];
                            bool preservedTechnicalLabel =
                                PdfParagraphSemanticClassifier.IsHeadingParagraph(paragraph) &&
                                TranslationSourcePreservationClassifier.IsHighConfidenceTechnicalLabel(
                                    paragraph.TextWithPlaceholders) &&
                                TranslationSourcePreservationClassifier.IsUnchanged(
                                    translationSource,
                                    rawResult);
                            string? qualityProblem = translator.Name.Equals(
                                "synthetic-cjk",
                                StringComparison.OrdinalIgnoreCase)
                                ? null
                                : TranslationResultQualityGuard.FindProblem(
                                    translationSource,
                                    rawResult,
                                    targetLang);
                            if (qualityProblem != null)
                                throw new InvalidOperationException(
                                    $"Translation provider returned unsafe output for paragraph {i + 1} on page {p + 1}: {qualityProblem}.");
                            string restoredMarkers = PdfParagraphMarkerNormalizer.RestoreTranslatedMarkers(
                                paragraphsToTranslate[i].TextWithPlaceholders,
                                rawResult);
                            paragraphsToTranslate[i].TranslatedText = PostProcessor.Process(
                                paragraphsToTranslate[i].TextWithPlaceholders,
                                restoredMarkers,
                                targetLang
                            );
                            if (preservedTechnicalLabel)
                            {
                                report.PreservedTechnicalLabels.Add(new PdfTranslationPreservation
                                {
                                    Page = p + 1,
                                    Text = paragraph.TextWithPlaceholders,
                                    Action = "preserved",
                                    Reason = "section heading containing a technical label"
                                });
                            }
                            else
                            {
                                report.TranslatedParagraphs++;
                            }
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(Localization.T("pdf_error_mismatched_batch", language));
                    }
                }
                catch (OfflineTranslationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogTranslationError(inputPath, p, $"Translation recovery exhausted. Error: {ex.Message}", dataDirectory, logLock);
                    report.Failures.Add($"page {p + 1}: {ex.Message}");
                    if (TranslationProviderLimits.IsLimit(ex))
                        throw PdfTranslationBatchRunner.ProviderLimitedError(p, language, ex);
                    throw new InvalidOperationException(
                        string.Format(Localization.T("pdf_error_translation_failed_page", language), p + 1),
                        ex);
                }
            }
        }

        return report;
    }

    private static void LogTranslationError(string inputPath, int pageIndex, string message, string dataDirectory, object logLock)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) return;
            string logPath = Path.Combine(dataDirectory, "translate_errors.log");
            string logLine = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {pageIndex + 1}] {message}{Environment.NewLine}";
            lock (logLock)
            {
                File.AppendAllText(logPath, logLine);
            }
        }
        catch { }
    }
}

internal sealed class PdfTranslationStageReport
{
    public string Provider { get; init; } = string.Empty;
    public int TranslatedParagraphs { get; set; }
    public int BypassedParagraphs { get; set; }
    public List<PdfTranslationPreservation> PreservedTechnicalLabels { get; } = new();
    public List<string> Failures { get; } = new();
}
