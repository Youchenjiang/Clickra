using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Modules.PdfTranslation.Translation;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline.TextProcessing;

    internal readonly record struct PdfTranslationBatchContext(
        int PageIndex,
        int TotalPages,
        Action<int, int, string>? OnProgress,
        string Language);

    internal static class PdfTranslationBatchRunner
    {
        public static List<string> TranslatePageBatches(
            ITranslationEngine translator,
            List<string> textsToTranslate,
            string targetLang,
            PdfTranslationBatchContext context,
            CancellationToken cancellationToken)
        {
            var results = new List<string>(textsToTranslate.Count);
            var chunks = BuildTranslationChunks(textsToTranslate, maxItems: 24, maxChars: 6000).ToList();
            int translatedCount = 0;

            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = chunks[chunkIndex];
                int currentProgress = GetTranslationProgress(context.PageIndex, context.TotalPages, chunkIndex, chunks.Count);
                context.OnProgress?.Invoke(
                    currentProgress,
                    100,
                    string.Format(
                        Localization.T("pdf_progress_translating_batch", context.Language),
                        context.PageIndex + 1,
                        context.TotalPages,
                        chunkIndex + 1,
                        chunks.Count,
                        translatedCount + 1,
                        translatedCount + chunk.Count,
                        textsToTranslate.Count));

                var chunkResults = TranslateChunkWithRecovery(
                    translator,
                    chunk,
                    targetLang,
                    context.PageIndex,
                    chunkIndex,
                    context.Language,
                    cancellationToken,
                    context.OnProgress,
                    currentProgress);

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
            string language,
            CancellationToken cancellationToken,
            Action<int, int, string>? onProgress = null,
            int currentProgress = 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Exception failure;
            try
            {
                return TranslateChunkOnce(translator, chunk, targetLang, cancellationToken, language);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failure = ex;
            }

            // A rate limit or exhausted quota is not fixed by splitting: smaller
            // batches only multiply the calls. Wait once as the provider asks,
            // retry the same batch, and stop with a clear message if still limited.
            if (TranslationProviderLimits.IsLimit(failure))
            {
                TimeSpan wait = TranslationProviderLimits.SuggestedWait(failure);
                int waitSeconds = (int)Math.Max(1, Math.Ceiling(wait.TotalSeconds));
                Console.Error.WriteLine(
                    $"[Translate] page {pageIndex + 1}, batch {chunkIndex + 1} hit a provider limit; " +
                    $"retrying once in {waitSeconds}s: {failure.Message}");

                for (int remaining = waitSeconds; remaining > 0; remaining--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    onProgress?.Invoke(
                        currentProgress,
                        100,
                        string.Format(Localization.T("pdf_progress_provider_waiting", language), pageIndex + 1, remaining));
                    int sleepMs = remaining == 1 ? (int)(wait.TotalMilliseconds % 1000 == 0 ? 1000 : wait.TotalMilliseconds % 1000) : 1000;
                    if (sleepMs <= 0) sleepMs = 1000;
                    Task.Delay(sleepMs, cancellationToken).GetAwaiter().GetResult();
                }

                try
                {
                    return TranslateChunkOnce(translator, chunk, targetLang, cancellationToken, language);
                }
                catch (Exception retryException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (TranslationProviderLimits.IsLimit(retryException))
                        throw ProviderLimitedError(pageIndex, language, retryException);
                    failure = retryException;
                }
            }

            Console.Error.WriteLine(
                $"[Translate] page {pageIndex + 1}, batch {chunkIndex + 1} failed; " +
                $"splitting for recovery: {failure.Message}");

            if (chunk.Count > 1)
            {
                int midpoint = chunk.Count / 2;
                var left = TranslateChunkWithRecovery(
                    translator,
                    chunk.GetRange(0, midpoint),
                    targetLang,
                    pageIndex,
                    chunkIndex,
                    language,
                    cancellationToken,
                    onProgress,
                    currentProgress);
                var right = TranslateChunkWithRecovery(
                    translator,
                    chunk.GetRange(midpoint, chunk.Count - midpoint),
                    targetLang,
                    pageIndex,
                    chunkIndex,
                    language,
                    cancellationToken,
                    onProgress,
                    currentProgress);
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
                    cancellationToken,
                    language);
                if (string.IsNullOrWhiteSpace(translated))
                {
                    throw new InvalidOperationException(Localization.T("pdf_error_provider_empty", language));
                }
                return new List<string> { translated };
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (TranslationProviderLimits.IsLimit(ex))
                    throw ProviderLimitedError(pageIndex, language, ex);
                throw new InvalidOperationException(
                    string.Format(Localization.T("pdf_error_unable_paragraph", language), pageIndex + 1),
                    ex);
            }
        }

        private static List<string> TranslateChunkOnce(
            ITranslationEngine translator,
            List<string> chunk,
            string targetLang,
            CancellationToken cancellationToken,
            string language)
        {
            var translated = RunWithTimeout(
                token => translator.TranslateBatchAsync(chunk, targetLang, token),
                translator is FallbackTranslator
                    ? TranslationTimeouts.ChainCallTimeout
                    : TranslationTimeouts.ProviderCallTimeout,
                cancellationToken,
                language);
            if (translated.Count != chunk.Count)
            {
                throw new InvalidOperationException(
                    $"{translator.Name} returned {translated.Count}/{chunk.Count} results.");
            }
            return translated;
        }

        /// <summary>The user-facing error when every provider is still rate limited or out of quota.</summary>
        internal static InvalidOperationException ProviderLimitedError(int pageIndex, string language, Exception cause) =>
            new(string.Format(Localization.T("pdf_error_provider_limited", language), pageIndex + 1), cause);

        private static T RunWithTimeout<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string language = "")
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
                throw new TimeoutException(string.Format(Localization.T("pdf_error_provider_timeout", language), timeout.TotalSeconds.ToString("0")));
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
