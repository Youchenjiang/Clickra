using Clickra.Core.Models;

namespace Clickra.Core.Processors;

internal static class PdfParagraphTranslationStage
{
    public static void TranslatePages(
        IReadOnlyList<List<PdfParagraph>> pageParagraphs,
        string inputPath,
        string targetLang,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        var translator = TranslationEngineFactory.Create();
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
                }
                else
                {
                    paragraphsToTranslate.Add(para);
                    textsToTranslate.Add(para.TextWithPlaceholders);
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
                            string rawResult = string.IsNullOrWhiteSpace(results[i])
                                ? paragraphsToTranslate[i].TextWithPlaceholders
                                : results[i];
                            paragraphsToTranslate[i].TranslatedText = PostProcessor.Process(
                                paragraphsToTranslate[i].TextWithPlaceholders,
                                rawResult,
                                targetLang
                            );
                        }
                    }
                    else
                    {
                        throw new Exception("Mismatched batch translation results count.");
                    }
                }
                catch (Exception ex)
                {
                    LogTranslationError(inputPath, p, $"Batch translation failed, falling back to sequential. Error: {ex.Message}", logLock);

                    for (int i = 0; i < paragraphsToTranslate.Count; i++)
                    {
                        var para = paragraphsToTranslate[i];
                        try
                        {
                            onProgress?.Invoke(
                                PdfTranslationBatchRunner.GetTranslationProgress(p, totalPages, i, paragraphsToTranslate.Count),
                                100,
                                $"第 {p + 1}/{totalPages} 頁批次翻譯失敗，正在逐段重試 {i + 1}/{paragraphsToTranslate.Count}...");
                            string result = translator.TranslateAsync(para.TextWithPlaceholders, targetLang, cancellationToken).GetAwaiter().GetResult();
                            string rawResult = string.IsNullOrWhiteSpace(result) ? para.TextWithPlaceholders : result;
                            para.TranslatedText = PostProcessor.Process(
                                para.TextWithPlaceholders,
                                rawResult,
                                targetLang
                            );
                        }
                        catch (Exception exSub)
                        {
                            para.TranslatedText = para.TextWithPlaceholders;
                            LogTranslationError(inputPath, p, $"Sequential fallback error: {exSub.Message}", logLock);
                        }
                    }
                }
            }
        }
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
