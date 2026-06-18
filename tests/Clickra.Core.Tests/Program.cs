using Clickra.Core;

var runner = new TestRunner();

runner.Run("Pentest p14 gray prompt boxes stay bypassed", () =>
{
    var page = Diagnostics("PentestAgent_Agent Pentest.pdf", 14);

    Assert.True(page.GrayPromptShadedRegions.Count >= 4,
        $"Expected at least 4 gray prompt regions, got {page.GrayPromptShadedRegions.Count}.");

    AssertParagraph(page, "Search Results Summary Prompt", p =>
        p.IsGrayPromptContent && p.IsCode && p.IsBypassed && !p.IsDiagram && !p.IsTable);
    AssertParagraph(page, "Exploit Procedure Analysis Prompt", p =>
        p.IsGrayPromptContent && p.IsCode && p.IsBypassed && !p.IsDiagram && !p.IsTable);
    AssertParagraph(page, "Attack Surface Suggestion Prompt", p =>
        p.IsGrayPromptContent && p.IsCode && p.IsBypassed && !p.IsDiagram && !p.IsTable);
    AssertParagraph(page, "Exploit Suggestion Prompt", p =>
        p.IsGrayPromptContent && p.IsCode && p.IsBypassed && !p.IsDiagram && !p.IsTable);
    AssertParagraph(page, "Execution Information Query Prompt", p =>
        p.IsGrayPromptContent && p.IsCode && p.IsBypassed && !p.IsDiagram && !p.IsTable);
});

runner.Run("Final project p14 Work Division cells are tables, not diagrams", () =>
{
    var page = Diagnostics("114423046_final_project.pdf", 14);

    Assert.True(page.TableCount >= 30,
        $"Expected Work Division tableCount >= 30, got {page.TableCount}.");

    foreach (var text in new[]
    {
        "workflow design",
        "Embedding topic",
        "Attention build up",
        "attention and SHAP"
    })
    {
        AssertParagraph(page, text, p => p.IsTable && p.IsBypassed && !p.IsDiagram);
    }
});

