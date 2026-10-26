using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string MyMemoryQuotaWarning =
        "MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY. NEXT AVAILABLE IN 13 HOURS 31 MINUTES";

    private static void RegisterProviderLimitTests(TestRunner runner)
    {
        runner.Run("MyMemory requests carry no shared contact e-mail", () =>
        {
            Uri uri = MyMemoryTranslator.BuildRequestUri("Hello world", "zh-TW");
            Assert.True(!uri.Query.Contains("de=", StringComparison.OrdinalIgnoreCase),
                $"MyMemory request must not send a shared e-mail: {uri}");
            Assert.True(uri.Query.Contains("langpair=en%7Czh-TW", StringComparison.Ordinal),
                $"Unexpected MyMemory language pair: {uri}");
        });

        runner.Run("MyMemory quota warning is rejected instead of rendered", () =>
        {
            Assert.Throws<TranslationQuotaExceededException>(() => MyMemoryTranslator.ParseResponse(
                "{\"responseData\":{\"translatedText\":\"" + MyMemoryQuotaWarning + "\"},\"quotaFinished\":false,\"responseStatus\":429}",
                "Hello world"));
            Assert.Throws<TranslationQuotaExceededException>(() => MyMemoryTranslator.ParseResponse(
                "{\"responseData\":{\"translatedText\":\"你好\"},\"quotaFinished\":true,\"responseStatus\":200}",
                "Hello"));
            Assert.Throws<TranslationRateLimitException>(() => MyMemoryTranslator.ParseResponse(
                "{\"responseData\":{\"translatedText\":\"slow down\"},\"responseStatus\":429}",
                "Hello"));
            Assert.Throws<InvalidOperationException>(() => MyMemoryTranslator.ParseResponse(
                "{\"responseData\":{\"translatedText\":\"INVALID LANGUAGE PAIR\"},\"responseStatus\":\"403\"}",
                "Hello"));
            Assert.Equal("你好", MyMemoryTranslator.ParseResponse(
                "{\"responseData\":{\"translatedText\":\"你好\"},\"quotaFinished\":false,\"responseStatus\":200}",
                "Hello"));
        });

        runner.Run("Translation guard rejects a provider quota warning", () =>
        {
            Assert.True(
                TranslationResultQualityGuard.FindProblem("Hello world", MyMemoryQuotaWarning, "zh-TW") != null,
                "A quota warning must not pass as a Chinese translation.");
            Assert.True(
                TranslationResultQualityGuard.FindProblem("Hallo Welt", MyMemoryQuotaWarning, "en") != null,
                "A quota warning must not pass as an English translation.");
            Assert.True(
                TranslationResultQualityGuard.FindProblem(MyMemoryQuotaWarning, MyMemoryQuotaWarning, "en") == null,
                "Source text that itself quotes the warning must still be allowed.");
        });

        runner.Run("Google verification pages are treated as a rate limit", () =>
        {
            Assert.True(GoogleFreeTranslator.LooksLikeVerificationPage("text/html", ""), "text/html must be a verification page.");
            Assert.True(GoogleFreeTranslator.LooksLikeVerificationPage(null, "  <html><body>sorry</body></html>"),
                "An HTML body must be a verification page.");
            Assert.True(!GoogleFreeTranslator.LooksLikeVerificationPage("application/json", "[[\"你好\"]]"),
                "A JSON translation must not be mistaken for a verification page.");
        });

        runner.Run("Fallback translator reports a limited chain when the fallback cannot take over", () =>
        {
            var translator = new FallbackTranslator(
                new LimitedTranslationEngine("limited-primary", TimeSpan.FromSeconds(1)),
                new RecordingTranslationEngine(FallbackEngineName, failOnMarker: "x"));

            var ex = Assert.Throws<TranslationRateLimitException>(() =>
                translator.TranslateBatchAsync(new List<string> { "x" }, "zh-TW", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Assert.True(TranslationProviderLimits.IsLimit(ex), "The chain failure must remain recognisable as a limit.");
        });

        runner.Run("Fallback translator still falls back when only the primary is limited", () =>
        {
            var fallback = new RecordingTranslationEngine(FallbackEngineName);
            var translator = new FallbackTranslator(
                new LimitedTranslationEngine("limited-primary", TimeSpan.FromSeconds(1)),
                fallback);

            var results = translator.TranslateBatchAsync(new List<string> { "hello" }, "zh-TW", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.Equal("fallback:hello", results.Single());
            Assert.True(fallback.BatchSizes.Count == 1, "The fallback provider should take over once.");
        });

        runner.Run("PDF batch runner waits out a limit once instead of splitting", () =>
        {
            var translator = new LimitedTranslationEngine("always-limited", TimeSpan.FromSeconds(1));
            var source = Enumerable.Range(0, 6).Select(i => $"paragraph-{i}").ToList();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                PdfTranslationBatchRunner.TranslatePageBatches(
                    translator,
                    source,
                    "zh-TW",
                    new PdfTranslationBatchContext(0, 1, null, ""),
                    cancellationToken: CancellationToken.None));

            Assert.True(TranslationProviderLimits.IsLimit(ex), $"Expected a provider-limit error: {ex.Message}");
            Assert.True(translator.BatchAttempts == 2,
                $"Expected one attempt plus one retry, got {translator.BatchAttempts} batch calls.");
            Assert.True(translator.SingleAttempts == 0, "A limited batch must not be split into single calls.");
        });

        runner.Run("PDF batch runner recovers when a limit clears after one wait", () =>
        {
            var translator = new LimitedTranslationEngine("limited-once", TimeSpan.FromSeconds(1), limitedCalls: 1);
            var source = new List<string> { "one", "two" };

            var results = PdfTranslationBatchRunner.TranslatePageBatches(
                translator,
                source,
                "zh-TW",
                new PdfTranslationBatchContext(0, 1, null, ""),
                cancellationToken: CancellationToken.None);

            Assert.True(results.SequenceEqual(new[] { "limited-once:one", "limited-once:two" }),
                $"Unexpected results: {string.Join(", ", results)}");
            Assert.True(translator.BatchAttempts == 2, $"Expected two batch calls, got {translator.BatchAttempts}.");
        });
    }

    /// <summary>Throws a rate limit for the first <c>limitedCalls</c> calls (all calls when null).</summary>
    private sealed class LimitedTranslationEngine(string name, TimeSpan retryAfter, int? limitedCalls = null) : ITranslationEngine
    {
        public string Name { get; } = name;
        public int BatchAttempts { get; private set; }
        public int SingleAttempts { get; private set; }

        private bool StillLimited => limitedCalls == null || BatchAttempts + SingleAttempts <= limitedCalls;

        public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            SingleAttempts++;
            if (StillLimited)
                throw new TranslationRateLimitException($"{Name} is rate limited.", retryAfter);
            return Task.FromResult($"{Name}:{text}");
        }

        public Task<List<string>> TranslateBatchAsync(
            List<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            BatchAttempts++;
            if (StillLimited)
                throw new TranslationRateLimitException($"{Name} is rate limited.", retryAfter);
            return Task.FromResult(texts.Select(text => $"{Name}:{text}").ToList());
        }
    }
}
