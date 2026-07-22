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

            onProgress?.Invoke(30 + (int)(p * 40.0 / totalPages), 100, $"正在翻譯第 {p + 1}/{totalPages} 頁...");

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
                        throw new Exception("Mismatched batch translation results count.");
                    }
                }
                catch (Exception ex)
                {
                    LogTranslationError(inputPath, p, $"Translation recovery exhausted. Error: {ex.Message}", logLock);
                    report.Failures.Add($"page {p + 1}: {ex.Message}");
                    throw new InvalidOperationException(
                        $"PDF translation failed on page {p + 1}; automatic batch splitting and provider fallback were exhausted.",
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
