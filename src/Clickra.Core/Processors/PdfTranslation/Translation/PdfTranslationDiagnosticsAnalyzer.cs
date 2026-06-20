using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clickra.Core.Models;
using UglyToad.PdfPig.Content;

namespace Clickra.Core.Processors
{
    internal sealed class PdfTranslationDiagnosticsDependencies
    {
        public required Func<Page, List<PdfParagraph>> BuildPageParagraphs { get; init; }
        public required Action<List<List<PdfParagraph>>, double[]> ApplyReferencesSectionBypass { get; init; }
        public required Func<List<PdfParagraph>, double, Func<PdfParagraph, bool>?, List<TableMaskRegion>> BuildTableMaskRegions { get; init; }
        public required Func<Page, List<PdfParagraph>, List<TableMaskRegion>> BuildProcessedDiagramMaskRegions { get; init; }
        public required Func<List<TableMaskRegion>, List<TableMaskRegion>, List<PdfParagraph>, List<TableMaskRegion>> GetEffectiveDiagramMaskRegions { get; init; }
        public required Func<List<PdfParagraph>, List<TableMaskRegion>, double, List<TableMaskRegion>> GetFigureClipRegions { get; init; }
        public required Func<IReadOnlyList<TableMaskRegion>, double, IReadOnlyList<PdfParagraph>?, List<TableMaskRegion>> GetGrayPromptShadedRegions { get; init; }
        public required Func<Page, IReadOnlyList<TableMaskRegion>, IReadOnlyList<PdfParagraph>, double, List<TableMaskRegion>> BuildEffectiveGrayMaskRegions { get; init; }
        public required Func<PdfParagraph, bool> IsTranslatableBodyProse { get; init; }
        public required Func<PdfParagraph, bool> IsTranslatableCalloutProse { get; init; }
        public required Func<PdfParagraph, bool> IsHeadingParagraph { get; init; }
        public required Func<double, double, double, double, IReadOnlyList<TableMaskRegion>, bool> ParagraphOverlapsAnyTableMask { get; init; }
        public required Func<PdfParagraph, IReadOnlyList<TableMaskRegion>, List<PdfParagraph>, double, bool> ShouldProtectDiagramRegionFromParagraph { get; init; }
    }

    internal static class PdfTranslationDiagnosticsAnalyzer
    {
        public static TranslationPageDiagnostics AnalyzePageParagraphDiagnostics(
            string inputPath,
            int pageNum,
            PdfTranslationDiagnosticsDependencies dependencies)
        {
            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
            int totalPages = pigDoc.NumberOfPages;
            if (pageNum < 1 || pageNum > totalPages)
                throw new ArgumentOutOfRangeException(nameof(pageNum), $"Page must be between 1 and {totalPages}.");

            var allPages = new List<List<PdfParagraph>>();
            var pageWidths = new double[totalPages];
            for (int p = 1; p <= totalPages; p++)
            {
                var pg = pigDoc.GetPage(p);
                pageWidths[p - 1] = pg.Width;
                allPages.Add(dependencies.BuildPageParagraphs(pg));
            }

            dependencies.ApplyReferencesSectionBypass(allPages, pageWidths);
            var page = pigDoc.GetPage(pageNum);
            var pageList = allPages[pageNum - 1];
            double center = page.Width / 2.0;
            var tableParas = pageList.Where(p => p.IsTable).ToList();
            Func<PdfParagraph, bool>? excludeAuthorFromTableMask = null;
            if (pageNum == 1 &&
                PageOneLayoutClassifier.TryGetAuthorBand(pageList, page.Height, out double titleBottom, out double abstractTop, out var titlePara) &&
                titlePara != null)
            {
                excludeAuthorFromTableMask = para =>
                    PageOneLayoutClassifier.IsInAuthorBand(para, titleBottom, abstractTop, titlePara);
            }

            var tableMaskRegions = dependencies.BuildTableMaskRegions(tableParas, page.Width, excludeAuthorFromTableMask);
            var rawDiagramMaskRegions = dependencies.BuildProcessedDiagramMaskRegions(page, pageList);
            var diagramMaskRegions = dependencies.GetEffectiveDiagramMaskRegions(
                rawDiagramMaskRegions, tableMaskRegions, pageList);
            var figureClipRegions = dependencies.GetFigureClipRegions(pageList, diagramMaskRegions, page.Width);
            var grayShadedRegions = dependencies.GetGrayPromptShadedRegions(diagramMaskRegions, page.Width, pageList);
            var effectiveGrayRegions = dependencies.BuildEffectiveGrayMaskRegions(
                page, diagramMaskRegions, pageList, page.Width);

            var paragraphs = new List<TranslationParagraphDiagnostics>();
            int idx = 0;
            foreach (var para in pageList.OrderByDescending(p => p.Y1))
            {
                string txt = para.TextWithPlaceholders.Trim();
                int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool hasPeriod = txt.IndexOf('.') >= 0;
                bool isBodyProse = dependencies.IsTranslatableBodyProse(para);
                bool isCalloutProse = dependencies.IsTranslatableCalloutProse(para);
                bool isHeading = dependencies.IsHeadingParagraph(para);
                bool wouldSkipRender = (tableMaskRegions.Count > 0 &&
                    dependencies.ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions)) ||
                    dependencies.ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, pageList, page.Width);

