using Clickra.Core.Processors;
using Clickra.Core.Models;

static partial class TestSuite
{
    public static void RegisterFigureRegressionTests(TestRunner runner)
    {
        RegisterFigureLinkTests(runner);
        RegisterFigureLabelTests(runner);
    }

    private static void RegisterFigureLinkTests(TestRunner runner)
    {
        runner.Run("Figure link matching keeps only the linked number span", () =>
        {
            var chars = "Fig. 2(c)".Select((character, index) => new RenderedChar
            {
                Character = character,
                Left = index * 5,
                Right = index * 5 + 4,
                Bottom = 100,
                Top = 110
            }).ToList();

            var occurrence = PdfAnnotationOccurrenceFinder.FindFigureRefOccurrences(
                chars.Where(character => !char.IsWhiteSpace(character.Character)).ToList(),
                "2",
                includeClosingParen: false);
            Assert.True(occurrence.Count == 1 && occurrence[0].Count == 1 && occurrence[0][0].Character == '2',
                "Figure links must cover the linked number, not the translated Fig/圖 prefix.");
        });

        runner.Run("Figure link occurrence ordinal survives repeated references", () =>
        {
            var letters = "Fig. 2(c) text Fig. 2(b)".Select((character, index) => new PdfLetter
            {
                Value = character.ToString(),
                X = index,
                Left = index,
                Right = index + 1,
                Y = 100,
                Bottom = 100,
                Top = 110
            }).ToList();

            int secondDigit = -1;
            for (int index = 0; index < letters.Count; index++)
            {
                if (letters[index].Value == "2" &&
                    string.Concat(letters.Take(index).Select(previous => previous.Value)).EndsWith("Fig. ", StringComparison.Ordinal))
                {
                    if (secondDigit < 0)
                    {
                        secondDigit = index;
                    }
                    else
                    {
                        secondDigit = index;
                        break;
                    }
                }
            }
            Assert.True(
                PdfAnnotationOccurrenceMatcher.GetFigureReferenceIndex(letters, secondDigit) == 1,
                "The second repeated figure reference should receive ordinal 1.");
        });
    }

