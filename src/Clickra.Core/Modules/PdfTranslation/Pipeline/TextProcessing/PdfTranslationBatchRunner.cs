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

    internal readonly record struct TranslationChunkContext(
        PdfTranslationBatchContext Batch,
        int ChunkIndex,
        int CurrentProgress);

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

                var chunkContext = new TranslationChunkContext(context, chunkIndex, currentProgress);
                var chunkResults = TranslateChunkWithRecovery(
                    translator,
                    chunk,
                    targetLang,
                    chunkContext,
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
            TranslationChunkContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return TranslateChunkOnce(translator, chunk, targetLang, context.Batch.Language, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (TryRecoverFromLimit(translator, chunk, targetLang, context, ex, cancellationToken, out var recovered) && recovered != null)
                    return recovered;

                Console.Error.WriteLine(
                    $"[Translate] page {context.Batch.PageIndex + 1}, batch {context.ChunkIndex + 1} failed; " +
                    $"splitting for recovery: {ex.Message}");
            }

            if (chunk.Count > 1)
            {
                int midpoint = chunk.Count / 2;
                var left = TranslateChunkWithRecovery(
                    translator,
                    chunk.GetRange(0, midpoint),
                    targetLang,
                    context,
                    cancellationToken);
                var right = TranslateChunkWithRecovery(
                    translator,
                    chunk.GetRange(midpoint, chunk.Count - midpoint),
                    targetLang,
                    context,
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
                    cancellationToken,
                    context.Batch.Language);
                if (string.IsNullOrWhiteSpace(translated))
                {
                    throw new InvalidOperationException(Localization.T("pdf_error_provider_empty", context.Batch.Language));
                }
                return new List<string> { translated };
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (TranslationProviderLimits.IsLimit(ex))
                    throw ProviderLimitedError(context.Batch.PageIndex, context.Batch.Language, ex);
                throw new InvalidOperationException(
                    string.Format(Localization.T("pdf_error_unable_paragraph", context.Batch.Language), context.Batch.PageIndex + 1),
                    ex);
            }
        }

        private static bool TryRecoverFromLimit(
            ITranslationEngine translator,
            List<string> chunk,
            string targetLang,
            TranslationChunkContext context,
            Exception failure,
            CancellationToken cancellationToken,
            out List<string>? recovered)
        {
            recovered = null;
            if (!TranslationProviderLimits.IsLimit(failure)) return false;

            TimeSpan wait = TranslationProviderLimits.SuggestedWait(failure);
            int waitSeconds = (int)Math.Max(1, Math.Ceiling(wait.TotalSeconds));
            Console.Error.WriteLine(
                $"[Translate] page {context.Batch.PageIndex + 1}, batch {context.ChunkIndex + 1} hit a provider limit; " +
                $"retrying once in {waitSeconds}s: {failure.Message}");

            WaitOutLimitWithProgress(wait, waitSeconds, context, cancellationToken);

            try
            {
                recovered = TranslateChunkOnce(translator, chunk, targetLang, context.Batch.Language, cancellationToken);
                return true;
            }
            catch (Exception retryException) when (!cancellationToken.IsCancellationRequested)
            {
                if (TranslationProviderLimits.IsLimit(retryException))
                    throw ProviderLimitedError(context.Batch.PageIndex, context.Batch.Language, retryException);
                return false;
            }
        }

        private static void WaitOutLimitWithProgress(
            TimeSpan wait,
            int waitSeconds,
            TranslationChunkContext context,
            CancellationToken cancellationToken)
        {
            for (int remaining = waitSeconds; remaining > 0; remaining--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.Batch.OnProgress?.Invoke(
                    context.CurrentProgress,
                    100,
                    string.Format(Localization.T("pdf_progress_provider_waiting", context.Batch.Language), context.Batch.PageIndex + 1, remaining));
                int sleepMs = CalculateSleepMs(wait, remaining);
                Task.Delay(sleepMs, cancellationToken).GetAwaiter().GetResult();
            }
        }

        private static int CalculateSleepMs(TimeSpan wait, int remainingSeconds)
        {
            if (remainingSeconds > 1) return 1000;
            int remainder = (int)(wait.TotalMilliseconds % 1000);
            return remainder > 0 ? remainder : 1000;
        }

        private static List<string> TranslateChunkOnce(
            ITranslationEngine translator,
            List<string> chunk,
            string targetLang,
            string language,
            CancellationToken cancellationToken)
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