                paragraphs.Add(new TranslationParagraphDiagnostics
                {
                    Index = idx++,
                    Column = (para.X0 + para.Width / 2) < center ? "L" : "R",
                    Text = para.TextWithPlaceholders,
                    X0 = para.X0,
                    Y0 = para.Y0,
                    X1 = para.X1,
                    Y1 = para.Y1,
                    AverageFontSize = para.AverageFontSize,
                    IsBypassed = para.IsBypassed,
                    IsTable = para.IsTable,
                    IsCode = para.IsCode,
                    IsDiagram = para.IsDiagram,
                    IsGrayPromptContent = para.IsGrayPromptContent,
                    WouldSkipRender = wouldSkipRender,
                    IsBodyProse = isBodyProse,
                    IsCalloutProse = isCalloutProse,
                    IsHeading = isHeading,
                    WordCount = wordCount,
                    HasPeriod = hasPeriod
                });
            }

            return new TranslationPageDiagnostics
            {
                SourcePath = inputPath,
                PageNumber = pageNum,
                PageWidth = page.Width,
                PageHeight = page.Height,
                TableCount = tableParas.Count,
                TableMaskRegions = tableMaskRegions.Select(ToDiagnosticsRegion).ToList(),
                DiagramMaskRegions = diagramMaskRegions.Select(ToDiagnosticsRegion).ToList(),
                FigureClipRegions = figureClipRegions.Select(ToDiagnosticsRegion).ToList(),
                GrayPromptShadedRegions = grayShadedRegions.Select(ToDiagnosticsRegion).ToList(),
                EffectiveGrayMaskRegions = effectiveGrayRegions.Select(ToDiagnosticsRegion).ToList(),
                Paragraphs = paragraphs
            };
        }

        public static string DumpPageParagraphDiagnostics(TranslationPageDiagnostics diagnostics)
        {
            var sb = new StringBuilder();
            if (diagnostics.TableMaskRegions.Count > 0)
            {
                sb.AppendLine($"TableMaskRegions: count={diagnostics.TableMaskRegions.Count} tableCount={diagnostics.TableCount}");
                for (int ri = 0; ri < diagnostics.TableMaskRegions.Count; ri++)
                {
                    var r = diagnostics.TableMaskRegions[ri];
                    sb.AppendLine($"  [{ri}] X=[{r.X0:F1},{r.X1:F1}] Y=[{r.Y0:F1},{r.Y1:F1}]");
                }
            }
            if (diagnostics.DiagramMaskRegions.Count > 0)
            {
                sb.AppendLine($"DiagramMaskRegions: count={diagnostics.DiagramMaskRegions.Count}");
                for (int ri = 0; ri < diagnostics.DiagramMaskRegions.Count; ri++)
                {
                    var r = diagnostics.DiagramMaskRegions[ri];
                    sb.AppendLine($"  [{ri}] X=[{r.X0:F1},{r.X1:F1}] Y=[{r.Y0:F1},{r.Y1:F1}]");
                }
            }
            if (diagnostics.FigureClipRegions.Count > 0)
            {
                sb.AppendLine($"FigureClipRegions: count={diagnostics.FigureClipRegions.Count}");
                for (int ri = 0; ri < diagnostics.FigureClipRegions.Count; ri++)
                {
                    var r = diagnostics.FigureClipRegions[ri];
                    sb.AppendLine($"  [{ri}] X=[{r.X0:F1},{r.X1:F1}] Y=[{r.Y0:F1},{r.Y1:F1}]");
                }
            }
            if (diagnostics.GrayPromptShadedRegions.Count > 0)
            {
                sb.AppendLine($"GrayPromptShadedRegions: count={diagnostics.GrayPromptShadedRegions.Count}");
                for (int ri = 0; ri < diagnostics.GrayPromptShadedRegions.Count; ri++)
                {
                    var r = diagnostics.GrayPromptShadedRegions[ri];
                    sb.AppendLine($"  [{ri}] X=[{r.X0:F1},{r.X1:F1}] Y=[{r.Y0:F1},{r.Y1:F1}]");
                }
            }
            if (diagnostics.EffectiveGrayMaskRegions.Count > 0)
            {
                sb.AppendLine($"EffectiveGrayMaskRegions: count={diagnostics.EffectiveGrayMaskRegions.Count}");
                for (int ri = 0; ri < diagnostics.EffectiveGrayMaskRegions.Count; ri++)
                {
                    var r = diagnostics.EffectiveGrayMaskRegions[ri];
                    sb.AppendLine($"  [{ri}] X=[{r.X0:F1},{r.X1:F1}] Y=[{r.Y0:F1},{r.Y1:F1}]");
                }
            }
            foreach (var para in diagnostics.Paragraphs)
            {
                string preview = para.Text.Length > 90
                    ? para.Text.Substring(0, 90) + "..."
                    : para.Text;
                preview = preview.Replace("\n", " ");
                sb.AppendLine($"[{para.Index}] {para.Column} [{para.X0:F0},{para.Y0:F0},{para.X1:F0},{para.Y1:F0}] bypass={para.IsBypassed} table={para.IsTable} code={para.IsCode} diagram={para.IsDiagram} grayPrompt={para.IsGrayPromptContent} skipRender={para.WouldSkipRender}");
                sb.AppendLine($"    isBodyProse={para.IsBodyProse} isCallout={para.IsCalloutProse} isHeading={para.IsHeading} wordCount={para.WordCount} height={para.Height:F1} width={para.Width:F1} hasPeriod={para.HasPeriod}");
                sb.AppendLine($"    {preview}");
            }
            return sb.ToString();
        }

        private static TranslationRegionDiagnostics ToDiagnosticsRegion(TableMaskRegion region) => new()
        {
            X0 = region.X0,
            Y0 = region.Y0,
            X1 = region.X1,
            Y1 = region.Y1
        };
    }
}