    private static void RegisterFigureLabelTests(TestRunner runner)
    {

        runner.Run("Figure caption masks stop below diagram borders", () =>
        {
            // The translated Fig. 4 caption grows upward in PDF coordinates.
            // Its mask must stop before the diagram's bottom vector edge.
            var diagram = new List<TableMaskRegion>
            {
                new(64, 181.4, 548, 620)
            };

            var clamped = PdfOverlayMaskPlanner.ClampMaskTopBelowDiagrams(
                maskX0: 126.3,
                maskY0: 179.9,
                maskX1: 476.5,
                maskY1: 196.1,
                regions: diagram,
                pageWidth: 612);

            Assert.True(clamped <= 179.4,
                $"Caption mask crossed the diagram bottom border: clampedY1={clamped:0.###}.");
        });

        runner.Run("A diagram mask alone is not a gray prompt region", () =>
        {
            var diagram = new List<TableMaskRegion>
            {
                new(310.5, 642.9, 549.4, 725.8)
            };
            var labels = new List<PdfParagraph>
            {
                UninitializedParagraph("Identify test scope", 42, 5),
                UninitializedParagraph("Mocking fields", 34, 5)
            };
            labels[0].X0 = 350; labels[0].X1 = 392; labels[0].Y0 = 690; labels[0].Y1 = 695;
            labels[1].X0 = 450; labels[1].X1 = 484; labels[1].Y0 = 680; labels[1].Y1 = 685;

            var shaded = PdfGrayPromptRegionBuilder.GetGrayPromptShadedRegions(diagram, 612, labels);
            Assert.True(shaded.Count == 0,
                "A workflow diagram was incorrectly promoted to a gray-prompt mask.");
        });

        runner.Run("Short workflow labels remain bypassed after diagram cleanup", () =>
        {
            var diagram = new List<TableMaskRegion>
            {
                new(310, 640, 550, 726)
            };
            var labels = new List<PdfParagraph>
            {
                UninitializedParagraph("for private", 24, 4),
                UninitializedParagraph("mocking types", 30, 4)
            };
            labels[0].X0 = 414; labels[0].X1 = 438; labels[0].Y0 = 698; labels[0].Y1 = 702;
            labels[1].X0 = 503; labels[1].X1 = 532; labels[1].Y0 = 700; labels[1].Y1 = 704;

            PdfDiagramLabelMarker.FinalizeShortFigureLabels(labels, diagram);

            Assert.True(labels.All(p => p.IsDiagram && p.IsBypassed && !p.IsTable),
                "Short workflow labels were left translatable after diagram cleanup.");
        });

        runner.Run("Combined chart subfigure labels remain bypassed", () =>
        {
            var caption = UninitializedParagraph(
                "Fig. 5: Line, branch, and method coverage achieved on Java applications",
                width: 340,
                height: 10);
            var labels = UninitializedParagraph(
                "(e) CargoTracker (f) PetClinic (g) DayTrader (h) App X",
                width: 210,
                height: 10);
            var page = new List<PdfParagraph> { caption, labels };

            Assert.True(PdfChartLabelClassifier.IsLikelyChartLabel(labels),
                "Combined subfigure labels should be recognized as chart artwork.");
            PdfDiagramLabelMarker.ReclassifyStandaloneChartLabelsAsDiagram(page);
            Assert.True(labels.IsDiagram && !labels.IsTable,
                "Combined chart labels must be classified as diagram artwork.");
        });

        runner.Run("Pentest p9 Figure 7 bar chart stays inside the original figure", () =>
        {
            var page = Diagnostics("PentestAgent_Agent Pentest.pdf", 9);

            foreach (var text in new[]
            {
                "Success Rate (%)",
                "GPT-4",
                "GPT-3.5",
                "Models"
            })
            {
                AssertParagraph(page, text, p => p.IsBypassed);
            }

            AssertParagraph(page, "Success rate on penetration testing tasks", p =>
                !p.IsDiagram && !p.IsBypassed && !p.IsTable);
        });

        runner.Run("Pentest Figure 7 success-rate axis label is chart protected", () =>
        {
            var method = typeof(PdfTranslateProcessor).Assembly
                .GetType("Clickra.Core.Processors.PdfTranslationPipeline")
                ?.GetMethod(
                "IsLikelyBarChartAxisLabel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.True(method != null, "Expected bar-chart axis label helper.");

            var axis = UninitializedParagraph("Success Rate (%) 60", width: 55, height: 35);
            Assert.True(method != null && (bool)(method.Invoke(null, new object[] { axis }) ?? false),
                "Expected merged Success Rate (%) y-axis label to be treated as a chart label.");
        });

        runner.Run("TOGLL p8 Figure 4 source code stays inside the original figure", () =>
            VerifyTogllFigureSourceCode(8, new[]
            {
                "public void test3",
                "assertSame(oA1, oA0)",
                "public void test9",
                "assertEquals((-119.4)",
                "public void test14",
                "Ground Truth"
            }, "Diverse yet correct test oracles"));

        runner.Run("TOGLL p9 Figure 5 source code stays inside the original figure", () =>
            VerifyTogllFigureSourceCode(9, new[]
            {
                "calculatePrintedLength",
                "public void test327",
                "l0.getVariant",
                "public Rad toRad",
                "Null Return",
                "public void test13",
                "angle_Rad1"
            }, "TOGLL generated assertions detecting unique mutants"));
    }

    private static void VerifyTogllFigureSourceCode(int pageNum, string[] expectedDiagramTexts, string captionText)
    {
        var page = Diagnostics("TOGLL_Oracle Generation.pdf", pageNum);
        foreach (var text in expectedDiagramTexts)
        {
            AssertParagraph(page, text, p =>
                p.IsDiagram && p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
        }
        AssertParagraph(page, captionText, p =>
            !p.IsDiagram && !p.IsBypassed && !p.IsTable);
    }
}
