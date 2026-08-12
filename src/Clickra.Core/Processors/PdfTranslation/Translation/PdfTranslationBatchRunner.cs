using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Processors
{
    internal static class PdfTranslationBatchRunner
    {
        private const string LanguageSettingKey = "Language";

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
                string language = ClickraStorage.GetSetting(LanguageSettingKey);
                onProgress?.Invoke(
                    GetTranslationProgress(pageIndex, totalPages, chunkIndex, chunks.Count),
                    100,
                    string.Format(
                        Localization.T("pdf_progress_translating_batch", language),
                        pageIndex + 1,
                        totalPages,
                        chunkIndex + 1,
                        chunks.Count,
                        translatedCount + 1,
                        translatedCount + chunk.Count,
                        textsToTranslate.Count));

                var chunkResults = TranslateChunkWithRecovery(
                    translator,
                    chunk,
                    targetLang,
                    pageIndex,
                    chunkIndex,
                    cancellationToken);

                results.AddRange(chunkResults);
                translatedCount += chunk.Count;
            }

            return results;
        }

        private static List<string> TranslateChunkWithRecovery(
            ITranslationEngine translator,
            List<string> chunk,
            string targetLang,
            int pageIndex,
            int chunkIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var translated = RunWithTimeout(
                    token => translator.TranslateBatchAsync(chunk, targetLang, token),
                    translator is FallbackTranslator
                        ? TranslationTimeouts.ChainCallTimeout
                        : TranslationTimeouts.ProviderCallTimeout,
                    cancellationToken);
                if (translated.Count != chunk.Count)
                {
                    throw new InvalidOperationException(
                        $"{translator.Name} returned {translated.Count}/{chunk.Count} results.");
                }
                return translated;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine(
                    $"[Translate] page {pageIndex + 1}, batch {chunkIndex + 1} failed; " +
                    $"splitting for recovery: {ex.Message}");
            }

            if (chunk.Count > 1)
            {
                int midpoint = chunk.Count / 2;
                var left = TranslateChunkWithRecovery(
                    translator,
                    chunk.GetRange(0, midpoint),
                    targetLang,
                    pageIndex,
                    chunkIndex,
                    cancellationToken);
                var right = TranslateChunkWithRecovery(
                    translator,
                    chunk.GetRange(midpoint, chunk.Count - midpoint),
                    targetLang,
                    pageIndex,
                    chunkIndex,
                    cancellationToken);
                left.AddRange(right);
                return left;
            }

            string sourceText = chunk.Count == 0 ? string.Empty : chunk[0];
            try
            {
                string translated = RunWithTimeout(
                    token => translator.TranslateAsync(sourceText, targetLang, token),
                    translator is FallbackTranslator
                        ? TranslationTimeouts.ChainCallTimeout
                        : TranslationTimeouts.ProviderCallTimeout,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(translated))
                {
                    throw new InvalidOperationException(Localization.T("pdf_error_provider_empty", ClickraStorage.GetSetting(LanguageSettingKey)));
                }
                return new List<string> { translated };
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    string.Format(Localization.T("pdf_error_unable_paragraph", ClickraStorage.GetSetting(LanguageSettingKey)), pageIndex + 1),
                    ex);
            }
        }

        private static T RunWithTimeout<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                return operation(timeoutCts.Token).WaitAsync(timeoutCts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(string.Format(Localization.T("pdf_error_provider_timeout", ClickraStorage.GetSetting(LanguageSettingKey)), timeout.TotalSeconds.ToString("0")));
            }
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
