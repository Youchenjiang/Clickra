using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string MyMemoryQuotaWarning =
        "MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY. NEXT AVAILABLE IN 13 HOURS 31 MINUTES";
    private const string TargetLanguageZhTw = "zh-TW";
    private const string TargetLanguageZhCn = "zh-CN";
    private const string SampleHelloWorld = "Hello world";
    private const string SampleHello = "Hello";

    private static void RegisterProviderLimitTests(TestRunner runner)
    {
        runner.Run("OpenAI translator normalizes endpoint URL variants", () =>
        {
            Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiTranslator.NormalizeEndpoint(null));
            Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiTranslator.NormalizeEndpoint(""));
            Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiTranslator.NormalizeEndpoint("https://api.openai.com/v1"));
            Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiTranslator.NormalizeEndpoint("https://api.openai.com/v1/"));
            Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiTranslator.NormalizeEndpoint("https://api.openai.com/v1/chat/completions"));
            Assert.Equal("http://localhost:11434/v1/chat/completions", OpenAiTranslator.NormalizeEndpoint("http://localhost:11434/v1"));
        });

        runner.Run("OpenAI translator parses valid chat completion response and strips code fences", () =>
        {
            string normalJson = "{\"choices\":[{\"message\":{\"content\":\"你好世界\"}}]}";
            Assert.Equal("你好世界", OpenAiTranslator.ParseResponse(normalJson, "Hello world"));

            string fencedJson = "{\"choices\":[{\"message\":{\"content\":\"```markdown\\n你好世界\\n```\"}}]}";
            Assert.Equal("你好世界", OpenAiTranslator.ParseResponse(fencedJson, "Hello world"));

            string errorJson = "{\"error\":{\"message\":\"Invalid key\"}}";
            Assert.Throws<InvalidOperationException>(() => OpenAiTranslator.ParseResponse(errorJson, "Hello"));

            string emptyJson = "{\"choices\":[]}";
            Assert.Throws<InvalidOperationException>(() => OpenAiTranslator.ParseResponse(emptyJson, "Hello"));
        });

        runner.Run("OpenAI translator parses batch JSON array responses", () =>
        {
            var valid = OpenAiTranslator.ParseJsonArray("[\"一\", \"二\", \"三\"]", 3);
            Assert.True(valid != null && valid.Count == 3, "Valid JSON array of 3 items should parse successfully");
            Assert.Equal("一", valid![0]);
            Assert.Equal("二", valid[1]);
            Assert.Equal("三", valid[2]);

            var fenced = OpenAiTranslator.ParseJsonArray("```json\n[\"一\", \"二\"]\n```", 2);
            Assert.True(fenced != null && fenced.Count == 2, "Markdown fenced JSON array should parse successfully");

            var countMismatch = OpenAiTranslator.ParseJsonArray("[\"一\"]", 2);
            Assert.True(countMismatch == null, "Count mismatch should return null");

            var nonJson = OpenAiTranslator.ParseJsonArray("Just text", 1);
            Assert.True(nonJson == null, "Non-JSON response should return null");
        });

        runner.Run("Translation guard rejects a provider quota warning", () =>
        {
            Assert.True(
                TranslationResultQualityGuard.FindProblem(SampleHelloWorld, MyMemoryQuotaWarning, TargetLanguageZhTw) != null,
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

            using var challengeResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<html><body>sorry</body></html>", System.Text.Encoding.UTF8, "text/html")
            };
            Assert.Throws<TranslationRateLimitException>(() =>
                GoogleFreeTranslator.ProcessResponse(challengeResponse, "<html><body>sorry</body></html>"));

            using var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limit", System.Text.Encoding.UTF8, "text/plain")
            };
            Assert.Throws<TranslationRateLimitException>(() =>
                GoogleFreeTranslator.ProcessResponse(rateLimitResponse, "rate limit"));

            using var serverErrorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("server error", System.Text.Encoding.UTF8, "text/plain")
            };
            Assert.Throws<HttpRequestException>(() =>
                GoogleFreeTranslator.ProcessResponse(serverErrorResponse, "server error"));

            using var successResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            };
            string result = GoogleFreeTranslator.ProcessResponse(successResponse, "{\"ok\":true}");
            Assert.Equal("{\"ok\":true}", result);
        });

        runner.Run("Fallback translator reports a limited chain when the fallback cannot take over", () =>
        {
            var translator = new LimitedTranslationEngine("limited-primary", TimeSpan.FromSeconds(1));
            var fallback = new RecordingTranslationEngine(FallbackEngineName, failOnMarker: "x");
            var combined = new FallbackTranslator(translator, fallback);

            var ex = Assert.Throws<TranslationRateLimitException>(() =>
                combined.TranslateBatchAsync(new List<string> { "x" }, TargetLanguageZhTw, CancellationToken.None)
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

            var results = translator.TranslateBatchAsync(new List<string> { "hello" }, TargetLanguageZhTw, CancellationToken.None)
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
                    TargetLanguageZhTw,
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
                TargetLanguageZhTw,
                new PdfTranslationBatchContext(0, 1, null, ""),
                cancellationToken: CancellationToken.None);

            Assert.True(results.SequenceEqual(new[] { "limited-once:one", "limited-once:two" }),
                $"Unexpected results: {string.Join(", ", results)}");
            Assert.True(translator.BatchAttempts == 2, $"Expected two batch calls, got {translator.BatchAttempts}.");
        });

        runner.Run("PDF batch runner reports countdown progress during rate limit backoff", () =>
        {
            var translator = new LimitedTranslationEngine("limited-once", TimeSpan.FromSeconds(1), limitedCalls: 1);
            var source = new List<string> { "one" };
            var progressMessages = new List<string>();

            _ = PdfTranslationBatchRunner.TranslatePageBatches(
                translator,
                source,
                TargetLanguageZhTw,
                new PdfTranslationBatchContext(0, 1, (prog, max, msg) => progressMessages.Add(msg), TargetLanguageZhTw),
                cancellationToken: CancellationToken.None);

            Assert.True(progressMessages.Any(m => m.Contains("限流") || m.Contains("等待")),
                $"Expected countdown progress message during wait, got: {string.Join("; ", progressMessages)}");
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
