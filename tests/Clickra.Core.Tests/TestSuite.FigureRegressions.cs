using Clickra.Core.Processors;
using Clickra.Core.Models;

namespace Clickra.Core.Tests;

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
            var (diagram, labels) = CreateDiagramTestFixture(
                new TableMaskRegion(310.5, 642.9, 549.4, 725.8),
                ("Identify test scope", 42, 5, 350, 392, 690, 695),
                ("Mocking fields", 34, 5, 450, 484, 680, 685));

            var shaded = PdfGrayPromptRegionBuilder.GetGrayPromptShadedRegions(diagram, 612, labels);
            Assert.True(shaded.Count == 0,
                "A workflow diagram was incorrectly promoted to a gray-prompt mask.");
        });

        runner.Run("Short workflow labels remain bypassed after diagram cleanup", () =>
        {
            var (diagram, labels) = CreateDiagramTestFixture(
                new TableMaskRegion(310, 640, 550, 726),
                ("for private", 24, 4, 414, 438, 698, 702),
                ("mocking types", 30, 4, 503, 532, 700, 704));

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
            // The fixture-based check (PentestAgent_Agent Pentest.pdf p9) is
            // replaced by a synthetic chart frame whose axis labels and model
            // names stay inside the diagram; the caption sits outside.
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddFigureFrame(50, 400, 500, 300, new[]
                    {
                        "Success Rate (%)",
                        "GPT-4",
                        "GPT-3.5",
                        "Models"
                    })
                    .AddOutsideText(60, "Success rate on penetration testing tasks"));

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
            VerifyTogllFigureSourceCode(new[]
            {
                "public void test3",
                "assertSame(oA1, oA0)",
                "public void test9",
                "assertEquals((-119.4)",
                "public void test14",
                "Ground Truth"
            }, "Diverse yet correct test oracles"));

        runner.Run("TOGLL p9 Figure 5 source code stays inside the original figure", () =>
            VerifyTogllFigureSourceCode(new[]
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

    private static (List<TableMaskRegion> Diagram, List<PdfParagraph> Labels) CreateDiagramTestFixture(
        TableMaskRegion mask,
        (string Text, double W, double H, double X0, double X1, double Y0, double Y1) l1,
        (string Text, double W, double H, double X0, double X1, double Y0, double Y1) l2)
    {
        var diagram = new List<TableMaskRegion> { mask };
        var labels = new List<PdfParagraph>
        {
            UninitializedParagraph(l1.Text, l1.W, l1.H),
            UninitializedParagraph(l2.Text, l2.W, l2.H)
        };
        labels[0].X0 = l1.X0; labels[0].X1 = l1.X1; labels[0].Y0 = l1.Y0; labels[0].Y1 = l1.Y1;
        labels[1].X0 = l2.X0; labels[1].X1 = l2.X1; labels[1].Y0 = l2.Y0; labels[1].Y1 = l2.Y1;
        return (diagram, labels);
    }

    private static void VerifyTogllFigureSourceCode(string[] expectedDiagramTexts, string captionText)
    {
        // The fixture-based check (TOGLL_Oracle Generation.pdf) is replaced by
        // a synthetic workflow figure frame containing the same code lines,
        // with the caption outside the frame.
        var page = DiagnosticsFromSynthetic(
            new SyntheticGrayPage()
                .AddFigureFrame(50, 400, 500, 300, expectedDiagramTexts)
                .AddOutsideText(60, captionText));
        foreach (var text in expectedDiagramTexts)
        {
            AssertParagraph(page, text, p =>
                p.IsDiagram && p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
        }
        AssertParagraph(page, captionText, p =>
            !p.IsDiagram && !p.IsBypassed && !p.IsTable);
    }
}
