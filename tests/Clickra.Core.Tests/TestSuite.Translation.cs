using Clickra.Core;
using Clickra.Core.Models;
using Clickra.Core.Processors;

static partial class TestSuite
{
    public static void RegisterTranslationTests(TestRunner runner)
    {
        runner.Run("CJK translation font resolves to embedded KaiU glyphs", () =>
        {
            var resolver = new ClickraFontResolver();
            foreach (var family in new[] { "DFKai-SB", "DFKai", "kaiu" })
            {
                var face = resolver.ResolveTypeface(family, isBold: true, isItalic: true);
                Assert.True(face != null && face.FaceName == "kaiu",
                    $"Expected {family} to resolve to kaiu, got {face?.FaceName ?? "<null>"}.");
                var bytes = resolver.GetFont(face!.FaceName);
                Assert.True(bytes != null && bytes.Length > 1_000_000,
                    $"Expected an embedded KaiU font payload for {family}.");
            }
        });

        runner.Run("PostProcessTranslation fixes zh-TW terminology", () =>
        {
            Assert.Equal(
                "大型語言模型的接收端會保留 user@example.com",
                PdfTranslateProcessor.PostProcessTranslation(
                    "The LLM sink keeps user@example.com",
                    "法學碩士的水槽會保留 user @ example . com",
                    "zh-TW"));

            Assert.Equal(
                "參考文獻",
                PdfTranslateProcessor.PostProcessTranslation("REFERENCES", "引用", "zh-TW"));
        });

        runner.Run("Formula literals are not rendered twice beside placeholders", () =>
        {
            var formula = new MathFormula
            {
                Letters = "eval()"
                    .Select((value, index) => new MathLetter
                    {
                        Value = value.ToString(),
                        RelativeX = index
                    })
                    .ToList()
            };

            Assert.Equal(
                "runtime features such as {v0} are difficult",
                PdfTranslateProcessor.RemoveDuplicateFormulaLiterals(
                    "runtime features such as eval() {v0} are difficult",
                    new List<MathFormula> { formula }));

            Assert.Equal(
                "runtime features such as eval() are difficult",
                PdfTranslateProcessor.RemoveDuplicateFormulaLiterals(
                    "runtime features such as eval() are difficult",
                    new List<MathFormula> { formula }));

            Assert.Equal(
                "eval() is named earlier; runtime features such as {v0} are difficult",
                PdfTranslateProcessor.RemoveDuplicateFormulaLiterals(
                    "eval() is named earlier; runtime features such as eval() {v0} are difficult",
                    new List<MathFormula> { formula }));
        });

        runner.Run("Math normalization removes non-printing font artifacts", () =>
        {
            Assert.Equal(
                "λ̸t",
                FontUtilities.NormalizeMathValue("\0λ\u0001\u000C\u0338t"));
        });

        runner.Run("Identity translation engine is opt-in for layout tests", () =>
        {
            var oldValue = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
            try
            {
                Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", "identity");
                var translator = TranslationEngineFactory.Create();
                Assert.Equal("identity", translator.Name);
                Assert.Equal(
                    "Keep layout text unchanged.",
                    translator.TranslateAsync("Keep layout text unchanged.", "zh-TW", CancellationToken.None)
                        .GetAwaiter()
                        .GetResult());
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", oldValue);
            }
        });

        runner.Run("Translation engine defaults to Google with MyMemory fallback", () =>
        {
            var oldValue = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
            try
            {
                Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", null);
                Assert.Equal("google-free+mymemory", TranslationEngineFactory.Create().Name);

                Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", "mymemory");
                Assert.Equal("mymemory", TranslationEngineFactory.Create().Name);

                Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", "google");
                Assert.Equal("google-free", TranslationEngineFactory.Create().Name);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", oldValue);
            }
        });

        runner.Run("Fallback translator chunks batches and retries only failed chunks", () =>
        {
            var primary = new RecordingTranslationEngine("primary", failOnMarker: "FAIL");
            var fallback = new RecordingTranslationEngine("fallback");
            var translator = new FallbackTranslator(primary, fallback);
            var texts = Enumerable.Range(0, 30)
                .Select(i => i == 25 ? "FAIL " + new string('x', 280) : $"item-{i} " + new string('x', 280))
                .ToList();

            var results = translator.TranslateBatchAsync(texts, "zh-TW", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.True(results.Count == texts.Count, $"Expected {texts.Count} results, got {results.Count}.");
            Assert.True(primary.BatchSizes.Count >= 2, "Expected the primary translator to receive multiple chunks.");
            Assert.True(primary.BatchSizes.All(size => size <= 24), "Primary batch exceeded 24 items.");
            Assert.True(fallback.BatchSizes.Count == 1, $"Expected one fallback chunk, got {fallback.BatchSizes.Count}.");
            Assert.True(results.Any(result => result.StartsWith("fallback:", StringComparison.Ordinal)),
                "Expected fallback output for the failed chunk.");
        });

        runner.Run("Fallback translator rejects incomplete fallback batches", () =>
        {
            var primary = new RecordingTranslationEngine("primary", failOnMarker: "FAIL");
            var fallback = new RecordingTranslationEngine("fallback", dropLastBatchResult: true);
            var translator = new FallbackTranslator(primary, fallback);

            var ex = Assert.Throws<Exception>(() =>
                translator.TranslateBatchAsync(
                        new List<string> { "ok", "FAIL" },
                        "zh-TW",
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            Assert.True(ex.Message.Contains("returned 1/2 results", StringComparison.Ordinal),
                $"Unexpected exception: {ex.Message}");
        });

        runner.Run("Fallback translator propagates caller cancellation without fallback", () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var primary = new RecordingTranslationEngine("primary");
            var fallback = new RecordingTranslationEngine("fallback");
            var translator = new FallbackTranslator(primary, fallback);

            Assert.Throws<OperationCanceledException>(() =>
                translator.TranslateBatchAsync(
                        new List<string> { "cancel me" },
                        "zh-TW",
                        cts.Token)
                    .GetAwaiter()
                    .GetResult());

            Assert.True(fallback.BatchSizes.Count == 0,
                "Fallback must not run after caller cancellation.");
        });

        runner.Run("PdfBypassedParagraphRenderer handles ligatures correctly without crashing", () =>
        {
            var allLetters = new List<PdfLetter>
            {
                new PdfLetter { Value = "f", X = 10, Y = 10 },
                new PdfLetter { Value = "i", X = 20, Y = 10 },
                new PdfLetter { Value = "n", X = 30, Y = 10 },
                new PdfLetter { Value = "d", X = 40, Y = 10 }
            };

            var formulaLetters = new List<MathLetter>
            {
                new MathLetter { Value = "fi", X = 10, Y = 10 }
            };

            int index = PdfBypassedParagraphRenderer.FindFormulaSubsequence(allLetters, formulaLetters);
            Assert.True(index == -1, $"Expected -1, got {index}");

            var formulaLettersMatching = new List<MathLetter>
            {
                new MathLetter { Value = "f", X = 10, Y = 10 },
                new MathLetter { Value = "i", X = 20, Y = 10 }
            };
            int indexMatching = PdfBypassedParagraphRenderer.FindFormulaSubsequence(allLetters, formulaLettersMatching);
            Assert.True(indexMatching == 0, $"Expected 0, got {indexMatching}");
        });

        if (string.Equals(Environment.GetEnvironmentVariable("CLICKRA_RUN_TRANSLATION_SMOKE"), "1", StringComparison.Ordinal))
        {
            runner.Run("MyMemory translator smoke test", () =>
            {
                var translator = new MyMemoryTranslator();
                string translated = translator.TranslateAsync(
                        "This vulnerability allows remote attackers to execute arbitrary code.",
                        "zh-TW",
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.True(
                    translated.Contains('攻') || translated.Contains('弱') || translated.Contains('漏'),
                    $"Unexpected MyMemory translation: {translated}");
            });
        }
    }
}

sealed class RecordingTranslationEngine(
    string name,
    string? failOnMarker = null,
    bool dropLastBatchResult = false) : ITranslationEngine
{
    public string Name { get; } = name;
    public List<int> BatchSizes { get; } = new();

    public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (failOnMarker != null && text.Contains(failOnMarker, StringComparison.Ordinal))
            throw new InvalidOperationException("forced failure");
        return Task.FromResult($"{Name}:{text}");
    }

    public Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        BatchSizes.Add(texts.Count);
        if (failOnMarker != null && texts.Any(text => text.Contains(failOnMarker, StringComparison.Ordinal)))
            throw new InvalidOperationException("forced failure");
        var results = texts.Select(text => $"{Name}:{text}").ToList();
        if (dropLastBatchResult && results.Count > 0)
            results.RemoveAt(results.Count - 1);
        return Task.FromResult(results);
    }
}
