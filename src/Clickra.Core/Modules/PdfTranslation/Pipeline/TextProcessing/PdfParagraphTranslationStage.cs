using Clickra.Core.Modules.PdfTranslation.Model;
using Clickra.Core.Modules.PdfTranslation.Translation;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline.TextProcessing;

internal readonly record struct PdfParagraphTranslationRequest(
    ITranslationEngine Translator,
    IReadOnlyList<List<PdfParagraph>> PageParagraphs,
    string InputPath,
    string TargetLang,
    string Language,
    string DataDirectory,
    Action<int, int, string>? OnProgress,
    CancellationToken CancellationToken);

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
        TranslatePages(new PdfParagraphTranslationRequest(
            TranslationEngineFactory.Create(),
            pageParagraphs,
            inputPath,
            targetLang,
            language,
            dataDirectory,
            onProgress,
            cancellationToken));

    public static PdfTranslationStageReport TranslatePages(PdfParagraphTranslationRequest request)
    {
        var report = new PdfTranslationStageReport { Provider = request.Translator.Name };
        object logLock = new object();
        int totalPages = request.PageParagraphs.Count;

        for (int p = 0; p < totalPages; p++)
        {
            TranslatePage(request, p, totalPages, report, logLock);
        }

        return report;
    }

    private static void TranslatePage(
        PdfParagraphTranslationRequest request,
        int pageIndex,
        int totalPages,
        PdfTranslationStageReport report,
        object logLock)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var paragraphs = request.PageParagraphs[pageIndex];
        if (paragraphs.Count == 0) return;

        request.OnProgress?.Invoke(
            30 + (int)(pageIndex * 40.0 / totalPages),
            100,
            string.Format(Localization.T("pdf_progress_translating_page", request.Language), pageIndex + 1, totalPages));

        var (paragraphsToTranslate, textsToTranslate) = PartitionParagraphs(paragraphs, report);
        if (textsToTranslate.Count == 0) return;

        try
        {
            var results = PdfTranslationBatchRunner.TranslatePageBatches(
                request.Translator,
                textsToTranslate,
                request.TargetLang,
                new PdfTranslationBatchContext(pageIndex, totalPages, request.OnProgress, request.Language),
                request.CancellationToken);

            if (results.Count != paragraphsToTranslate.Count)
            {
                throw new InvalidOperationException(Localization.T("pdf_error_mismatched_batch", request.Language));
            }

            ApplyParagraphResults(request, pageIndex, paragraphsToTranslate, textsToTranslate, results, report);
        }
        catch (OfflineTranslationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogTranslationError(request.InputPath, pageIndex, $"Translation recovery exhausted. Error: {ex.Message}", request.DataDirectory, logLock);
            report.Failures.Add($"page {pageIndex + 1}: {ex.Message}");
            if (TranslationProviderLimits.IsLimit(ex))
                throw PdfTranslationBatchRunner.ProviderLimitedError(pageIndex, request.Language, ex);
            throw new InvalidOperationException(
                string.Format(Localization.T("pdf_error_translation_failed_page", request.Language), pageIndex + 1),
                ex);
        }
    }

    private static (List<PdfParagraph> ParagraphsToTranslate, List<string> TextsToTranslate) PartitionParagraphs(
        List<PdfParagraph> paragraphs,
        PdfTranslationStageReport report)
    {
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

        return (paragraphsToTranslate, textsToTranslate);
    }

    private static void ApplyParagraphResults(
        PdfParagraphTranslationRequest request,
        int pageIndex,
        List<PdfParagraph> paragraphs,
        List<string> sourceTexts,
        List<string> results,
        PdfTranslationStageReport report)
    {
        for (int i = 0; i < results.Count; i++)
        {
            string rawResult = results[i];
            if (string.IsNullOrWhiteSpace(rawResult))
            {
                throw new InvalidOperationException(
                    $"Translation provider returned an empty result for paragraph {i + 1} on page {pageIndex + 1}.");
            }

            PdfParagraph paragraph = paragraphs[i];
            string translationSource = sourceTexts[i];
            ValidateQuality(request.Translator.Name, translationSource, rawResult, request.TargetLang, pageIndex, i);

            string restoredMarkers = PdfParagraphMarkerNormalizer.RestoreTranslatedMarkers(
                paragraph.TextWithPlaceholders,
                rawResult);
            paragraph.TranslatedText = PostProcessor.Process(
                paragraph.TextWithPlaceholders,
                restoredMarkers,
                request.TargetLang);

            if (IsPreservedTechnicalLabel(paragraph, translationSource, rawResult))
            {
                report.PreservedTechnicalLabels.Add(new PdfTranslationPreservation
                {
                    Page = pageIndex + 1,
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

    private static void ValidateQuality(string provider, string source, string result, string targetLang, int pageIndex, int paraIndex)
    {
        if (provider.Equals("synthetic-cjk", StringComparison.OrdinalIgnoreCase)) return;

        string? problem = TranslationResultQualityGuard.FindProblem(source, result, targetLang);
        if (problem != null)
        {
            throw new InvalidOperationException(
                $"Translation provider returned unsafe output for paragraph {paraIndex + 1} on page {pageIndex + 1}: {problem}.");
        }
    }

    private static bool IsPreservedTechnicalLabel(PdfParagraph paragraph, string source, string result) =>
        PdfParagraphSemanticClassifier.IsHeadingParagraph(paragraph) &&
        TranslationSourcePreservationClassifier.IsHighConfidenceTechnicalLabel(paragraph.TextWithPlaceholders) &&
        TranslationSourcePreservationClassifier.IsUnchanged(source, result);

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
