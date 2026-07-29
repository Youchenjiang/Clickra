using Clickra.Core.Models;

namespace Clickra.Core.Processors;

internal static class PdfParagraphTranslationStage
{
    public static PdfTranslationStageReport TranslatePages(
        IReadOnlyList<List<PdfParagraph>> pageParagraphs,
        string inputPath,
        string targetLang,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        var translator = TranslationEngineFactory.Create();
        var report = new PdfTranslationStageReport { Provider = translator.Name };
        object logLock = new object();
        int totalPages = pageParagraphs.Count;

        for (int p = 0; p < totalPages; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paragraphs = pageParagraphs[p];
            if (paragraphs.Count == 0) continue;

            string language = ClickraStorage.GetSetting("Language");
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
                        p,
                        totalPages,
                        onProgress,
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
                            string styledSource = paragraphsToTranslate[i].TranslationTextWithStyles;
                            string? qualityProblem = translator.Name.Equals(
                                "synthetic-cjk",
                                StringComparison.OrdinalIgnoreCase)
                                ? null
                                : TranslationResultQualityGuard.FindProblem(
                                    styledSource,
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
                            report.TranslatedParagraphs++;
                        }
                    }
                    else
                    {
                        throw new Exception(Localization.T("pdf_error_mismatched_batch", language));
                    }
                }
                catch (Exception ex)
                {
                    LogTranslationError(inputPath, p, $"Translation recovery exhausted. Error: {ex.Message}", logLock);
                    report.Failures.Add($"page {p + 1}: {ex.Message}");
                    throw new InvalidOperationException(
                        string.Format(Localization.T("pdf_error_translation_failed_page", language), p + 1),
                        ex);
                }
            }
        }

        return report;
    }

    private static void LogTranslationError(string inputPath, int pageIndex, string message, object logLock)
    {
        try
        {
            string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {pageIndex + 1}] {message}{Environment.NewLine}";
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
    public List<string> Failures { get; } = new();
}
