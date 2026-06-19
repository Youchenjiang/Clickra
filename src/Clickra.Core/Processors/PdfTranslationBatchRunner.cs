using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Clickra.Core.Processors
{
    internal static class PdfTranslationBatchRunner
    {
        public static List<string> TranslatePageBatches(
            ITranslationEngine translator,
            List<string> textsToTranslate,
            string targetLang,
            int pageIndex,
            int totalPages,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            var results = new List<string>(textsToTranslate.Count);
            var chunks = BuildTranslationChunks(textsToTranslate, maxItems: 24, maxChars: 6000).ToList();
            int translatedCount = 0;

            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = chunks[chunkIndex];
                onProgress?.Invoke(
                    GetTranslationProgress(pageIndex, totalPages, chunkIndex, chunks.Count),
                    100,
                    $"正在翻譯第 {pageIndex + 1}/{totalPages} 頁，批次 {chunkIndex + 1}/{chunks.Count}（段落 {translatedCount + 1}-{translatedCount + chunk.Count}/{textsToTranslate.Count}）...");

                var chunkResults = translator.TranslateBatchAsync(chunk, targetLang, cancellationToken).GetAwaiter().GetResult();
                if (chunkResults.Count != chunk.Count)
                {
                    throw new Exception("Mismatched batch translation results count.");
                }

                results.AddRange(chunkResults);
                translatedCount += chunk.Count;
            }

            return results;
        }

        public static int GetTranslationProgress(int pageIndex, int totalPages, int unitIndex, int unitCount)
        {
            totalPages = Math.Max(1, totalPages);
            unitCount = Math.Max(1, unitCount);
            double pageFraction = Math.Clamp(unitIndex / (double)unitCount, 0.0, 1.0);
            return 30 + (int)(((pageIndex + pageFraction) * 40.0) / totalPages);
        }

        private static IEnumerable<List<string>> BuildTranslationChunks(List<string> texts, int maxItems, int maxChars)
        {
            var chunk = new List<string>();
            int charCount = 0;

            foreach (var text in texts)
            {
                string safeText = text ?? string.Empty;
                bool wouldExceedItems = chunk.Count >= maxItems;
                bool wouldExceedChars = chunk.Count > 0 && charCount + safeText.Length > maxChars;

                if (wouldExceedItems || wouldExceedChars)
                {
                    yield return chunk;
                    chunk = new List<string>();
                    charCount = 0;
                }

                chunk.Add(safeText);
                charCount += safeText.Length;
            }

            if (chunk.Count > 0)
            {
                yield return chunk;
            }
        }
    }
}
