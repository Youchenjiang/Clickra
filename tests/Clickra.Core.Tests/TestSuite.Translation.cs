using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Models;
using Clickra.Core.Processors;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

static partial class TestSuite
{
    private const string DfKaiSbFontName = "DFKai-SB";
    private const string CircledNumbersText = "①、②、③";
    private const string PrimaryEngineName = "primary";
    private const string FallbackEngineName = "fallback";
    private const string ProviderTimeoutEnvVar = "CLICKRA_TRANSLATION_PROVIDER_TIMEOUT_SECONDS";

    public static void RegisterTranslationTests(TestRunner runner)
    {
        RegisterFontAndNormalizationTests(runner);
        RegisterPostProcessAndTextTests(runner);
        RegisterEngineAndProcessorTests(runner);
    }

    private static void RegisterFontAndNormalizationTests(TestRunner runner)
    {
        runner.Run("CJK translation font resolves to embedded KaiU glyphs", () =>
        {
            var resolver = new ClickraFontResolver();
            foreach (var family in new[] { "DFKai-SB", "DFKai", "kaiu" })
            {
                var face = resolver.ResolveTypeface(family, isBold: true, isItalic: true);
                Assert.True(face?.FaceName == "kaiu",
                    $"Expected {family} to resolve to kaiu, got {face?.FaceName ?? "<null>"}.");
                if (face is not null)
                {
                    var bytes = resolver.GetFont(face.FaceName);
                    Assert.True(bytes?.Length > 1_000_000,
                        $"Expected an embedded KaiU font payload for {family}.");
                }
            }
        });

        runner.Run("Math normalization removes non-printing font artifacts", () =>
        {
            Assert.Equal(
                "λ̸t",
                FontUtilities.NormalizeMathValue("\0λ\u0001\u000C\u0338t"));
            Assert.Equal("①", FontUtilities.NormalizeMathValue("1⃝"));
            Assert.Equal("Pasareanu", FontUtilities.NormalizeMathValue("P˘as˘areanu"));
        });

        runner.Run("Math normalization preserves circled figure markers", () =>
        {
            Assert.Equal(
                CircledNumbersText,
                FontUtilities.NormalizeMathValue("1⃝、2⃝、3⃝"));
            Assert.True(
                PdfParagraphLayoutEngine.TokenizeTranslatedText(CircledNumbersText).Contains("①"),
                "Circled marker should remain an inline symbol token.");
            Assert.Equal(CircledNumbersText, FontUtilities.NormalizeRenderValue(CircledNumbersText));
        });
    }