runner.Run("Final project appendix feature tables stay bypassed", () =>
{
    var page15 = Diagnostics("114423046_final_project.pdf", 15);
    foreach (var text in new[] { "TV_SHORT)", "UNKNOWN_SOURCE", "VIDEO_GAME" })
    {
        AssertParagraph(page15, text, p =>
            p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
    }

    var page16 = Diagnostics("114423046_final_project.pdf", 16);
    foreach (var text in new[]
    {
        "titles from the retrieved title's",
        "retrieved studio score",
        "Jaccard"
    })
    {
        AssertParagraph(page16, text, p =>
            p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
    }
});

runner.Run("Final project p1 translated title clip follows rendered CJK height", () =>
{
    var method = typeof(FileProcessor).GetMethod(
        "GetPageOneTitleClipBottom",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    Assert.True(method != null, "Expected page-one title clip helper.");

    const double clipTop = 56.2;
    const double originalClipBottom = 72.3;
    const double measuredHeight = 24.3;
    double clipBottom = (double)method!.Invoke(
        null, new object[] { clipTop, originalClipBottom, measuredHeight })!;

    Assert.True(clipBottom >= clipTop + measuredHeight,
        $"Expected title clip to contain the measured height, got {clipBottom - clipTop:F1}.");
});

runner.Run("TOGLL p8 RQ4 body prose is translatable outside tables", () =>
{
    var page = Diagnostics("TOGLL_Oracle Generation.pdf", 8);

    AssertParagraph(page, "Experimental Setup", p =>
        !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent && p.IsBodyProse);
    AssertParagraph(page, "RQ3 Finding", p =>
        !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
});

runner.Run("SemTaint p8 short multi-token equations stay bypassed", () =>
{
    var page = Diagnostics("SemTaint.pdf", 8);

    AssertParagraph(page, "raw =", p =>
        p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
});

runner.Run("2407 references body is bypassed after heading", () =>
{
    var page = Diagnostics("2407.11279v1_clean.pdf", 14);

    AssertParagraph(page, "Polyscope", p =>
        p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
    AssertParagraph(page, "Flowdroid", p =>
        p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
});

runner.Run("2407 p7 Figure 5 labels stay inside the original diagram", () =>
{
    var page = Diagnostics("2407.11279v1_clean.pdf", 7);

    foreach (var text in new[]
    {
        "Preparing Program",
        "Compute Program",
        "Compute Environmental",
        "Compute Exploit",
        "Test for"
    })
    {
        AssertParagraph(page, text, p =>
            p.IsDiagram && p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
    }

    AssertParagraph(page, "PathSentinel processes Android APK files", p =>
        !p.IsDiagram && !p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
});

runner.Run("2407 p10 Tables II III IV preserve their original cells", () =>
{
    var page = Diagnostics("2407.11279v1_clean.pdf", 10);

    foreach (var text in new[]
    {
        "Total Apps",
        "Average Entry Count",
        "Samsung 12",
        "OnePlus",
        "Hijacking Positives",
        "True Positive Path Traversal",
        "Source-to-Sink Flow Count",
        "Total Pre",
        "Total Post"
    })
    {
        AssertParagraph(page, text, p =>
            p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
    }

    foreach (var text in new[] { "TABLE II", "TABLE III", "TABLE IV" })
    {
        AssertParagraph(page, text, p =>
            !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
    }
});

runner.Run("PostProcessTranslation fixes zh-TW terminology", () =>
{
    Assert.Equal(
        "大型語言模型的接收端會保留 user@example.com",
        FileProcessor.PostProcessTranslation(
            "The LLM sink keeps user@example.com",
            "法學碩士的水槽會保留 user @ example . com",
            "zh-TW"));

    Assert.Equal(
        "參考文獻",
        FileProcessor.PostProcessTranslation("REFERENCES", "引用", "zh-TW"));
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
        FileProcessor.RemoveDuplicateFormulaLiterals(
            "runtime features such as eval() {v0} are difficult",
            new List<MathFormula> { formula }));

    Assert.Equal(
        "runtime features such as eval() are difficult",
        FileProcessor.RemoveDuplicateFormulaLiterals(
            "runtime features such as eval() are difficult",
            new List<MathFormula> { formula }));

    Assert.Equal(
        "eval() is named earlier; runtime features such as {v0} are difficult",
        FileProcessor.RemoveDuplicateFormulaLiterals(
            "eval() is named earlier; runtime features such as eval() {v0} are difficult",
            new List<MathFormula> { formula }));
});

runner.Run("Math normalization removes non-printing font artifacts", () =>
{
    Assert.Equal(
        "λ̸t",
        FileProcessor.NormalizeMathValue("\0λ\u0001\u000C\u0338t"));
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

return runner.Failures == 0 ? 0 : 1;

static TranslationPageDiagnostics Diagnostics(string sourceFile, int page)
{
    var path = RepoRoot() / "test_pdfs" / "source" / sourceFile;
    Assert.True(File.Exists(path.Value), $"Missing test PDF: {path}");
    return FileProcessor.AnalyzePageParagraphDiagnostics(path.ToString(), page);
}

static PathInfo RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "test_pdfs", "source")) &&
            Directory.Exists(Path.Combine(dir.FullName, "src", "Clickra.Core")))
        {
            return new PathInfo(dir.FullName);
        }
        dir = dir.Parent;
    }

    throw new InvalidOperationException("Could not locate Clickra repo root.");
}

static void AssertParagraph(
    TranslationPageDiagnostics page,
    string text,
    Func<TranslationParagraphDiagnostics, bool> predicate)
{
    var matches = page.Paragraphs
        .Where(p => p.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
        .ToList();

    Assert.True(matches.Count > 0, $"Could not find paragraph containing '{text}' on page {page.PageNumber}.");
    Assert.True(matches.Any(predicate),
        $"Paragraph containing '{text}' did not satisfy predicate. Matches:\n" +
        string.Join("\n", matches.Select(Describe)));
}

static string Describe(TranslationParagraphDiagnostics p) =>
    $"  [{p.Index}] bypass={p.IsBypassed} table={p.IsTable} code={p.IsCode} " +
    $"diagram={p.IsDiagram} gray={p.IsGrayPromptContent} body={p.IsBodyProse} " +
    $"text='{Short(p.Text)}'";

static string Short(string value)
{
    value = value.Replace("\r", " ").Replace("\n", " ").Trim();
    return value.Length <= 120 ? value : value[..120] + "...";
}

sealed class PathInfo(string value)
{
    public static PathInfo operator /(PathInfo left, string right) =>
        new(Path.Combine(left.Value, right));

    public string Value { get; } = value;
    public override string ToString() => Value;
}

sealed class TestRunner
{
    public int Failures { get; private set; }

    public void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Failures++;
            Console.WriteLine($"FAIL {name}");
            Console.WriteLine(ex.Message);
        }
    }
}

static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void Equal(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
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