    private static void RegisterPostProcessAndTextTests(TestRunner runner)
    {
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

            Assert.Equal(
                "使用大型語言模型生成",
                PdfTranslateProcessor.PostProcessTranslation(
                    "Generation with LLMs",
                    "大型語言模型一代",
                    "zh-TW"));

            Assert.Equal(
                "使用大型语言模型生成",
                PdfTranslateProcessor.PostProcessTranslation(
                    "Generation using an LLM",
                    "法学硕士的一代",
                    "zh-CN"));

            Assert.Equal(
                "自動測試生成",
                PdfTranslateProcessor.PostProcessTranslation(
                    "automated test generation",
                    "自動測試一代",
                    "zh-TW"));
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

        runner.Run("Caption marker formulas are restored inline", () =>
        {
            var formulas = Enumerable.Range(0, 3)
                .Select(id => new MathFormula
                {
                    Id = id,
                    Letters = new List<MathLetter> { new() { Value = "⃝" } }
                })
                .ToList();
            Assert.Equal(
                "Fig. 3: Overview. ①, ②, ③ represent prompts.",
                PdfParagraphMarkerNormalizer.Normalize(
                    "Fig. 3: Overview. {v0}, 1 {v1}, 2 {v2}3 represent prompts.",
                    formulas));
        });

        runner.Run("Provider cannot downgrade circled caption markers", () =>
        {
            Assert.Equal(
                "圖 3：ASTER 概述。①、②、③ 表示提示。",
                PdfParagraphMarkerNormalizer.RestoreTranslatedMarkers(
                    "圖 3：ASTER 概述。①、②、③ 表示提示。",
                    "圖 3：ASTER 概述。1、2、3 表示提示。"));
            Assert.Equal(
                "圖 3:ASTER 概述。 ①、②、③  代表測試產生、測試修復和覆蓋範圍增強提示。",
                PdfParagraphMarkerNormalizer.RestoreTranslatedMarkers(
                    "圖 3: Overview of ASTER. ①, ②, ③ represent test-generation, test-repair, and coverage-augmentation prompts.",
                    "圖 3:ASTER 概述。 1 、2 、3  代表測試產生、測試修復和覆蓋範圍增強提示。"));
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

        runner.Run("Synthetic CJK engine preserves placeholders for layout tests", () =>
        {
            var oldValue = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
            try
            {
                Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", "synthetic-cjk");
                var translator = TranslationEngineFactory.Create();
                Assert.Equal("synthetic-cjk", translator.Name);
                string translated = translator.TranslateAsync(
                        "runtime features {v0}.",
                        "zh-TW",
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.True(translated.Contains("{v0}", StringComparison.Ordinal), "Formula placeholder was changed.");
                Assert.True(translated.Contains('測'), "Synthetic CJK output did not contain a CJK glyph.");
                Assert.True(
                    translated.Length < "runtime features {v0}.".Length,
                    "Synthetic CJK output did not model the higher information density of CJK text.");
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
    }

    private static void RegisterEngineAndProcessorTests(TestRunner runner)
    {
        RegisterFallbackEngineTests(runner);
        RegisterProcessorAndPipelineTests(runner);
    }

    private static void RegisterFallbackEngineTests(TestRunner runner)
    {
        runner.Run("Fallback translator chunks batches and retries only failed chunks", () =>
        {
            var primary = new RecordingTranslationEngine(PrimaryEngineName, failOnMarker: "FAIL");
            var fallback = new RecordingTranslationEngine(FallbackEngineName);
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
            var primary = new RecordingTranslationEngine(PrimaryEngineName, failOnMarker: "FAIL");
            var fallback = new RecordingTranslationEngine(FallbackEngineName, dropLastBatchResult: true);
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

        runner.Run("Fallback translator rejects unchanged CJK provider output", () =>
        {
            var primary = new UnchangedTranslationEngine(PrimaryEngineName);
            var fallback = new RecordingTranslationEngine(FallbackEngineName);
            var translator = new FallbackTranslator(primary, fallback);
            const string source = "ASTER: Natural and Multi-language Unit Test Generation with LLMs";

            string result = translator.TranslateAsync(source, "zh-TW", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.Equal($"fallback:{source}", result);
            Assert.True(primary.SingleAttempts == 1, "Primary provider should be attempted once.");
            Assert.True(fallback.SingleAttempts == 1, "Fallback provider should handle unchanged output.");

            var batch = translator.TranslateBatchAsync(
                    new List<string> { source },
                    "zh-TW",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Assert.Equal($"fallback:{source}", batch.Single());
            Assert.True(fallback.BatchSizes.Count == 1, "Fallback provider should handle unchanged batch output.");
        });

        runner.Run("Fallback translator propagates caller cancellation without fallback", () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var primary = new RecordingTranslationEngine(PrimaryEngineName);
            var fallback = new RecordingTranslationEngine(FallbackEngineName);
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

        runner.Run("Fallback translator gives each provider an independent deadline", () =>
        {
            var oldTimeout = Environment.GetEnvironmentVariable(ProviderTimeoutEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(ProviderTimeoutEnvVar, "1");
                var primary = new DelayedTranslationEngine("slow-primary", delayMilliseconds: 1500);
                var fallback = new RecordingTranslationEngine(FallbackEngineName);
                var translator = new FallbackTranslator(primary, fallback);

                var result = translator.TranslateBatchAsync(
                        new List<string> { "deadline test" },
                        "zh-TW",
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.Equal("fallback:deadline test", result.Single());
                Assert.True(primary.BatchAttempts == 1, "Primary provider should be attempted once.");
                Assert.True(fallback.BatchSizes.Count == 1, "Fallback should run after primary timeout.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(ProviderTimeoutEnvVar, oldTimeout);
            }
        });

        runner.Run("Fallback translator fails closed when both provider deadlines expire", () =>
        {
            var oldTimeout = Environment.GetEnvironmentVariable(ProviderTimeoutEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(ProviderTimeoutEnvVar, "1");
                var translator = new FallbackTranslator(
                    new DelayedTranslationEngine("slow-primary", delayMilliseconds: 1500),
                    new DelayedTranslationEngine("slow-fallback", delayMilliseconds: 1500));

                Assert.Throws<TimeoutException>(() => translator.TranslateBatchAsync(
                        new List<string> { "deadline test" },
                        "zh-TW",
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            }
            finally
            {
                Environment.SetEnvironmentVariable(ProviderTimeoutEnvVar, oldTimeout);
            }
        });
    }

    private static void RegisterProcessorAndPipelineTests(TestRunner runner)
    {
        RegisterClassifierTests(runner);
        RegisterLayoutAndMaskPipelineTests(runner);
    }

    private static void RegisterClassifierTests(TestRunner runner)
    {
        runner.Run("Heading classifier recognizes Roman sections but not equation numbers", () =>
        {
            var heading = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "I. INTRODUCTION"
            };
            var equationNumber = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "2"
            };

            Assert.True(PdfParagraphSemanticClassifier.IsHeadingParagraph(heading),
                "Roman-numbered section should be treated as a heading.");
            Assert.True(!PdfParagraphSemanticClassifier.IsHeadingParagraph(equationNumber),
                "Standalone equation number must not become a heading.");
        });

        runner.Run("Heading classifier preserves short colon labels", () =>
        {
            var label = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "The main contributions of this work include:"
            };
            Assert.True(PdfParagraphSemanticClassifier.IsHeadingParagraph(label),
                "A short colon label should retain heading typography.");
        });

        runner.Run("Lower-case wide continuation remains translatable prose", () =>
        {
            var continuation = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "of test assertions, (3) meaningfulness of test sequences, (4)",
                X0 = 314,
                X1 = 545,
                Y0 = 102,
                Y1 = 109
            };
            Assert.True(PdfParagraphRoleClassifier.IsTranslatableBodyProse(continuation),
                "A wide lower-case continuation must not be treated as a bypass fragment.");
        });

        runner.Run("Reference section bypasses every continuation line", () =>
        {
            var heading = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "REFERENCES",
                AverageFontSize = 9.96,
                X0 = 150,
                X1 = 220,
                Y0 = 500,
                Y1 = 512
            };
            var entry = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "[25] N. Alshahwan, J. Chheda, and E. Wang, Automated unit improvement",
                X0 = 60,
                X1 = 300,
                Y0 = 450,
                Y1 = 460
            };
            var continuation = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "using large language models for testing, in the proceedings.",
                X0 = 60,
                X1 = 300,
                Y0 = 438,
                Y1 = 448
            };
            var pages = new List<List<PdfParagraph>> { new() { heading, entry, continuation } };

            PdfReferenceSectionBypasser.Apply(
                pages,
                new[] { 612d },
                (paragraphs, _) => paragraphs);

            Assert.True(entry.IsBypassed, "Numbered bibliography entry must bypass translation.");
            Assert.True(continuation.IsBypassed, "Bibliography continuation line must also bypass translation.");
        });

        runner.Run("Reference author initials do not terminate bibliography bypass", () =>
        {
            var authorContinuation = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "A. Panichella and G. Fraser",
                AverageFontSize = 9.96,
                X0 = 314,
                X1 = 545,
                Y0 = 650,
                Y1 = 657
            };

            Assert.True(!ReferenceSectionDetector.IsTerminator(authorContinuation),
                "An author-initial continuation must not end the reference section.");
        });

        runner.Run("References heading survives diagram misclassification", () =>
        {
            var bibliographyHeading = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "REFERENCES",
                AverageFontSize = 7.514,
                X0 = 150,
                X1 = 201,
                Y0 = 500,
                Y1 = 506,
                IsDiagram = true
            };
            var tableField = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "Reference",
                AverageFontSize = 9.96,
                X0 = 150,
                X1 = 222,
                Y0 = 500,
                Y1 = 510,
                IsTable = true
            };

            Assert.True(ReferenceSectionDetector.IsHeading(bibliographyHeading),
                "An unambiguous REFERENCES heading must start bibliography bypass despite a diagram flag.");
            Assert.True(!ReferenceSectionDetector.IsHeading(tableField),
                "A singular table field named Reference must not start bibliography bypass.");
        });

        runner.Run("Short research questions remain full-size prose", () =>
        {
            var question = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "RQ3: How natural are ASTER-generated tests?",
                X0 = 314,
                X1 = 545,
                Y0 = 563,
                Y1 = 570
            };
            Assert.True(PdfParagraphRoleClassifier.IsTranslatableCalloutProse(question),
                "RQ3 must not be treated as a tiny fixed-height label.");
        });

        runner.Run("Narrow lower-case continuation remains full-size prose", () =>
        {
            var continuation = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "search questions.",
                X0 = 65,
                X1 = 128,
                Y0 = 93,
                Y1 = 100
            };
            Assert.True(PdfParagraphRoleClassifier.IsTranslatableCalloutProse(continuation),
                "A narrow lower-case continuation must not be shrunk to its extraction box.");
        });

        runner.Run("Finding callouts preserve their fixed container geometry", () =>
        {
            var finding = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "Finding 5: Developers prefer ASTER-generated tests.",
                X0 = 67.6,
                X1 = 292.4,
                Y0 = 330.1,
                Y1 = 380.5
            };
            Assert.True(PdfParagraphRoleClassifier.IsFindingCallout(finding),
                "Singular numbered Finding callouts must be classified explicitly.");
            Assert.True(PdfParagraphRoleClassifier.IsTranslatableCalloutProse(finding),
                "Finding callouts remain translatable prose.");

            double x0 = finding.X0;
            double x1 = finding.X1;
            PdfMaskGeometry.ExpandMaskToColumnWidth(ref x0, ref x1, finding, 612);
            Assert.True(Math.Abs(finding.X0 - x0) < 0.001, "Finding mask must not expand left to the column edge.");
            Assert.True(Math.Abs(finding.X1 - x1) < 0.001, "Finding mask must not expand right to the column edge.");
        });

        runner.Run("Paragraph source visual font size survives title grouping", () =>
        {
            var title = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "Main title",
                IsPageTitle = true,
                SourceVisualFontSize = 18,
                AverageFontSize = 11
            };
            Assert.True(Math.Abs(title.SourceVisualFontSize - 18d) < 0.001,
                "Source visual font size was not retained.");
            Assert.True(title.IsPageTitle, "Title role was not retained in the source snapshot.");
        });

        runner.Run("Flowable translated body measures at source size before vertical balancing", () =>
        {
            try { GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch (InvalidOperationException) { /* FontResolver already initialized */ }
            using var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            using var gfx = XGraphics.FromPdfPage(page);
            var paragraph = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "A deliberately long body paragraph",
                TranslatedText = string.Concat(Enumerable.Repeat("這是一段用來驗證正文自然重排高度的翻譯文字。", 12)),
                SemanticRole = PdfParagraphSemanticRole.Body,
                X0 = 49,
                X1 = 300,
                Y0 = 500,
                Y1 = 520,
                AverageFontSize = 9.0,
                SourceVisualFontSize = 9.0,
                SourceLineHeight = 9.0
            };

            PdfParagraphRenderMetrics metrics = default;
            PdfTranslatedParagraphRenderer.RenderParagraph(
                gfx,
                paragraph,
                DfKaiSbFontName,
                measureOnly: true,
                metricsSink: value => metrics = value);

            Assert.True(metrics.LineCount > 2,
                "The fixture must wrap to multiple translated lines.");
            Assert.True(metrics.RenderedHeight > paragraph.Height,
                "Flowable body text must report its natural height instead of shrinking into the source box.");
            Assert.True(metrics.EffectiveFontSize >= paragraph.AverageFontSize - 0.01,
                "Space planning must not begin from a body font smaller than the source reading size.");
        });
    }

    private static void RegisterLayoutAndMaskPipelineTests(TestRunner runner)
    {
        RegisterLayoutBalancingTests(runner);
        RegisterMaskAndOverlayPipelineTests(runner);
    }

    private static void RegisterLayoutBalancingTests(TestRunner runner)
    {
        runner.Run("Vertical balancing treats spatial table masks as fixed boundaries", () =>
        {
            try { GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch (InvalidOperationException) { /* FontResolver already initialized */ }
            using var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            using var gfx = XGraphics.FromPdfPage(page);

            var unclassifiedTableText = LayoutParagraph(
                "Q1. Current Professional Role Open Q2. Years of experience",
                "問卷表格內容",
                320, 610, 550, 700,
                5.1);
            var firstBody = LayoutParagraph(
                "Developer-written tests were compared in an anonymous survey.",
                string.Concat(Enumerable.Repeat("開發人員撰寫的測試會在匿名問卷中進行比較。", 18)),
                312, 310, 563, 580);
            var secondBody = LayoutParagraph(
                "The survey received responses from several software roles.",
                string.Concat(Enumerable.Repeat("調查收到來自不同軟體職務的回覆。", 12)),
                312, 76, 563, 298);
            var protectedTable = new TableMaskRegion(310, 590, 565, 730);

            PdfTranslationLayoutPlanner.BuildAndApply(
                gfx,
                new[] { unclassifiedTableText, firstBody, secondBody },
                DfKaiSbFontName,
                612,
                792,
                new[] { protectedTable });

            Assert.True(firstBody.Y1 <= firstBody.OriginalY1 + 0.01,
                "Body text below a table must not be pulled upward into the protected table region.");
            Assert.True(!PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(
                    firstBody.X0, firstBody.Y0, firstBody.X1, firstBody.Y1, new[] { protectedTable }),
                "Balanced body geometry must remain outside the table mask.");
        });

        runner.Run("Vertical balancing ignores incidental fixed-region overlap", () =>
        {
            try { GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch (InvalidOperationException) { /* Font resolver already initialized */ }
            using var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            using var gfx = XGraphics.FromPdfPage(page);

            var bodyAboveCallout = LayoutParagraph(
                string.Join(' ', Enumerable.Repeat("translated body prose", 45)),
                string.Concat(Enumerable.Repeat("這是需要在固定標註框上方平衡的翻譯正文。", 9)),
                312, 332, 563, 518,
                9.96);
            var callout = LayoutParagraph(
                "Finding 6: The generated tests use meaningful names.",
                "發現六：產生的測試使用有意義的名稱。",
                315, 293, 560, 324,
                9.96);
            var calloutRegion = new TableMaskRegion(311, 284, 567, 355);

            var plan = PdfTranslationLayoutPlanner.BuildAndApply(
                gfx,
                new[] { bodyAboveCallout, callout },
                DfKaiSbFontName,
                612,
                792,
                new[] { calloutRegion });

            var bodySnapshot = plan.Snapshots.Single(s => s.Paragraph == bodyAboveCallout);
            Assert.True(bodyAboveCallout.Y1 < bodyAboveCallout.OriginalY1 - 0.5 ||
                        bodyAboveCallout.LayoutLineSpacingMultiplierOverride > 0,
                "A body paragraph that only grazes a fixed callout region must still participate in balancing.");
            Assert.True(bodyAboveCallout.Y1 - bodySnapshot.MeasuredHeight >= calloutRegion.Y1 - 0.5,
                "Balanced body text must remain above the fixed callout region.");
            Assert.True(plan.IsSuccessful,
                "Incidental protected-region overlap must not create a layout defect.");
        });

        runner.Run("Spatial table masks bypass thin merged table rows", () =>
        {
            var thinTableRow = LayoutParagraph(
                "Q6. Prior experience with automated test generation MCQ",
                "具有自動測試產生經驗",
                367.3, 693.7, 517.3, 697.2,
                5.1);
            var bodyBelowTable = LayoutParagraph(
                "The survey received responses from several software roles.",
                "調查收到來自不同軟體職務的回覆。",
                312, 302, 563, 584);
            var caption = LayoutParagraph(
                "TABLE III: Two groups of survey questions.",
                "表 III：兩組調查問題。",
                345.5, 748.6, 529.5, 755.4,
                7.5);
            caption.SemanticRole = PdfParagraphSemanticRole.FigureCaption;
            var tableRegion = new TableMaskRegion(310, 590, 565, 730);

            int marked = PdfTableMaskPlanner.MarkParagraphsInsideTableMasks(
                new List<PdfParagraph> { thinTableRow, bodyBelowTable, caption },
                new[] { tableRegion },
                PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph);

            Assert.True(marked == 1, $"Expected one promoted table row, got {marked}.");
            Assert.True(thinTableRow.IsTable && thinTableRow.IsBypassed,
                "A thin row centered inside the table mask must bypass translation.");
            Assert.True(!bodyBelowTable.IsTable && !bodyBelowTable.IsBypassed,
                "Body prose below the table must remain translatable.");
            Assert.True(!caption.IsTable && !caption.IsBypassed,
                "The table caption must remain translatable.");
        });

        runner.Run("Spatial table promotion expands until table rows stabilize", () =>
        {
            var headerLeft = LayoutParagraph("Type", "", 330, 700, 380, 710, 6);
            var headerRight = LayoutParagraph("Format", "", 500, 700, 550, 710, 6);
            headerLeft.IsTable = true;
            headerRight.IsTable = true;
            var firstBridge = LayoutParagraph("Q5. Level of expertise", "", 360, 688, 520, 696, 6);
            var secondBridge = LayoutParagraph("Q6. Prior experience", "", 360, 676, 520, 684, 6);

            int marked = PdfTableMaskPlanner.MarkParagraphsInsideTableMasksUntilStable(
                new List<PdfParagraph> { headerLeft, headerRight, firstBridge, secondBridge },
                612);

            Assert.True(marked == 2, $"Expected two promoted bridge rows, got {marked}.");
            Assert.True(firstBridge.IsTable && secondBridge.IsTable,
                "Each newly promoted short row must extend the table region for the next row.");
        });

        runner.Run("Spatial table promotion fills narrow aligned mask gaps", () =>
        {
            var topLeft = LayoutParagraph("Type", "", 345, 700, 380, 710, 6);
            var topRight = LayoutParagraph("Format", "", 500, 700, 530, 710, 6);
            var bottomLeft = LayoutParagraph("Q10", "", 345, 640, 380, 650, 6);
            var bottomRight = LayoutParagraph("Likert", "", 500, 640, 530, 650, 6);
            foreach (var seed in new[] { topLeft, topRight, bottomLeft, bottomRight })
                seed.IsTable = true;
            var gapRow = LayoutParagraph(
                "Q7. Prior experience with automated test generation",
                "",
                367, 668, 519, 686,
                6);

            int marked = PdfTableMaskPlanner.MarkParagraphsInsideTableMasksUntilStable(
                new List<PdfParagraph> { topLeft, topRight, bottomLeft, bottomRight, gapRow },
                612);

            Assert.True(marked == 1, $"Expected the aligned gap row to be promoted, got {marked}.");
            Assert.True(gapRow.IsTable && gapRow.IsBypassed,
                "A row in a narrow gap between aligned table masks must bypass translation.");
        });
    }

    private static void RegisterMaskAndOverlayPipelineTests(TestRunner runner)
    {
        runner.Run("Tall narrow table blocks survive prose cleanup", () =>
        {
            var mergedTableRows = LayoutParagraph(
                string.Join(' ', Enumerable.Repeat("survey question", 30)),
                "",
                365, 567, 508, 629,
                6);
            var fullColumnProse = LayoutParagraph(
                string.Join(' ', Enumerable.Repeat("survey prose", 30)),
                "",
                314, 329, 546, 555,
                9);

            Assert.True(!PdfTableMisclassifiedProseCleanup.IsTallFullColumnProse(
                    mergedTableRows, 60, 612),
                "A tall narrow block beneath a table caption must remain a table block.");
            Assert.True(PdfTableMisclassifiedProseCleanup.IsTallFullColumnProse(
                    fullColumnProse, 60, 612),
                "Tall full-column prose must still be cleared from table classification.");
        });

        runner.Run("Table captions classify merged sections without a word-level page hint", () =>
        {
            var caption = LayoutParagraph(
                "TABLE III: Survey Questions",
                "表 III：調查問題",
                345, 712, 515, 719,
                7.5);
            var mergedRows = LayoutParagraph(
                "Q1 Current Professional Role Open Q2 Years of experience MCQ Q3 Programming languages MCQ",
                "",
                365, 635, 508, 700,
                6);
            var secondSection = LayoutParagraph(
                "Q10 I understand what this test case is doing Likert Q11 I understand the assertions Likert",
                "",
                365, 550, 504, 605.5,
                6);
            var shortFinalSection = LayoutParagraph(
                "Q17 Test sequence is understandable Likert Q18 Values are appropriate Likert Q19 Overall quality Likert",
                "",
                365, 525, 504, 544,
                6);
            var bodyBelow = LayoutParagraph(
                "We selected these questions to characterize the survey participants.",
                "我們選擇這些問題來描述調查參與者。",
                314, 400, 548, 470,
                9);
            var paragraphs = new List<PdfParagraph> { caption, mergedRows, secondSection, shortFinalSection, bodyBelow };

            PdfTableClassifier.MarkTableParagraphs(paragraphs, 612, 792, isTablePage: false);
            PdfTableClassifier.ReclassifyTableMisclassifiedProse(paragraphs, 612);
            PdfTableClassifier.MarkCaptionDelimitedTableRegions(paragraphs, 612);

            Assert.True(mergedRows.IsTable,
                "A merged table body directly below its caption must be classified even when it is the only table candidate.");
            Assert.True(secondSection.IsTable,
                "A table section separated by a nominal 30-point band must remain inside the caption-delimited table.");
            Assert.True(shortFinalSection.IsTable,
                "A short final table section demoted by prose cleanup must be restored by the final caption pass.");
            Assert.True(!caption.IsTable,
                "The caption remains translatable and must not be classified as table content.");
            Assert.True(!bodyBelow.IsTable,
                "Prose separated from the table by a large gap must remain translatable.");
        });

        runner.Run("Translation health rejects fragmented flow whitespace", () =>
        {
            var acceptable = new PdfTranslationHealthReport
            {
                MinimumBodyFontRatio = 0.80,
                MaximumBodyFontRatio = 1.15,
                MaximumBodyLineSpacingMultiplier = 1.50,
                MaximumFlowRegionResidualWhitespace = 18.0
            };
            var fragmented = new PdfTranslationHealthReport
            {
                MinimumBodyFontRatio = 0.80,
                MaximumBodyFontRatio = 1.15,
                MaximumBodyLineSpacingMultiplier = 1.50,
                MaximumFlowRegionResidualWhitespace = 18.1
            };

            Assert.True(!acceptable.HasLayoutDefects,
                "Typography exactly on the documented limits should pass the health gate.");
            Assert.True(fragmented.HasLayoutDefects,
                "A flow region with excessive undistributed whitespace must fail the health gate.");
        });

        runner.Run("Page-one wrapped title lines share the title role", () =>
        {
            var title = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "ASTER: Natural and Multi-language Unit Test",
                X0 = 89.9, X1 = 512.9, Y0 = 667.4, Y1 = 682.4,
                AverageFontSize = 23.91, SourceVisualFontSize = 23.91
            };
            var generation = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "Generation",
                X0 = 197.8, X1 = 299.6, Y0 = 639.1, Y1 = 656.7,
                AverageFontSize = 23.91, SourceVisualFontSize = 23.91
            };
            var withLlms = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "with LLMs",
                X0 = 308.0, X1 = 405.0, Y0 = 639.1, Y1 = 656.7,
                AverageFontSize = 23.91, SourceVisualFontSize = 23.91
            };
            var page = new List<PdfParagraph> { title, generation, withLlms };

            PageOneLayoutClassifier.MergeTitleWithSubtitle(page, 792);

            Assert.True(page.Count == 2, "Wrapped title continuation lines should form one title paragraph.");
            Assert.True(title.IsPageTitle, "The first title line lost its page-title role.");
            Assert.True(page[1].IsPageTitle, "The wrapped title continuation lost its page-title role.");
            Assert.True(page[1].TextWithPlaceholders.Contains("Generation", StringComparison.Ordinal) &&
                        page[1].TextWithPlaceholders.Contains("with LLMs", StringComparison.Ordinal),
                "The wrapped title continuation was not coalesced.");
        });

        runner.Run("Page-one running header cannot replace the paper title", () =>
        {
            var runningHeader = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "2025 IEEE/ACM International Conference on Software Engineering",
                X0 = 62, X1 = 550, Y0 = 762, Y1 = 769,
                AverageFontSize = 5.2
            };
            var paperTitle = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "ASTER: Natural and Multi-language Unit Test",
                X0 = 90, X1 = 513, Y0 = 667, Y1 = 682,
                AverageFontSize = 18
            };

            Assert.True(
                ReferenceEquals(
                    paperTitle,
                    PageOneLayoutClassifier.FindTitleParagraph(
                        new List<PdfParagraph> { runningHeader, paperTitle }, 792)),
                "The publication running header was selected as the page title.");
        });

        runner.Run("Inline bold markers preserve Nimbus medium source runs", () =>
        {
            Assert.True(
                FontUtilities.IsSourceFontBold("ANFUWD+NimbusRomNo9L-Medi"),
                "Nimbus medium face used for IEEE bold text was not recognized.");

            var tokens = PdfParagraphLayoutEngine.TokenizeTranslatedText("{b}Code coverage:{/b} body");
            Assert.True(tokens.Contains("{b}") && tokens.Contains("{/b}"),
                "Inline bold markers were not preserved by the layout tokenizer.");
            Assert.Equal(
                "{b}程式碼覆蓋率:{/b} body",
                PostProcessor.Process("{b}Code coverage:{/b} body", "{b}程式碼覆蓋率:{/b} body", "zh-TW"));
        });

        runner.Run("Uniform bold PDF paragraphs do not send per-line style markers", () =>
        {
            Assert.Equal(
                "Abstract—first line second line",
                PdfParagraphMarkerNormalizer.NormalizeStyleRuns(
                    "Abstract—first line second line",
                    "{b}Abstract—first line{/b} {b}second line{/b}",
                    isUniformlyBold: true));
            Assert.Equal(
                "{b}label one label two{/b} body",
                PdfParagraphMarkerNormalizer.NormalizeStyleRuns(
                    "label one label two body",
                    "{b}label one{/b} {b}label two{/b} body",
                    isUniformlyBold: false));
        });

        runner.Run("Translation guard rejects corrupted markers and tripled phrases", () =>
        {
            Assert.True(
                TranslationResultQualityGuard.FindProblem(
                    "{b}Important{/b} body",
                    "{b}重要}/b}正文",
                    "zh-TW") != null,
                "A corrupted closing bold marker must fail closed.");
            Assert.True(
                TranslationResultQualityGuard.FindProblem(
                    "Despite this effort, usable tools remain scarce.",
                    "儘管付出了這樣的努力，儘管付出了這樣的努力，儘管付出了這樣的努力。",
                    "zh-TW") != null,
                "A tripled provider phrase must fail closed.");
            Assert.True(
                TranslationResultQualityGuard.FindProblem(
                    "We evaluate standard and enterprise Java applications.",
                    "我們評估標準 as well as a 大型基準。",
                    "zh-TW") != null,
                "An English connective run in CJK output must fail closed.");
        });

        runner.Run("Chinese post-processing removes provider token spacing", () =>
            Assert.Equal(
                "摘要—實作自動化單元測試是一項重要但耗時的活動。",
                PostProcessor.Process(
                    "Abstract—Implementing automated unit tests is important.",
                    "摘要：實作 自動化 單元 測試 是 一項 重要 但 耗時 的 活動 。",
                    "zh-TW")));

        runner.Run("Academic table headers stay bypassed and bold", () =>
        {
            var header = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
            {
                TextWithPlaceholders = "Model Name Provider Update Date Model Size",
                IsBold = true,
                IsTable = true,
                AverageFontSize = 5.74,
                Y0 = 698.5,
                Y1 = 702.5,
                X0 = 77.7,
                X1 = 204.2
            };

            PdfTableMisclassifiedProseCleanup.Reclassify(new List<PdfParagraph> { header }, 612);
            Assert.True(header.IsTable, "A bold compact table header was demoted to prose.");
            Assert.True(
                PdfTableMisclassifiedProseCleanup.IsLikelyTableHeader(header),
                "The table-header guard did not recognize the source header row.");
        });

        runner.Run("PDF batch runner splits failed batches and recovers each paragraph", () =>
        {
            var translator = new BatchOnlyFailingTranslationEngine();
            var source = Enumerable.Range(0, 7).Select(i => $"paragraph-{i}").ToList();

            var results = PdfTranslationBatchRunner.TranslatePageBatches(
                translator,
                source,
                "zh-TW",
                pageIndex: 0,
                totalPages: 1,
                onProgress: null,
                cancellationToken: CancellationToken.None);

            Assert.True(source.Count == results.Count, $"Expected {source.Count} results, got {results.Count}.");
            Assert.True(
                results.All(result => result.StartsWith("recovered:", StringComparison.Ordinal)),
                "Every paragraph should recover through the single-item path.");
            Assert.True(translator.BatchAttempts > 1, "The failed batch should have been split recursively.");
        });

        runner.Run("PDF batch runner bounds a hung provider call", () =>
        {
            var oldTimeout = Environment.GetEnvironmentVariable(ProviderTimeoutEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(ProviderTimeoutEnvVar, "1");
                var translator = new HangingTranslationEngine();

                var ex = Assert.Throws<Exception>(() =>
                    PdfTranslationBatchRunner.TranslatePageBatches(
                        translator,
                        new List<string> { "hung" },
                        "zh-TW",
                        pageIndex: 0,
                        totalPages: 1,
                        onProgress: null,
                        cancellationToken: CancellationToken.None));

                Assert.True(
                    ex.Message.Contains("Unable to translate page 1", StringComparison.Ordinal),
                    $"Unexpected timeout recovery error: {ex.Message}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(ProviderTimeoutEnvVar, oldTimeout);
            }
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

    private sealed class RecordingTranslationEngine(
        string name,
        string? failOnMarker = null,
        bool dropLastBatchResult = false) : ITranslationEngine
    {
        public string Name { get; } = name;
        public List<int> BatchSizes { get; } = new();
        public int SingleAttempts { get; private set; }

        public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            SingleAttempts++;
            if (failOnMarker != null && text.Contains(failOnMarker, StringComparison.Ordinal))
                throw new InvalidOperationException($"Engine '{Name}' triggered on '{text}'.");
            return Task.FromResult($"{Name}:{text}");
        }

        public Task<List<string>> TranslateBatchAsync(
            List<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            BatchSizes.Add(texts.Count);
            if (failOnMarker != null && texts.Any(text => text.Contains(failOnMarker, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Engine '{Name}' triggered in batch.");
            var results = texts.Select(text => $"{Name}:{text}").ToList();
            if (dropLastBatchResult && results.Count > 0)
                results.RemoveAt(results.Count - 1);
            return Task.FromResult(results);
        }
    }

    private sealed class UnchangedTranslationEngine(string name) : ITranslationEngine
    {
        public string Name { get; } = name;
        public int SingleAttempts { get; private set; }

        public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            SingleAttempts++;
            return Task.FromResult(text);
        }

        public Task<List<string>> TranslateBatchAsync(
            List<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult(texts.ToList());
    }

    private sealed class BatchOnlyFailingTranslationEngine : ITranslationEngine
    {
        public string Name => "batch-failing";
        public int BatchAttempts { get; private set; }

        public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken) =>
            Task.FromResult($"recovered:{text}");

        public Task<List<string>> TranslateBatchAsync(
            List<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            BatchAttempts++;
            throw new InvalidOperationException("forced batch failure");
        }
    }

    private sealed class HangingTranslationEngine : ITranslationEngine
    {
        public string Name => "hanging";

        public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return text;
        }

        public async Task<List<string>> TranslateBatchAsync(
            List<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return texts;
        }
    }

    internal sealed class DelayedTranslationEngine(string name, int delayMilliseconds) : ITranslationEngine
    {
        public string Name { get; } = name;
        public int BatchAttempts { get; private set; }

        public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            await Task.Delay(delayMilliseconds, cancellationToken);
            return $"{Name}:{text}";
        }

        public async Task<List<string>> TranslateBatchAsync(
            List<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            BatchAttempts++;
            await Task.Delay(delayMilliseconds, cancellationToken);
            return texts.Select(text => $"{Name}:{text}").ToList();
        }
    }
}
