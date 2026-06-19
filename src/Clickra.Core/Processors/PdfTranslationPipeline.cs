using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Drawing;
#pragma warning disable CA1416 // Validate platform compatibility
using System.Drawing;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class PdfTranslationPipeline
    {
        /// <summary>Structured paragraph flags after the translation layout pipeline for one page.</summary>
        public static TranslationPageDiagnostics AnalyzePageParagraphDiagnostics(string inputPath, int pageNum)
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
                allPages.Add(BuildPageParagraphs(pg));
            }

            ApplyReferencesSectionBypass(allPages, pageWidths);
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

            var tableMaskRegions = BuildTableMaskRegions(tableParas, page.Width, excludeAuthorFromTableMask);
            var rawDiagramMaskRegions = BuildProcessedDiagramMaskRegions(page, pageList);
            var diagramMaskRegions = GetEffectiveDiagramMaskRegions(
                rawDiagramMaskRegions, tableMaskRegions, pageList);
            var figureClipRegions = GetFigureClipRegions(pageList, diagramMaskRegions, page.Width);
            var grayShadedRegions = GetGrayPromptShadedRegions(diagramMaskRegions, page.Width, pageList);
            var effectiveGrayRegions = BuildEffectiveGrayMaskRegions(
                page, diagramMaskRegions, pageList, page.Width);

            var paragraphs = new List<TranslationParagraphDiagnostics>();
            int idx = 0;
            foreach (var para in pageList.OrderByDescending(p => p.Y1))
            {
                string txt = para.TextWithPlaceholders.Trim();
                int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool hasPeriod = txt.IndexOf('.') >= 0;
                bool isBodyProse = IsTranslatableBodyProse(para);
                bool isCalloutProse = IsTranslatableCalloutProse(para);
                bool isHeading = IsHeadingParagraph(para);
                bool wouldSkipRender = (tableMaskRegions.Count > 0 &&
                    ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions)) ||
                    ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, pageList, page.Width);

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

        /// <summary>Debug helper: dump paragraph flags after full layout pipeline for one page.</summary>
        public static string DumpPageParagraphDiagnostics(string inputPath, int pageNum)
        {
            var diagnostics = AnalyzePageParagraphDiagnostics(inputPath, pageNum);
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

        private static List<PdfParagraph> BuildPageParagraphs(UglyToad.PdfPig.Content.Page page)
        {
            var pageList = new List<PdfParagraph>();

                var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();
                if (words.Count == 0)
                {
                    return pageList;
                }

                var segmenter = new DocstrumBoundingBoxes();
                bool isTablePage = words.Any(w => IsTableCaptionWord(w, words));
                var blocks = PdfParagraphBlockMerger.GetMergedBlocks(segmenter.GetBlocks(words), page.Width, isTablePage);
                foreach (var block in blocks)
                {
                    var blockLines = PdfParagraph.MergeHorizontalLines(block.TextLines);
                    var currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
                    bool? currentIsMath = null;

                    double minX = block.TextLines.Count > 0 ? block.TextLines.Min(l => l.BoundingBox.Left) : 0;
                    double maxX = block.TextLines.Count > 0 ? block.TextLines.Max(l => l.BoundingBox.Right) : 0;
                    double blockWidth = maxX - minX;

                    bool isTableBlock = blockLines.Count >= 2 &&
                                        (isTablePage || blockWidth < 150.0) &&
                                        (blockLines.Average(l => l.Words.Count) <= 3.5 ||
                                         (isTablePage && blockWidth < page.Width * 0.45 &&
                                          blockLines.Max(l => l.Words.Count) <= 8));

                    foreach (var line in blockLines)
                    {
                        bool isMath = PdfParagraph.IsMathLine(line);
                        bool startsNew = PdfParagraphBlockMerger.StartsNewParagraphOrSection(line.Text);

                        bool prevLineEndedEarly = false;
                        bool prevLineWasHeading = false;
                        bool isVerticalGapLarge = false;
                        if (currentGroup.Count > 0)
                        {
                            var prevLine = currentGroup[currentGroup.Count - 1];
                            if (prevLine.BoundingBox.Right < block.Right - 20.0)
                            {
                                prevLineEndedEarly = true;
                            }
                            if (PdfParagraphBlockMerger.IsHeadingLine(prevLine))
                            {
                                prevLineWasHeading = true;
                            }

                            // Prevent DocStrum from mistakenly merging paragraphs across a large vertical gap (e.g. over 15 pt)
                            double gapY = prevLine.BoundingBox.Bottom - line.BoundingBox.Top;
                            if (gapY > 15.0)
                            {
                                isVerticalGapLarge = true;
                            }
                        }

                        bool prevLineHasGap = isTablePage && currentGroup.Count > 0 && HasColumnGap(currentGroup[currentGroup.Count - 1]);
                        bool currLineHasGap = isTablePage && HasColumnGap(line);
                        bool crossColumnSplit = currentGroup.Count > 0 &&
                            IsLineInLeftColumn(currentGroup[currentGroup.Count - 1], page.Width) !=
                            IsLineInLeftColumn(line, page.Width);
                        bool forceSplit = isTableBlock && currentGroup.Count > 0;

                        // When the previous line is a heading, don't split on prevLineEndedEarly
                        // (headings naturally end early; e.g., '2.1 Text Representation and Modality' + 'Alignment')
                        bool shouldSplit = startsNew || isVerticalGapLarge || crossColumnSplit ||
                            (prevLineEndedEarly && !prevLineWasHeading) || (prevLineWasHeading && !FontUtilities.IsLineBold(line)) ||
                            prevLineHasGap || currLineHasGap || forceSplit;

                        if (currentGroup.Count == 0)
                        {
                            currentGroup.Add(line);
                            currentIsMath = isMath;
                        }
                        else if (isMath == currentIsMath && !shouldSplit)
                        {
                            currentGroup.Add(line);
                        }
                        else
                        {
                            var paragraph = new PdfParagraph(currentGroup);
                            pageList.Add(paragraph);

                            currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line };
                            currentIsMath = isMath;
                        }
                    }

                    if (currentGroup.Count > 0)
                    {
                        var paragraph = new PdfParagraph(currentGroup);
                        pageList.Add(paragraph);
                    }
                }

                // Pass 0: Sanitize TextWithPlaceholders — remove stray '):(...)' bracket artifacts
                // These appear when AnalyzeLines absorbs the opening '(' of a parenthetical phrase
                // into a formula token, leaving a dangling '):(label)' in the text.
                // e.g. "{v0}):(Equation (1))" -> "{v0}"   or   "InfoNCE):(Equation (1))" -> "InfoNCE"
                foreach (var para in pageList)
                {
                    if (string.IsNullOrWhiteSpace(para.TextWithPlaceholders)) continue;
                    string twp = para.TextWithPlaceholders.Trim();
                    // Find first occurrence of "):(" — a stray closing paren followed by colon+open
                    int artifactIdx = twp.IndexOf("):(", System.StringComparison.Ordinal);
                    if (artifactIdx > 0)
                    {
                        para.TextWithPlaceholders = twp.Substring(0, artifactIdx).TrimEnd();
                    }
                }

                if (page.Number == 1)
                    PageOneLayoutClassifier.MergeTitleWithSubtitle(pageList, page.Height);

                // Pass 0.5: Mark table paragraphs geometrically
                MarkTableParagraphs(pageList, page.Width, page.Height, isTablePage);

                // Pass 0.55: Clear false-positive table marks on body paragraphs
                foreach (var para in pageList)
                {
                    if (!para.IsTable) continue;
                    string txt = para.TextWithPlaceholders.Trim();
                    int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    if (para.Height > 35 && wordCount > 20)
                    {
                        para.IsTable = false;
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\s"))
                    {
                        para.IsTable = false;
                    }
                    else if (para.Width > page.Width * 0.38 && wordCount > 10)
                    {
                        // Full-column prose on table pages (e.g. RQ4 intro) is not a table cell.
                        para.IsTable = false;
                    }
                    else if (txt.StartsWith("•") || txt.StartsWith("·") ||
                             txt.StartsWith("To sum up", StringComparison.OrdinalIgnoreCase))
                    {
                        // Contribution bullets/headings near comparison tables (e.g. PentestAgent p2).
                        para.IsTable = false;
                    }
                    else if (txt.StartsWith("and ", StringComparison.OrdinalIgnoreCase) && wordCount > 3 && para.Height <= 20)
                    {
                        // Table footnote continuation lines.
                        para.IsTable = false;
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\s+[A-Za-z]") &&
                             para.Height <= 25 && para.Width > 120)
                    {
                        // Numbered footnote body (e.g. "1 AutoAttacker and PentestGPT solely rely...").
                        para.IsTable = false;
                    }
                }

                // Pass 1: Mark initial bypassed paragraphs (short figure labels only)
                foreach (var para in pageList)
                {
                    if (IsGrayPromptBoxParagraph(para) || IsGrayPromptSubheading(para))
                    {
                        MarkAsGrayPromptContent(para);
                        continue;
                    }
                    if (para.IsCode) continue;
                    if (!para.IsTable && OverlapsWithLargeImage(para, page))
                    {
                        if (IsHeadingParagraph(para) || IsTranslatableBodyProse(para) ||
                            IsTranslatableCalloutProse(para) || IsFigureTableCaptionParagraph(para))
                        {
                            continue;
                        }
                        if (IsLikelyChartLabel(para) || para.TextWithPlaceholders.Trim().Length <= 80)
                        {
                            para.IsDiagram = true;
                        }
                    }
                }

                var diagramRegions = BuildProcessedDiagramMaskRegions(page, pageList);
                ClearDiagramFlagOnRunningHeaders(pageList, page.Height);

                // Table grid strokes overlap cell text and falsely mark it as diagram; keep as table for redraw.
                PdfTableClassifier.ReclassifyWorkDivisionTableText(pageList, page.Width);
                PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);
                PdfTableClassifier.ReclassifyTableMisclassifiedProse(pageList, page.Width);
                PdfTableClassifier.MarkCompactAcademicTableBodies(
                    pageList,
                    page.Width,
                    IsFigureTableCaptionParagraph,
                    IsHeadingParagraph,
                    IsAppendixSectionHeading);
                PdfTableClassifier.MarkSplitPromptPerformanceTable(pageList, IsFigureTableCaptionParagraph);
                var tableMaskForDiagram = BuildTableMaskRegions(
                    pageList.Where(p => p.IsTable).ToList(), page.Width);
                var effectiveDiagramRegions = GetEffectiveDiagramMaskRegions(
                    diagramRegions, tableMaskForDiagram, pageList);
                MarkDiagramFigureLabels(pageList, page, effectiveDiagramRegions);
                ReclassifyChartLabelsMisclassifiedAsTable(pageList, effectiveDiagramRegions);
                ReclassifyStandaloneChartLabelsAsDiagram(pageList);
                FinalizeDiagramFigureLabels(pageList, effectiveDiagramRegions, page.Height);
                MarkWorkflowFigureLabelsAboveCaption(pageList, page.Height);
                MarkCodeFigureContentAboveCaption(pageList, page.Width, page.Height);
                ClearDiagramFlagOnFigureCaptions(pageList);
                ClearDiagramFlagOnSectionHeadings(pageList);
                ReclassifyCalloutFindingsText(pageList);
                PdfTableClassifier.ReclassifyWorkDivisionTableText(pageList, page.Width);
                PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);

                foreach (var para in pageList)
                {
                    para.IsBypassed = para.IsBypassed || para.IsCode || para.IsOnlyMath || string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                                      IsEquationParagraph(para) || IsTableParagraph(para) || para.IsDiagram || para.IsTable;
                }



                // Pass 2: Propagate bypass to nearby small/label paragraphs (e.g. annotations inside drawings)
                bool pageHasDiagramLabels = pageList.Any(p => p.IsDiagram);
                int diagramLabelMaxLen = pageHasDiagramLabels ? 80 : 20;
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var para in pageList)
                    {
                        if (para.IsBypassed) continue;
                        if (para.IsTable) continue;
                        if (page.Number == 1 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, page.Height)) continue;
                        if (para.IsCode) continue;
                        if (IsRunningHeaderOrFooter(para, page.Height)) continue;
                        if (IsFigureTableCaptionParagraph(para)) continue;
                        if (IsTranslatableBodyProse(para)) continue;
                        if (IsTranslatableCalloutProse(para)) continue;

                        bool isSmallLabel = para.TextWithPlaceholders.Length <= diagramLabelMaxLen &&
                                            !IsHeadingParagraph(para) && IsLikelyChartLabel(para);
                        if (isSmallLabel)
                        {
                            foreach (var other in pageList)
                            {
                                if (other == para || !other.IsBypassed) continue;
                                if (other.IsTable && !other.IsDiagram) continue;
                                if (IsRunningHeaderOrFooter(other, page.Height)) continue;

                                bool closeX = (para.X0 <= other.X1 + 30) && (para.X1 >= other.X0 - 30);
                                bool closeY = (para.Y0 <= other.Y1 + 30) && (para.Y1 >= other.Y0 - 30);

                                if (closeX && closeY)
                                {
                                    para.IsBypassed = true;
                                    if (other.IsDiagram)
                                    {
                                        para.IsDiagram = true;
                                        para.IsTable = false;
                                    }
                                    changed = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                ClearDiagramFlagOnRunningHeaders(pageList, page.Height);
                ClearDiagramFlagOnTranslatableProse(pageList, effectiveDiagramRegions);
                MarkWorkflowFigureLabelsAboveCaption(pageList, page.Height);
                MarkCodeFigureContentAboveCaption(pageList, page.Width, page.Height);
                ClearDiagramFlagOnFigureCaptions(pageList);
                ClearDiagramFlagOnSectionHeadings(pageList);
                MarkWorkflowBannerTextInDiagramRegions(pageList, effectiveDiagramRegions, page.Height);
                bool workDivisionPage = pageList.Any(p =>
                    p.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase));
                var grayPromptShadedRegions = workDivisionPage
                    ? new List<TableMaskRegion>()
                    : GetGrayPromptShadedRegions(effectiveDiagramRegions, page.Width, pageList);
                var effectiveGrayRegions = workDivisionPage
                    ? new List<TableMaskRegion>()
                    : BuildEffectiveGrayMaskRegions(
                        page, effectiveDiagramRegions, pageList, page.Width);
                if (!workDivisionPage)
                {
                    // Geometry-first: mark by vector gray boxes before any heuristic clearing.
                    MarkAllParagraphsByGrayGeometry(pageList, effectiveGrayRegions, page.Height);
                    MarkGrayPromptBoxesAsCode(pageList, grayPromptShadedRegions);
                    MarkGrayPromptContentInShadedRegions(pageList, grayPromptShadedRegions);
                }
                ClearMisclassifiedCodeFlags(pageList);

                MergeVerticallyAdjacentParagraphs(pageList);
                PdfTableClassifier.ReclassifyWorkDivisionTableText(pageList, page.Width);
                PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);
                PdfTableClassifier.MarkCompactAcademicTableBodies(
                    pageList,
                    page.Width,
                    IsFigureTableCaptionParagraph,
                    IsHeadingParagraph,
                    IsAppendixSectionHeading);
                PdfTableClassifier.MarkSplitPromptPerformanceTable(pageList, IsFigureTableCaptionParagraph);
                if (!workDivisionPage)
                {
                    MarkAllParagraphsByGrayGeometry(pageList, effectiveGrayRegions, page.Height);
                    ClearGrayPromptContentOutsideShadedRegions(pageList, effectiveGrayRegions);
                    ClearTranslatableProseFromGrayPromptFlags(pageList, effectiveGrayRegions);
                    ClearGrayPromptFlagsBelowShadedBottom(pageList, effectiveGrayRegions, page.Width);
                    RestoreGrayPromptContinuations(pageList);
                    MarkAllParagraphsByGrayGeometry(pageList, effectiveGrayRegions, page.Height);
                    FinalizeGrayPromptContentFlags(pageList);
                }
                PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);
                MarkWorkflowFigureLabelsAboveCaption(pageList, page.Height);
                MarkCodeFigureContentAboveCaption(pageList, page.Width, page.Height);
                if (!workDivisionPage)
                {
                    MarkAllParagraphsByGrayGeometry(pageList, grayPromptShadedRegions, page.Height);
                    RestoreGrayPromptContinuations(pageList);
                    FinalizeGrayPromptContentFlags(pageList);
                }

                if (page.Number == 1)
                    PageOneLayoutClassifier.ApplyAuthorBlockFlags(pageList, page.Height);

                foreach (var para in pageList)
                {
                    string finalText = para.TextWithPlaceholders.Trim();
                    if (finalText.Equals("EX-", StringComparison.OrdinalIgnoreCase) ||
                        System.Text.RegularExpressions.Regex.IsMatch(
                            finalText, @"^AMPLE\}?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                        finalText.Contains("OUTPUT FORMAT", StringComparison.OrdinalIgnoreCase))
                    {
                        MarkAsGrayPromptContent(para);
                    }

                    if (page.Number == 1 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, page.Height))
                    {
                        para.IsBypassed = true;
                        continue;
                    }
                    // Preserve IsBypassed=true set by proximity propagation (Pass 2 above);
                    // only recalculate when it is currently false.
                    para.IsBypassed = para.IsBypassed ||
                                      para.IsCode || para.IsOnlyMath || string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                                      IsEquationParagraph(para) || IsTableParagraph(para) || para.IsDiagram || para.IsTable ||
                                      IsChartTickGlyph(para);
                }

                return pageList;
        }

        public static void TranslatePdf(string inputPath, string outputPath, string targetLang, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch { }
            onProgress?.Invoke(10, 100, "正在分析 PDF 版面結構與公式...");

            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
            int totalPages = pigDoc.NumberOfPages;
            var pageParagraphs = new List<List<PdfParagraph>>();

            var pageWidths = new double[totalPages];
            for (int p = 1; p <= totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pigDoc.GetPage(p);
                pageWidths[p - 1] = page.Width;
                pageParagraphs.Add(BuildPageParagraphs(page));
            }

            ApplyReferencesSectionBypass(pageParagraphs, pageWidths);

            onProgress?.Invoke(30, 100, "正在翻譯文本內容...");
            var translator = TranslationEngineFactory.Create();
            object logLock = new object();

            for (int p = 0; p < totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paragraphs = pageParagraphs[p];
                if (paragraphs.Count == 0) continue;

                onProgress?.Invoke(30 + (int)(p * 40.0 / totalPages), 100, $"正在翻譯第 {p + 1}/{totalPages} 頁...");

                var paragraphsToTranslate = new List<PdfParagraph>();
                var textsToTranslate = new List<string>();

                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed)
                    {
                        para.TranslatedText = para.TextWithPlaceholders;
                    }
                    else
                    {
                        paragraphsToTranslate.Add(para);
                        textsToTranslate.Add(para.TextWithPlaceholders);
                    }
                }

                if (textsToTranslate.Count > 0)
                {
                    try
                    {
                        var results = translator.TranslateBatchAsync(textsToTranslate, targetLang, cancellationToken).GetAwaiter().GetResult();
                        if (results.Count == paragraphsToTranslate.Count)
                        {
                            for (int i = 0; i < results.Count; i++)
                            {
                                string rawResult = string.IsNullOrWhiteSpace(results[i]) 
                                    ? paragraphsToTranslate[i].TextWithPlaceholders 
                                    : results[i];
                                paragraphsToTranslate[i].TranslatedText = PostProcessor.Process(
                                    paragraphsToTranslate[i].TextWithPlaceholders,
                                    rawResult,
                                    targetLang
                                );
                            }
                        }
                        else
                        {
                            throw new Exception("Mismatched batch translation results count.");
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
                            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {p + 1}] Batch translation failed, falling back to sequential. Error: {ex.Message}{Environment.NewLine}";
                            lock (logLock)
                            {
                                File.AppendAllText(logPath, logLine);
                            }
                        }
                        catch { }

                        foreach (var para in paragraphsToTranslate)
                        {
                            try
                            {
                                string result = translator.TranslateAsync(para.TextWithPlaceholders, targetLang, cancellationToken).GetAwaiter().GetResult();
                                string rawResult = string.IsNullOrWhiteSpace(result) ? para.TextWithPlaceholders : result;
                                para.TranslatedText = PostProcessor.Process(
                                    para.TextWithPlaceholders,
                                    rawResult,
                                    targetLang
                                );
                            }
                            catch (Exception exSub)
                            {
                                para.TranslatedText = para.TextWithPlaceholders;
                                try
                                {
                                    string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
                                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {p + 1}] Sequential fallback error: {exSub.Message}{Environment.NewLine}";
                                    lock (logLock)
                                    {
                                        File.AppendAllText(logPath, logLine);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }

            onProgress?.Invoke(80, 100, "正在重建 PDF 佈局與公式...");
            cancellationToken.ThrowIfCancellationRequested();


            using var finalDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

            string targetFontName = "DFKai-SB";
            if (targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "DFKai-SB";
            }
            else if (targetLang.Equals("ja", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "DFKai-SB";
            }
            else if (targetLang.Equals("ko", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "Malgun Gothic";
            }
            else if (targetLang.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "Arial";
            }

            for (int p = 0; p < totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = finalDoc.Pages[p];

                var paragraphs = pageParagraphs[p];
                if (paragraphs.Count == 0) continue;

                // Map annotations to paragraphs
                try
                {
                    for (int i = 0; i < page.Annotations.Count; i++)
                    {
                        var annot = page.Annotations[i];
                        var rect = annot.Rectangle;
                        
                        var paraOverlaps = new Dictionary<PdfParagraph, List<PdfLetter>>();
                        foreach (var para in paragraphs)
                        {
                            var overlapping = para.AllLetters
                                .Where(l => l.Right >= rect.X1 - 2.5 && l.Left <= rect.X2 + 2.5 &&
                                            l.Top >= rect.Y1 - 2.5 && l.Bottom <= rect.Y2 + 2.5)
                                .OrderBy(l => para.AllLetters.IndexOf(l))
                                .ToList();
                            if (overlapping.Count > 0)
                            {
                                paraOverlaps[para] = overlapping;
                            }
                        }
                        
                        if (paraOverlaps.Count > 0)
                        {
                            double annotCenterX = (rect.X1 + rect.X2) / 2.0;
                            double annotCenterY = (rect.Y1 + rect.Y2) / 2.0;
                            var bestPair = paraOverlaps
                                .OrderByDescending(kv => ScoreAnnotationParagraph(kv.Key, kv.Value, annotCenterX, annotCenterY))
                                .First();
                            var bestPara = bestPair.Key;
                            var overlappingLetters = bestPair.Value;
                            
                            string searchText = string.Join("", overlappingLetters.Select(l => l.Value)).Trim();
                            searchText = NormalizeAnnotationSearchText(searchText);
                            if (!string.IsNullOrEmpty(searchText))
                            {
                                int occurrenceIdx = GetOccurrenceIndex(bestPara.AllLetters, overlappingLetters, searchText);
                                int firstLetterIdx = bestPara.AllLetters.IndexOf(overlappingLetters[0]);
                                int lastLetterIdx = bestPara.AllLetters.IndexOf(overlappingLetters[^1]);
                                double relCenterX = bestPara.Width > 0 ? (annotCenterX - bestPara.X0) / bestPara.Width : 0.5;
                                double relCenterY = bestPara.Height > 0 ? (annotCenterY - bestPara.Y0) / bestPara.Height : 0.5;
                                double relWidth = bestPara.Width > 0 ? (rect.X2 - rect.X1) / bestPara.Width : 0.05;
                                bestPara.Annotations.Add(new ParagraphAnnotationInfo
                                {
                                    PdfAnnotation = annot,
                                    Text = searchText,
                                    OccurrenceIndex = occurrenceIdx,
                                    FirstLetterIndex = firstLetterIdx,
                                    LastLetterIndex = lastLetterIdx,
                                    TotalLetterCount = bestPara.AllLetters.Count,
                                    RelCenterX = relCenterX,
                                    RelCenterY = relCenterY,
                                    RelWidth = relWidth
                                });
                            }
                        }
                    }
                }
                catch { }

                // Table pages skip white masks over table regions but still strip text streams;
                // bypassed table cells are redrawn in Pass 2 after stripping.
                bool pageHasTable = paragraphs.Any(para => para.IsTable);
                var pigPage = pigDoc.GetPage(p + 1);
                double pageWidthPts = pigPage.Width;
                double pageHeightPts = pigPage.Height;
                var rawDiagramMaskRegions = BuildProcessedDiagramMaskRegions(pigPage, paragraphs);

                Func<PdfParagraph, bool>? excludeAuthorFromTableMask = null;
                if (p == 0 &&
                    PageOneLayoutClassifier.TryGetAuthorBand(paragraphs, pageHeightPts, out double authorTitleBottom, out double authorAbstractTop, out var authorTitlePara) &&
                    authorTitlePara != null)
                {
                    excludeAuthorFromTableMask = para =>
                        PageOneLayoutClassifier.IsInAuthorBand(para, authorTitleBottom, authorAbstractTop, authorTitlePara);
                }
                var tableMaskRegions = (pageHasTable && p != 0)
                    ? BuildTableMaskRegions(paragraphs.Where(para => para.IsTable).ToList(), pageWidthPts, excludeAuthorFromTableMask)
                    : new List<TableMaskRegion>();
                var diagramMaskRegions = GetEffectiveDiagramMaskRegions(
                    rawDiagramMaskRegions, tableMaskRegions, paragraphs);
                bool workDivisionPage = paragraphs.Any(para =>
                    para.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase));
                var effectiveGrayMaskRegions = workDivisionPage
                    ? new List<TableMaskRegion>()
                    : BuildEffectiveGrayMaskRegions(pigPage, diagramMaskRegions, paragraphs, pageWidthPts);

                HashSet<string> strippedBaseFonts;
                try
                {
                    var translatableFonts = PdfFontStripper.CollectTranslatableFontBaseNames(paragraphs);
                    var mustStripFonts = PdfFontStripper.CollectTranslatableFontBaseNames(paragraphs.Where(para =>
                        !para.IsBypassed && !para.IsGrayPromptContent && !IsGrayPromptCodeParagraph(para)));
                    var protectedOnlyFonts = PdfFontStripper.CollectFontsUsedOnlyInProtectedRegions(
                        paragraphs,
                        effectiveGrayMaskRegions,
                        p,
                        pageHeightPts,
                        new ProtectedNoStripPredicates
                        {
                            IsGrayPromptCodeParagraph = IsGrayPromptCodeParagraph,
                            ParagraphCenterInsideAnyRegion = ParagraphCenterInsideAnyRegion,
                            IsParagraphInsideGrayShadedRegion = IsParagraphInsideGrayShadedRegion,
                            IsLikelyChartLabel = IsLikelyChartLabel
                        });
                    protectedOnlyFonts.ExceptWith(mustStripFonts);
                    translatableFonts.ExceptWith(protectedOnlyFonts);
                    strippedBaseFonts = PdfFontStripper.StripTextFromPage(page, translatableFonts);
                }
                catch
                {
                    strippedBaseFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                // Ensure the page has /ExtGState with /NormalState to reset overprint and multiply blend modes
                try
                {
                    var extGStatesProp = typeof(PdfResources).GetProperty("ExtGStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (extGStatesProp != null)
                    {
                        var extGStates = extGStatesProp.GetValue(page.Resources) as PdfDictionary;
                        if (extGStates != null && !extGStates.Elements.ContainsKey("/NormalState"))
                        {
                            var normalState = new PdfDictionary();
                            normalState.Elements["/BM"] = new PdfName("/Normal");
                            normalState.Elements["/op"] = new PdfBoolean(false);
                            normalState.Elements["/OP"] = new PdfBoolean(false);
                            extGStates.Elements["/NormalState"] = normalState;
                        }
                    }
                }
                catch { }

                using var gfx = XGraphics.FromPdfPage(page);
                try
                {
                    gfx.Internals.ContentStringBuilder.Append(" /NormalState gs ");
                }
                catch { }

                // Pass 1: Draw white masks ONLY for translated paragraphs
                var pageOneTitlePara = p == 0 ? PageOneLayoutClassifier.FindTitleParagraph(paragraphs, pageHeightPts) : null;
                double pageOneTitleBottom = 0, pageOneAbstractTop = 0;
                bool hasPageOneAuthorBand = p == 0 &&
                    PageOneLayoutClassifier.TryGetAuthorBand(paragraphs, pageHeightPts, out pageOneTitleBottom, out pageOneAbstractTop, out _);

                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed) continue;
                    if (p == 0 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, paragraphs, pageHeightPts)) continue;
                    if (para.IsGrayPromptContent) continue;
                    if (IsGrayPromptCodeParagraph(para)) continue;
                    if ((para.IsGrayPromptContent || IsGrayPromptCodeParagraph(para)) &&
                        effectiveGrayMaskRegions.Count > 0 &&
                        ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions))
                    {
                        continue;
                    }
                    if (para.IsTable) continue; // Skip table cells/diagram boxes to avoid erasing lines
                    if (string.IsNullOrWhiteSpace(para.TranslatedText)) continue;

                    if (tableMaskRegions.Count > 0 &&
                        ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions))
                    {
                        continue;
                    }

                    if (ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, paragraphs, gfx.PageSize.Width) &&
                        !IsTranslatableBodyProse(para) && !IsTranslatableCalloutProse(para) &&
                        !IsHeadingParagraph(para) && !IsAppendixSectionHeading(para))
                    {
                        continue;
                    }

                    double pageHeight = gfx.PageSize.Height;
                    GetParagraphPaintBounds(para, out double maskX0, out double maskY0, out double maskX1, out double maskY1);

                    double renderedHeight = RenderParagraph(gfx, para, targetFontName, measureOnly: true);
                    double bboxHeight = maskY1 - maskY0;

                    const double maskPad = 2.5;
                    // White masks hide original text only; when translation is taller, grow upward
                    // instead of downward so masks from upper paragraphs cannot erase table borders.
                    double maskPdfX0 = maskX0 - maskPad;
                    double maskPdfX1 = maskX1 + maskPad;
                    double maskPdfY0 = maskY0 - maskPad;
                    double maskPdfY1 = maskY1 + maskPad + Math.Max(0.0, renderedHeight - bboxHeight);
                    ExpandMaskToColumnWidth(ref maskPdfX0, ref maskPdfX1, para, gfx.PageSize.Width);

                    // Title mask strictly clipped to title paragraph bbox (never into author band).
                    if (pageOneTitlePara != null && para == pageOneTitlePara)
                    {
                        maskPdfY1 = Math.Min(maskPdfY1, pageOneTitlePara.Y1 + maskPad);
                        maskPdfY0 = Math.Max(maskPdfY0, pageOneTitlePara.Y0 - maskPad);
                    }

                    if (tableMaskRegions.Count > 0)
                    {
                        maskPdfY0 = ClampMaskBottomAboveTables(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, tableMaskRegions);
                    }

                    if (diagramMaskRegions.Count > 0)
                    {
                        var clipRegions = GetFigureClipRegions(paragraphs, diagramMaskRegions, gfx.PageSize.Width);
                        maskPdfY1 = ClampMaskTopBelowDiagrams(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, clipRegions, gfx.PageSize.Width);
                    }

                    if (effectiveGrayMaskRegions.Count > 0 &&
                        MaskRectIntersectsAnyGrayRegion(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1,
                            effectiveGrayMaskRegions) &&
                        (para.IsGrayPromptContent || IsGrayPromptCodeParagraph(para)) &&
                        ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions))
                    {
                        continue;
                    }

                    double maskY1BeforeClamp = maskPdfY1;
                    maskPdfY1 = ClampMaskTopBelowNeighboringParagraphs(
                        maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, para, paragraphs, gfx.PageSize.Width);

                    // DEBUG: trace mask suppression for paragraphs that have translated text
                    bool dbgTrace = !string.IsNullOrWhiteSpace(para.TranslatedText);
                    if (dbgTrace)
                        ClickraDebug.LogMask(p + 1, para.Y0, para.Y1, maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1,
                            maskY1BeforeClamp, renderedHeight);

                    if (maskPdfY0 >= maskPdfY1 - 0.5) continue;

                    if (hasPageOneAuthorBand &&
                        MaskRectOverlapsPageOneAuthorBand(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1,
                            pageOneTitleBottom, pageOneAbstractTop))
                    {
                        continue;
                    }

                    if (maskPdfY0 >= maskPdfY1 - 0.5) continue;

                    double paragraphX = maskPdfX0;
                    double paragraphY = pageHeight - maskPdfY1;
                    double paragraphWidth = maskPdfX1 - maskPdfX0;
                    double paragraphHeight = maskPdfY1 - maskPdfY0;

                    gfx.DrawRectangle(XBrushes.White, paragraphX, paragraphY, paragraphWidth, paragraphHeight);
                }

                // Pass 2: Render all paragraphs (translated overlays and selectively redrawn bypassed text)
                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed)
                    {
                        if (!PdfFontStripper.ParagraphUsesStrippedFont(para, strippedBaseFonts)) continue;
                        RenderBypassedParagraph(gfx, para, targetFontName);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(para.TranslatedText)) continue;

                        if ((para.IsGrayPromptContent || IsGrayPromptCodeParagraph(para)) &&
                            effectiveGrayMaskRegions.Count > 0 &&
                            ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions))
                        {
                            continue;
                        }

                        if (p == 0 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, paragraphs, pageHeightPts))
                        {
                            continue;
                        }

                        if (pageHasTable && tableMaskRegions.Count > 0 &&
                            ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions))
                        {
                            continue;
                        }

                        if (ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, paragraphs, gfx.PageSize.Width) &&
                            !IsTranslatableBodyProse(para) && !IsTranslatableCalloutProse(para) &&
                            !IsHeadingParagraph(para) && !IsAppendixSectionHeading(para))
                        {
                            continue;
                        }

                        double measuredHeight = RenderParagraph(gfx, para, targetFontName, measureOnly: true);
                        var clipState = BeginClipRenderAboveDiagramBelow(
                            gfx, para, gfx.PageSize.Height, diagramMaskRegions, paragraphs, measuredHeight, gfx.PageSize.Width);
                        XGraphicsState? authorClipState = null;
                        if (p == 0 && pageOneTitlePara != null && para == pageOneTitlePara && hasPageOneAuthorBand)
                        {
                            authorClipState = gfx.Save();
                            double clipTop = gfx.PageSize.Height - pageOneTitlePara.Y1 - 1.5;
                            double originalClipBottom = gfx.PageSize.Height - pageOneTitlePara.Y0 + 1.5;
                            double clipBottom = PageOneLayoutClassifier.GetTitleClipBottom(
                                clipTop, originalClipBottom, measuredHeight);
                            gfx.IntersectClip(new XRect(
                                0, clipTop, gfx.PageSize.Width, Math.Max(1, clipBottom - clipTop)));
                        }
                        try
                        {
                            ClickraDebug.LogRender(p + 1, para.Y0, para.Y1, para.X0, para.X1,
                                clipState != null, measuredHeight);
                            RenderParagraph(gfx, para, targetFontName);
                        }
                        finally
                        {
                            if (authorClipState != null) gfx.Restore(authorClipState);
                            if (clipState != null) gfx.Restore(clipState);
                        }
                    }
                }
            }

            onProgress?.Invoke(95, 100, "正在儲存翻譯後的檔案...");
            finalDoc.Save(outputPath);
            finalDoc.Close();
        }

        private static bool IsTableCaptionWord(UglyToad.PdfPig.Content.Word w, List<UglyToad.PdfPig.Content.Word> words)
        {
            if (w.Text.Equals("表", StringComparison.OrdinalIgnoreCase))
            {
                // Geometric check for "表"
                double centerY = w.BoundingBox.Centroid.Y;
                double lineTolerance = w.BoundingBox.Height * 0.5;
                foreach (var other in words)
                {
                    if (other == w) continue;
                    if (Math.Abs(other.BoundingBox.Centroid.Y - centerY) < lineTolerance && other.BoundingBox.Right < w.BoundingBox.Left)
                    {
                        return false;
                    }
                }
                return true;
            }

            if (w.Text.Equals("Table", StringComparison.OrdinalIgnoreCase))
            {
                double centerY = w.BoundingBox.Centroid.Y;
                double lineTolerance = w.BoundingBox.Height * 0.5;

                // 1. Check if there is any word to the left on the same line
                foreach (var other in words)
                {
                    if (other == w) continue;
                    if (Math.Abs(other.BoundingBox.Centroid.Y - centerY) < lineTolerance && other.BoundingBox.Right < w.BoundingBox.Left)
                    {
                        return false;
                    }
                }

                // 2. Check preceding words in reading order (if they are close textually or temporally)
                int idx = words.IndexOf(w);
                if (idx > 0)
                {
                    var prevWord = words[idx - 1];
                    string prevText = prevWord.Text.Trim().ToLowerInvariant();
                    if (Math.Abs(prevWord.BoundingBox.Centroid.Y - centerY) < lineTolerance * 3.0)
                    {
                        var preps = new System.Collections.Generic.HashSet<string> { 
                            "in", "see", "shown", "of", "and", "or", "from", "on", "with", "below", "above", "shows", "depicts", "illustrates", "to", "for", "at", "using", "the" 
                        };
                        if (preps.Contains(prevText))
                        {
                            return false;
                        }
                        
                        if (idx > 1)
                        {
                            var prev2Word = words[idx - 2];
                            string prev2Text = prev2Word.Text.Trim().ToLowerInvariant();
                            if (preps.Contains(prev2Text))
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }

            return false;
        }

        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            TranslationPostProcessor.PostProcessTranslation(originalText, translatedText, targetLang);
        private static TranslationRegionDiagnostics ToDiagnosticsRegion(TableMaskRegion region) => new()
        {
            X0 = region.X0,
            Y0 = region.Y0,
            X1 = region.X1,
            Y1 = region.Y1
        };

        /// <summary>Cluster table cells into separate mask regions instead of one page-wide bounding box.</summary>
        private static List<TableMaskRegion> BuildTableMaskRegions(
            List<PdfParagraph> tableParas, double pageWidth, Func<PdfParagraph, bool>? excludePara = null)
        {
            if (excludePara != null)
                tableParas = tableParas.Where(p => !excludePara(p)).ToList();
            var regions = new List<TableMaskRegion>();
            // Only cluster actual table cells (short rows), not body paragraphs mis-marked as tables.
            var cellParas = tableParas.Where(p => p.Height <= 35).ToList();
            if (cellParas.Count < 2) return regions;

            double center = pageWidth / 2.0;
            var groups = new List<List<PdfParagraph>>();
            foreach (var cand in cellParas)
            {
                bool added = false;
                foreach (var group in groups)
                {
                    bool close = false;
                    foreach (var member in group)
                    {
                        bool candLeft = (cand.X0 + cand.Width / 2) < center;
                        bool memberLeft = (member.X0 + member.Width / 2) < center;
                        if (candLeft != memberLeft) continue;

                        double verticalDist = 0;
                        if (cand.Y1 < member.Y0)
                        {
                            verticalDist = member.Y0 - cand.Y1;
                        }
                        else if (member.Y1 < cand.Y0)
                        {
                            verticalDist = cand.Y0 - member.Y1;
                        }

                        if (verticalDist < 45)
                        {
                            close = true;
                            break;
                        }
                    }
                    if (close)
                    {
                        group.Add(cand);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<PdfParagraph> { cand });
                }
            }

            foreach (var group in groups)
            {
                if (group.Count < 2) continue;
                regions.Add(new TableMaskRegion(
                    group.Min(p => p.X0) - 8,
                    group.Min(p => p.Y0) - 8,
                    group.Max(p => p.X1) + 8,
                    group.Max(p => p.Y1) + 12));
            }

            return regions;
        }

        private static bool ParagraphOverlapsAnyTableMask(
            double paraX0, double paraY0, double paraX1, double paraY1,
            IReadOnlyList<TableMaskRegion> regions,
            double minOverlapX = 30.0, double minOverlapY = 5.0)
        {
            foreach (var region in regions)
            {
                if (ParagraphOverlapsTableMask(paraX0, paraY0, paraX1, paraY1,
                        region.X0, region.Y0, region.X1, region.Y1, minOverlapX, minOverlapY))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ParagraphOverlapsTableMask(
            double paraX0, double paraY0, double paraX1, double paraY1,
            double tableMaskX0, double tableMaskY0, double tableMaskX1, double tableMaskY1,
            double minOverlapX = 30.0, double minOverlapY = 5.0)
        {
            double overlapX = Math.Min(paraX1, tableMaskX1) - Math.Max(paraX0, tableMaskX0);
            double overlapY = Math.Min(paraY1, tableMaskY1) - Math.Max(paraY0, tableMaskY0);
            return overlapX >= minOverlapX && overlapY >= minOverlapY;
        }

        /// <summary>
        /// Raise the bottom edge of a white mask so it cannot paint over a table's top border.
        /// Table captions sit above the mask region; only the padded mask rect reaches into it.
        /// Top border lines sit ~9pt below region.Y1 (region adds 12pt above max cell Y1).
        /// </summary>
        private static double ClampMaskBottomAboveTables(
            double maskX0, double maskY0, double maskX1, double maskY1,
            IReadOnlyList<TableMaskRegion> regions,
            double minOverlapX = 10.0)
        {
            const double tableTopBorderInset = 9.0;
            const double borderClearance = 1.5;

            double clampedY0 = maskY0;
            foreach (var region in regions)
            {
                double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
                if (overlapX < minOverlapX) continue;

                // Mask overlaps table region from above: keep bottom above top border line.
                if (maskY1 > region.Y0 && clampedY0 < region.Y1)
                {
                    double minMaskBottom = region.Y1 - tableTopBorderInset + borderClearance;
                    clampedY0 = Math.Max(clampedY0, minMaskBottom);
                }
            }
            return clampedY0;
        }

        /// <summary>
        /// Cap the top edge of a white mask so upward growth cannot paint over gray prompt shaded boxes.
        /// </summary>
        private static double ClampMaskTopBelowGrayShadedRegions(
            double maskX0, double maskY0, double maskX1, double maskY1,
            IReadOnlyList<TableMaskRegion> grayRegions,
            double pageWidth = 0,
            double minOverlapX = 10.0)
        {
            if (grayRegions.Count == 0) return maskY1;
            const double clearance = 4.0;
            double clampedY1 = maskY1;
            foreach (var region in grayRegions)
            {
                if (pageWidth > 0 &&
                    !ParagraphSharesColumnWithRegion(maskX0, maskX1, region, pageWidth, minOverlapX))
                {
                    continue;
                }

                double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
                if (overlapX < minOverlapX) continue;

                if (maskY0 < region.Y0 && clampedY1 > region.Y0 - clearance)
                {
                    clampedY1 = Math.Min(clampedY1, region.Y0 - clearance);
                }
            }
            return clampedY1;
        }

        /// <summary>Prevent a lower paragraph's white mask from growing into the bbox above (same column).</summary>
        private static double ClampMaskTopBelowNeighboringParagraphs(
            double maskX0, double maskY0, double maskX1, double maskY1,
            PdfParagraph para, IReadOnlyList<PdfParagraph> allParas, double pageWidth)
        {
            const double clearance = 2.0;
            double center = pageWidth / 2.0;
            bool leftCol = (para.X0 + para.X1) / 2.0 < center - 8;
            double clampedY1 = maskY1;
            foreach (var other in allParas)
            {
                if (ReferenceEquals(other, para) || other.IsBypassed) continue;
                bool otherLeft = (other.X0 + other.X1) / 2.0 < center - 8;
                if (otherLeft != leftCol) continue;
                if (other.Y0 <= para.Y1 + 1) continue;
                if (maskY1 > other.Y0 - clearance)
                    clampedY1 = Math.Min(clampedY1, other.Y0 - clearance);
            }
            return clampedY1;
        }

        /// <summary>Union paragraph bbox with per-letter ink extents for white-mask coverage.</summary>
        private static void GetParagraphPaintBounds(
            PdfParagraph para, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = Math.Min(para.OriginalX0, para.X0);
            y0 = Math.Min(para.OriginalY0, para.Y0);
            x1 = Math.Max(para.OriginalX1, para.X1);
            y1 = Math.Max(para.OriginalY1, para.Y1);
            if (para.AllLetters.Count == 0) return;
            foreach (var letter in para.AllLetters)
            {
                if (letter.Left < x0) x0 = letter.Left;
                if (letter.Bottom < y0) y0 = letter.Bottom;
                if (letter.Right > x1) x1 = letter.Right;
                if (letter.Top > y1) y1 = letter.Top;
            }
        }

        /// <summary>Expand white masks to full column width for body prose to erase orphan glyph runs.</summary>
        private static void ExpandMaskToColumnWidth(
            ref double maskX0, ref double maskX1, PdfParagraph para, double pageWidth)
        {
            if (!IsTranslatableBodyProse(para) && !IsTranslatableCalloutProse(para)) return;
            double center = pageWidth / 2.0;
            double paraCenter = (para.X0 + para.X1) / 2.0;
            if (paraCenter < center - 8)
            {
                maskX0 = Math.Min(maskX0, 48);
                maskX1 = Math.Max(maskX1, center - 12);
            }
            else if (paraCenter > center + 8)
            {
                maskX0 = Math.Min(maskX0, center + 12);
                maskX1 = Math.Max(maskX1, pageWidth - 48);
            }
        }

        /// <summary>
        /// Gray geometry may suppress Pass 1/2 only when paragraph center/letters are inside the shaded box —
        /// loose bbox overlap must not delete translatable section body (PentestAgent p7 §3.5).
        /// </summary>
        private static bool ShouldSuppressOverlayForGrayGeometry(
            PdfParagraph para, IReadOnlyList<TableMaskRegion> effectiveGrayMaskRegions)
        {
            if (effectiveGrayMaskRegions.Count == 0) return false;
            bool insideGray = ParagraphCenterInsideAnyRegion(para, effectiveGrayMaskRegions) ||
                              IsParagraphInsideGrayShadedRegion(para, effectiveGrayMaskRegions);
            if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para) ||
                IsHeadingParagraph(para) || IsAppendixSectionHeading(para))
            {
                // Only gray-prompt paragraphs skip overlay; spurious vector gray boxes
                // (e.g. PentestAgent p4 right-column EffectiveGrayMaskRegion) must not
                // strip-and-skip translatable §2.3 body prose.
                if (para.IsGrayPromptContent || IsGrayPromptCodeParagraph(para))
                    return insideGray;
                return false;
            }
            if (insideGray) return true;
            return ParagraphOverlapsAnyTableMask(
                para.X0, para.Y0, para.X1, para.Y1,
                ExpandGrayShadedRegions(effectiveGrayMaskRegions), 8.0, 2.0);
        }

        /// <summary>
        /// Cap the top edge of a white mask so upward growth cannot paint over diagram/chart vectors.
        /// </summary>
        private static double ClampMaskTopBelowDiagrams(
            double maskX0, double maskY0, double maskX1, double maskY1,
            IReadOnlyList<TableMaskRegion> regions,
            double pageWidth = 0,
            double minOverlapX = 10.0)
        {
            const double clearance = 2.0;
            double clampedY1 = maskY1;
            foreach (var region in regions)
            {
                if (pageWidth > 0 &&
                    !ParagraphSharesColumnWithRegion(maskX0, maskX1, region, pageWidth, minOverlapX))
                {
                    continue;
                }

                double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
                if (overlapX < minOverlapX) continue;

                // Mask sits below the diagram region; cap top before diagram bottom edge.
                if (maskY0 < region.Y0 && clampedY1 > region.Y0 - clearance)
                {
                    clampedY1 = Math.Min(clampedY1, region.Y0 - clearance);
                }
            }
            return clampedY1;
        }

        /// <summary>
        /// Clip translated body text so it cannot extend into a figure region (below or in the adjacent column).
        /// </summary>
        private static XGraphicsState? BeginClipRenderAboveDiagramBelow(
            XGraphics gfx, PdfParagraph para, double pageHeight,
            IReadOnlyList<TableMaskRegion> diagramMaskRegions,
            IReadOnlyList<PdfParagraph> pageParagraphs,
            double renderedHeight,
            double pageWidth)
        {
            if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para) ||
                IsHeadingParagraph(para) || IsAppendixSectionHeading(para))
            {
                return null;
            }
            var clipRegions = GetFigureClipRegions(pageParagraphs, diagramMaskRegions, pageWidth);
            if (clipRegions.Count == 0) return null;
            if (!IsTranslatableBodyProse(para) && !IsTranslatableCalloutProse(para)) return null;
            if (IsHeadingParagraph(para) || IsAppendixSectionHeading(para)) return null;

            const double clearance = 2.0;
            double center = pageWidth / 2.0;
            double paraCenterX = para.X0 + para.Width / 2.0;
            bool paraLeftCol = paraCenterX < center - 8;
            bool paraRightCol = paraCenterX > center + 8;
            double renderedBottomPigY = para.Y0 - Math.Max(0.0, renderedHeight - para.Height);
            double clipPdfTop = pageHeight - para.Y1 - 1.5;
            double clipPdfBottom = pageHeight - renderedBottomPigY + 1.5;
            double clipPdfLeft = para.X0 - 1.5;
            double clipPdfRight = para.X1 + 1.5;
            bool needClip = false;

            foreach (var region in clipRegions)
            {
                double regionCenterX = (region.X0 + region.X1) / 2.0;
                bool regionLeftCol = regionCenterX < center - 8;
                bool regionRightCol = regionCenterX > center + 8;
                double overlapY = Math.Min(para.Y1, region.Y1) - Math.Max(para.Y0, region.Y0);
                if (overlapY <= 0) continue;

                if (paraLeftCol && regionRightCol)
                {
                    double maxRight = center - 10;
                    if (clipPdfRight > maxRight)
                    {
                        clipPdfRight = maxRight;
                        needClip = true;
                    }
                    continue;
                }

                if (paraRightCol && regionLeftCol)
                {
                    double minLeft = center + 10;
                    if (clipPdfLeft < minLeft)
                    {
                        clipPdfLeft = minLeft;
                        needClip = true;
                    }
                    continue;
                }

                double overlapX = Math.Min(para.X1, region.X1) - Math.Max(para.X0, region.X0);
                if (overlapX < 20) continue;
                double paraHeight = Math.Max(8.0, para.Y1 - para.Y0);
                if (overlapY < Math.Min(18.0, paraHeight * 0.2)) continue;
                if (renderedBottomPigY >= region.Y1 + clearance) continue;

                double figureTopPdf = pageHeight - region.Y1 - clearance;
                if (clipPdfBottom > figureTopPdf)
                {
                    clipPdfBottom = figureTopPdf;
                    needClip = true;
                }
            }

            if (!needClip || clipPdfBottom <= clipPdfTop + 1) return null;
            var state = gfx.Save();
            gfx.IntersectClip(new XRect(clipPdfLeft, clipPdfTop, clipPdfRight - clipPdfLeft, clipPdfBottom - clipPdfTop));
            return state;
        }

        /// <summary>Tight figure bounds from captions and diagram labels for body-text clip/mask.</summary>
        private static List<TableMaskRegion> GetFigureClipRegions(
            IReadOnlyList<PdfParagraph> pageParagraphs,
            IReadOnlyList<TableMaskRegion> diagramMaskRegions,
            double pageWidth)
        {
            var clips = new List<TableMaskRegion>();
            double center = pageWidth / 2.0;
            foreach (var para in pageParagraphs)
            {
                if (!IsFigureTableCaptionParagraph(para)) continue;
                string t = para.TextWithPlaceholders.Trim();
                if (!t.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) &&
                    !t.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) &&
                    !t.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool rightCol = para.X0 >= center - 20;
                TableMaskRegion? tightDiagram = null;
                foreach (var region in diagramMaskRegions)
                {
                    double regionCenter = (region.X0 + region.X1) / 2.0;
                    bool regionRightCol = regionCenter >= center - 20;
                    if (regionRightCol != rightCol) continue;
                    // PdfPig coordinates grow upward. A conventional figure sits above its
                    // caption, so compare the figure's lower edge with the caption's upper edge.
                    // Using region.Y1 against caption.Y0 rejects the real figure and creates a
                    // fallback clip below the caption, truncating masks for nearby inline math.
                    if (region.Y0 < para.Y1 - 8 || region.Y0 > para.Y1 + 200) continue;
                    if (!tightDiagram.HasValue || region.Y0 < tightDiagram.Value.Y0)
                    {
                        tightDiagram = region;
                    }
                }

                if (tightDiagram.HasValue)
                {
                    clips.Add(tightDiagram.Value);
                    continue;
                }

                double y0 = Math.Max(40, para.Y0 - 100);
                double y1 = para.Y1 + 10;
                double x0 = rightCol ? center + 5 : 40;
                double x1 = rightCol ? pageWidth - 40 : center - 5;
                clips.Add(new TableMaskRegion(x0, y0, x1, y1));
            }

            foreach (var para in pageParagraphs.Where(p => p.IsDiagram))
            {
                if (IsFigureTableCaptionParagraph(para)) continue;
                bool rightCol = para.X0 >= center - 20;
                double padX = 6;
                double padY = 4;
                double x0 = Math.Max(rightCol ? center + 5 : 40, para.X0 - padX);
                double x1 = Math.Min(rightCol ? pageWidth - 40 : center - 5, para.X1 + padX);
                double y0 = para.Y0 - padY;
                double y1 = para.Y1 + padY;
                clips.Add(new TableMaskRegion(x0, y0, x1, y1));
            }

            if (clips.Count > 0)
            {
                return BuildDiagramMaskRegions(clips);
            }

            return diagramMaskRegions
                .Select(r =>
                {
                    double regionCenter = (r.X0 + r.X1) / 2.0;
                    bool rightCol = regionCenter >= center - 20;
                    double x0 = rightCol ? Math.Max(center + 5, r.X0) : Math.Max(40, r.X0);
                    double x1 = rightCol ? Math.Min(pageWidth - 40, r.X1) : Math.Min(center - 5, r.X1);
                    return new TableMaskRegion(x0, r.Y0, x1, r.Y1);
                })
                .Where(r => (r.Y1 - r.Y0) <= 360 && r.X1 > r.X0 + 20)
                .ToList();
        }

        private static bool ParagraphSharesColumnWithRegion(
            double paraX0, double paraX1, TableMaskRegion region, double pageWidth, double minSharedWidth = 20.0)
        {
            double center = pageWidth / 2.0;
            double paraCenter = (paraX0 + paraX1) / 2.0;
            double regionCenter = (region.X0 + region.X1) / 2.0;
            bool paraLeft = paraCenter < center - 5;
            bool paraRight = paraCenter > center + 5;
            bool regionLeft = regionCenter < center - 5;
            bool regionRight = regionCenter > center + 5;
            if (paraLeft && regionRight) return false;
            if (paraRight && regionLeft) return false;
            double overlapX = Math.Min(paraX1, region.X1) - Math.Max(paraX0, region.X0);
            return overlapX >= minSharedWidth;
        }

        private static void RenderBypassedParagraph(XGraphics gfx, PdfParagraph para, string targetFontName)
        {
            double pageHeight = gfx.PageSize.Height;
            XBrush brush = XBrushes.Black;
            double tableFontSize = para.AverageFontSize > 0 ? para.AverageFontSize : 10;
            var formulaLetterKeys = BuildFormulaLetterKeys(para);

            foreach (var letter in para.AllLetters)
            {
                if (string.IsNullOrEmpty(letter.Value) || string.IsNullOrWhiteSpace(letter.Value)) continue;
                if (formulaLetterKeys.Contains(FormulaLetterKey(letter))) continue;

                string fontName = letter.FontName ?? "";
                string cleanFontName = fontName;
                int plusIdx = fontName.IndexOf('+');
                if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                {
                    cleanFontName = fontName.Substring(plusIdx + 1);
                }

                double fontSize = para.IsTable ? tableFontSize : letter.FontSize;
                XFont font;
                if (letter.Value.Any(FontUtilities.IsCjkCharacter))
                {
                    font = new XFont(targetFontName, fontSize, XFontStyleEx.Regular);
                }
                else
                {
                    font = FontUtilities.GetMathFont(letter.FontName, fontSize);
                }

                string drawVal = FontUtilities.NormalizeMathValue(letter.Value.Normalize(NormalizationForm.FormKD));
                if (drawVal.Length == 1 &&
                    (FontUtilities.IsMathOrGreekCharacter(drawVal[0]) || drawVal[0] == '*' || drawVal[0] == '†' || drawVal[0] == '‡'))
                {
                    font = new XFont("Segoe UI Symbol", fontSize, font.Style);
                }

                double x = letter.X;
                double y = pageHeight - letter.Y;
                gfx.DrawString(drawVal, font, brush, x, y);
            }

            // AllLetters is the authoritative source-positioned glyph stream
            // for bypassed paragraphs and already includes formula glyphs.
            // Repainting Formulas as well duplicates equations when embedded
            // font control characters prevent exact formula-letter matching.
            if (para.AllLetters.Count == 0)
            {
                foreach (var formula in para.Formulas)
                {
                    RenderBypassedFormula(gfx, para, formula, pageHeight, brush);
                }
            }
        }

        private static string FormulaLetterKey(PdfLetter letter)
        {
            return $"{letter.X:F2}|{letter.Y:F2}|{letter.Value}";
        }

        private static HashSet<string> BuildFormulaLetterKeys(PdfParagraph para)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (para.Formulas.Count == 0 || para.AllLetters.Count == 0) return keys;

            foreach (var formula in para.Formulas)
            {
                string needle = string.Concat(formula.Letters.Select(l => l.Value));
                if (needle.Length == 0) continue;

                for (int i = 0; i <= para.AllLetters.Count - needle.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < needle.Length; j++)
                    {
                        if (para.AllLetters[i + j].Value != formula.Letters[j].Value)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (!match) continue;
                    for (int j = 0; j < needle.Length; j++)
                    {
                        keys.Add(FormulaLetterKey(para.AllLetters[i + j]));
                    }
                    break;
                }
            }
            return keys;
        }

        private static void RenderBypassedFormula(
            XGraphics gfx, PdfParagraph para, MathFormula formula, double pageHeight, XBrush brush)
        {
            if (formula.Letters.Count == 0) return;
            string needle = string.Concat(formula.Letters.Select(l => l.Value));
            int startIdx = -1;
            for (int i = 0; i <= para.AllLetters.Count - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (para.AllLetters[i + j].Value != formula.Letters[j].Value)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    startIdx = i;
                    break;
                }
            }

            if (startIdx < 0)
            {
                foreach (var ml in formula.Letters)
                {
                    double fSize = ml.FontSize;
                    XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);
                    string drawVal = FontUtilities.NormalizeMathValue(ml.Value.Normalize(NormalizationForm.FormKD));
                    if (drawVal.Length == 1 &&
                        (FontUtilities.IsMathOrGreekCharacter(drawVal[0]) || drawVal[0] == '*' || drawVal[0] == '†' || drawVal[0] == '‡'))
                    {
                        mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                    }
                    double x = ml.X;
                    double y = ml.Y;
                    gfx.DrawString(drawVal, mathFont, brush, x, pageHeight - y);
                }
                return;
            }

            for (int j = 0; j < formula.Letters.Count; j++)
            {
                var ml = formula.Letters[j];
                var letter = para.AllLetters[startIdx + j];
                double fSize = ml.FontSize;
                XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);
                string drawVal = FontUtilities.NormalizeMathValue(ml.Value.Normalize(NormalizationForm.FormKD));
                if (drawVal.Length == 1 &&
                    (FontUtilities.IsMathOrGreekCharacter(drawVal[0]) || drawVal[0] == '*' || drawVal[0] == '†' || drawVal[0] == '‡'))
                {
                    mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                }
                double x = letter.X;
                double y = pageHeight - letter.Y;
                gfx.DrawString(drawVal, mathFont, brush, x, y);
            }
        }

        /// <summary>
        /// Marks bibliography entries as bypassed from the REFERENCES heading through the next major section.
        /// Heading itself remains translatable (e.g. REFERENCES → 參考文獻).
        /// </summary>
        private static void ApplyReferencesSectionBypass(List<List<PdfParagraph>> allPages, double[] pageWidths)
        {
            bool inSection = false;

            for (int p = 0; p < allPages.Count; p++)
            {
                double pageWidth = p < pageWidths.Length ? pageWidths[p] : 595.0;
                foreach (var para in GetPageReadingOrder(allPages[p], pageWidth))
                {
                    if (ReferenceSectionDetector.IsHeading(para))
                    {
                        inSection = true;
                        continue;
                    }

                    if (inSection && ReferenceSectionDetector.IsTerminator(para))
                    {
                        inSection = false;
                        continue;
                    }

                    if (inSection && !para.IsTable)
                    {
                        para.IsBypassed = true;
                    }
                }
            }
        }

        private static bool IsEquationParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            // Matches (1), (2), (3), etc. at the end
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"\(\d+\)\s*$")) return true;

            // Matches patterns like x : A -> B
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[a-zA-Z0-9_\{\}\s]+:.*(⇀|→|→|↦|⇒|⊆|∈)")) return true;

            // Density based check: if the text has math formulas/variables placeholders
            // and contains common math operator characters
            int formulaTokensCount = para.Formulas.Count;
            if (formulaTokensCount > 0)
            {
                // Check if the non-placeholder part contains mostly math operators or is very short
                string stripped = System.Text.RegularExpressions.Regex.Replace(txt, @"\{v\d+\}", "").Trim();
                if (string.IsNullOrEmpty(stripped)) return true;

                int letters = stripped.Count(char.IsLetter);
                int operators = stripped.Count(c => "=+-*/()[]{}<>,.:;|\\&!_^⇀→∈∧↓⟨⟩⊆×Σ∗↑↓⇀".Contains(c));
                int wordCount = stripped.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries).Length;

                // Short display equations can retain identifier fragments such
                // as "raw", "QL", and "s.t." around several formula tokens.
                // Translating them as prose linearizes independently positioned
                // glyphs and creates overlapping formulas (SemTaint p8 eq. 10).
                if (formulaTokensCount >= 2 && wordCount <= 5 && para.Height <= 18)
                {
                    return true;
                }
                 
                // If the stripped text contains mostly math operators/punctuation rather than English words
                if (letters < 3 || (double)operators / (letters + operators) > 0.4)
                {
                    return true;
                }
            }

            return false;
        }

        private static void MarkTableParagraphs(
            List<PdfParagraph> pageList, double pageWidth, double pageHeight, bool isTablePage)
        {
            bool hasAuthorBand = PageOneLayoutClassifier.TryGetAuthorBand(
                pageList, pageHeight, out double authorTitleBottom, out double authorAbstractTop, out var authorTitlePara);
            var candidates = new List<PdfParagraph>();
            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (string.IsNullOrEmpty(txt)) continue;

                if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsEquationParagraph(para)) continue;

                // Exclude citations, references, and links from becoming table candidates
                if (txt.StartsWith("[") ||
                    txt.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.IndexOf("doi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.IndexOf("www.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"\b10\.\d{4,}/"))
                {
                    continue;
                }

                // Exclude list labels (e.g. "1.", "2.", "a.", "(1)")
                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^(?:\d+|[a-zA-Z])\.$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\((?:\d+|[a-zA-Z])\)$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"^(?:\d+\.\s*)+$"))
                {
                    continue;
                }

                // Exclude section numbering headings (e.g. "3.2", "3.2.1", "10. WORK DIVISION")
                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+(?:\.\d+)*\.?\s+[A-Z]"))
                {
                    continue;
                }

                // Exclude single character / punctuation-only paragraphs
                if (txt.Length <= 2 && !System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[0-9✓xX-]$"))
                {
                    continue;
                }

                string[] allWords = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (allWords.Length > 50) continue;

                if (para.Width < pageWidth * 0.45 && para.Height < 120)
                {
                    int rowAlignedCount = 0;
                    int colAlignedCount = 0;

                    foreach (var other in pageList)
                    {
                        if (other == para) continue;

                        double overlapY = Math.Min(para.Y1, other.Y1) - Math.Max(para.Y0, other.Y0);
                        double minHeight = Math.Min(para.Height, other.Height);
                        if (overlapY > minHeight * 0.1)
                        {
                            rowAlignedCount++;
                        }

                        double overlapX = Math.Min(para.X1, other.X1) - Math.Max(para.X0, other.X0);
                        double minWidth = Math.Min(para.Width, other.Width);
                        if (overlapX > minWidth * 0.5)
                        {
                            colAlignedCount++;
                        }
                    }

                    bool colAlignedOk = (colAlignedCount >= 1) || isTablePage;
                    // Row-style tables (one PDF paragraph per row) align vertically, not side-by-side.
                    bool rowStyleTable = isTablePage && colAlignedCount >= 2 && para.Height < 35 && para.Width > 80;
                    if ((rowAlignedCount >= 1 && colAlignedOk) || rowStyleTable)
                    {
                        candidates.Add(para);
                    }
                }
            }

            // Filter candidates to keep only those that have a horizontal neighbor,
            // or vertically stacked full-width rows on table pages (e.g. TABLE V).
            var filteredCandidates = new List<PdfParagraph>();
            foreach (var cand in candidates)
            {
                bool isRowStyle = isTablePage && cand.Height < 35 && cand.Width > 80;
                bool hasNeighbor = false;
                foreach (var other in candidates)
                {
                    if (other == cand) continue;
                    double overlapY = Math.Min(cand.Y1, other.Y1) - Math.Max(cand.Y0, other.Y0);
                    double minH = Math.Min(cand.Height, other.Height);
                    if (overlapY > minH * 0.1)
                    {
                        double overlapX = Math.Min(cand.X1, other.X1) - Math.Max(cand.X0, other.X0);
                        if (overlapX <= 0)
                        {
                            hasNeighbor = true;
                            break;
                        }
                    }
                }
                if (hasNeighbor || isRowStyle)
                {
                    filteredCandidates.Add(cand);
                }
            }
            candidates = filteredCandidates;

            if (candidates.Count < 2) return;

            var groups = new List<List<PdfParagraph>>();
            foreach (var cand in candidates)
            {
                bool added = false;
                foreach (var group in groups)
                {
                    bool close = false;
                    foreach (var member in group)
                    {
                        double center = pageWidth / 2;
                        bool candIsLeft = cand.X1 <= center + 5;
                        bool memberIsLeft = member.X1 <= center + 5;
                        if (candIsLeft != memberIsLeft) continue;
                        double verticalDist = 0;
                        if (cand.Y1 < member.Y0)
                        {
                            verticalDist = member.Y0 - cand.Y1;
                        }
                        else if (member.Y1 < cand.Y0)
                        {
                            verticalDist = cand.Y0 - member.Y1;
                        }
                        else
                        {
                            verticalDist = 0;
                        }

                        // Tightened threshold from 80 to 45 to prevent multi-column chaining
                        if (verticalDist < 45)
                        {
                            close = true;
                            break;
                        }
                    }
                    if (close)
                    {
                        group.Add(cand);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<PdfParagraph> { cand });
                }
            }

            foreach (var group in groups)
            {
                if (group.Count < 2) continue;

                // Enforce that a table group must have at least one pair of horizontally adjacent cells
                bool hasHorizontalPair = false;
                for (int i = 0; i < group.Count; i++)
                {
                    for (int j = i + 1; j < group.Count; j++)
                    {
                        var p1 = group[i];
                        var p2 = group[j];
                        double overlapY = Math.Min(p1.Y1, p2.Y1) - Math.Max(p1.Y0, p2.Y0);
                        double minH = Math.Min(p1.Height, p2.Height);
                        if (overlapY > minH * 0.1)
                        {
                            double overlapX = Math.Min(p1.X1, p2.X1) - Math.Max(p1.X0, p2.X0);
                            if (overlapX <= 0) // No horizontal overlap means they are side-by-side
                            {
                                hasHorizontalPair = true;
                                break;
                            }
                        }
                    }
                    if (hasHorizontalPair) break;
                }
                bool isRowStyleGroup = isTablePage && group.All(p => p.Height < 35 && p.Width > 80);
                if (!hasHorizontalPair && !isRowStyleGroup) continue;

                foreach (var member in group)
                {
                    member.IsTable = true;
                }

                double minY = group.Min(p => p.Y0);
                double maxY = group.Max(p => p.Y1);
                double minX = group.Min(p => p.X0);
                double maxX = group.Max(p => p.X1);

                minY -= 15;
                maxY += 15;
                minX -= 15;
                maxX += 15;

                foreach (var para in pageList)
                {
                    string txt = para.TextWithPlaceholders.Trim();
                    if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double centerX = para.X0 + para.Width / 2;
                    double centerY = para.Y0 + para.Height / 2;

                    if (centerX >= minX && centerX <= maxX && centerY >= minY && centerY <= maxY)
                    {
                        if (hasAuthorBand && authorTitlePara != null &&
                            PageOneLayoutClassifier.IsInAuthorBand(para, authorTitleBottom, authorAbstractTop, authorTitlePara))
                        {
                            continue;
                        }

                        string[] words = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\s")) continue;
                        // Skip multi-line body paragraphs that happen to fall inside expanded table bbox
                        if (para.Height > 30 && words.Length > 20) continue;
                        // Increased word count limit to 150 to allow long cell descriptions (like work division) to be bypassed
                        if (words.Length <= 150)
                        {
                            para.IsTable = true;
                        }
                    }
                }
            }

            // Fallback: merged multi-row table blocks (when row splitting still groups rows together).
            if (isTablePage)
            {
                foreach (var para in pageList)
                {
                    if (para.IsTable) continue;
                    string txt = para.TextWithPlaceholders.Trim();
                    if (string.IsNullOrEmpty(txt)) continue;
                    if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (para.Height >= 35 && para.Height < 120 && para.Width > 80 && para.Width < pageWidth * 0.45)
                    {
                        int digitGroups = System.Text.RegularExpressions.Regex.Matches(txt, @"\b\d+\b").Count;
                        int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        if (digitGroups >= 4 && wordCount <= 18)
                        {
                            para.IsTable = true;
                        }
                    }
                }

                MarkTableRegionByCaption(pageList, pageWidth);
            }
        }

        private static void ReclassifyCalloutFindingsText(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (!para.IsDiagram) continue;
                if (IsTranslatableCalloutProse(para))
                {
                    para.IsDiagram = false;
                }
            }
        }

        private static void MarkTableRegionByCaption(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfParagraph? caption = null;
            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        txt, @"^(?:TABLE|Table)\s+[IVXLCDM\d]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    caption = para;
                    break;
                }
            }
            if (caption == null) return;

            double captionCenterX = caption.X0 + caption.Width / 2;
            bool captionOnLeft = captionCenterX < pageWidth / 2;
            double prevBottom = caption.Y0;

            foreach (var para in pageList.OrderByDescending(p => p.Y1))
            {
                if (para == caption) continue;

                double paraCenterX = para.X0 + para.Width / 2;
                if ((paraCenterX < pageWidth / 2) != captionOnLeft) continue;
                if (para.Y1 > caption.Y0 + 5) continue;

                double gap = prevBottom - para.Y1;
                if (gap > 28) break;

                string txt = para.TextWithPlaceholders.Trim();
                if (txt.StartsWith("Listing", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[IVXLC]+\.\s"))
                {
                    break;
                }

                if (para.Height > 30 && para.Width > pageWidth * 0.35 &&
                    txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length > 25)
                {
                    break;
                }

                para.IsTable = true;
                prevBottom = para.Y0;
            }
        }

        private static bool IsTableParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return true;

            // Section number fragments (e.g. "2", "2.1") are not table data.
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2})?$"))
                return false;

            int letterCount = txt.Count(char.IsLetter);
            if (letterCount == 0) return true;

            return false;
        }

        private static bool OverlapsWithLargeImage(PdfParagraph para, UglyToad.PdfPig.Content.Page pigPage)
        {
            try
            {
                foreach (var region in GetLargeDiagramBounds(pigPage))
                {
                    bool intersectX = para.X0 <= region.X1 && para.X1 >= region.X0;
                    bool intersectY = para.Y0 <= region.Y1 && para.Y1 >= region.Y0;
                    if (intersectX && intersectY)
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static List<TableMaskRegion> SplitWideDiagramMaskRegionsByColumn(
            List<TableMaskRegion> regions, double pageWidth)
        {
            if (regions.Count == 0) return regions;
            double center = pageWidth / 2.0;
            double maxColWidth = pageWidth * 0.52;
            var result = new List<TableMaskRegion>();
            foreach (var r in regions)
            {
                double w = r.X1 - r.X0;
                if (w <= maxColWidth)
                {
                    result.Add(r);
                    continue;
                }

                var left = new TableMaskRegion(r.X0, r.Y0, Math.Min(r.X1, center - 5), r.Y1);
                var right = new TableMaskRegion(Math.Max(r.X0, center + 5), r.Y0, r.X1, r.Y1);
                if (left.X1 - left.X0 >= 80)
                    result.Add(left);
                if (right.X1 - right.X0 >= 80)
                    result.Add(right);
            }
            return result;
        }

        private static List<TableMaskRegion> BuildProcessedDiagramMaskRegions(
            UglyToad.PdfPig.Content.Page pigPage, IReadOnlyList<PdfParagraph> pageList)
        {
            return CapDiagramMaskBelowFigureCaptions(
                ShrinkDiagramMaskRegionsBottomGutter(
                    SplitWideDiagramMaskRegionsByColumn(
                        BuildDiagramMaskRegions(GetLargeDiagramBounds(pigPage)),
                        pigPage.Width)),
                pageList is List<PdfParagraph> list ? list : pageList.ToList(),
                pigPage.Width);
        }

        /// <summary>Collect large image/path bounding boxes that define diagram/chart regions.</summary>
        private static List<TableMaskRegion> GetLargeDiagramBounds(UglyToad.PdfPig.Content.Page pigPage)
        {
            var bounds = new List<TableMaskRegion>();
            try
            {
                foreach (var img in pigPage.GetImages())
                {
                    if (img.Bounds.Width > 80 && img.Bounds.Height > 80)
                    {
                        var b = img.Bounds;
                        bounds.Add(new TableMaskRegion(b.Left, b.Bottom, b.Right, b.Top));
                    }
                }

                foreach (var path in pigPage.ExperimentalAccess.Paths)
                {
                    var rectOpt = path.GetBoundingRectangle();
                    if (!rectOpt.HasValue) continue;
                    var b = rectOpt.Value;

                    // Skip full-page borders
                    if (b.Width > pigPage.Width * 0.9 || b.Height > pigPage.Height * 0.9)
                        continue;

                    // Skip thin horizontal rules (e.g. column separators, table borders)
                    bool isThinHRule = b.Width > pigPage.Width * 0.35 && b.Height < 3.0;
                    bool isThinVRule = b.Height > pigPage.Height * 0.35 && b.Width < 3.0;
                    if (isThinHRule || isThinVRule) continue;

                    // Collect any path with meaningful area (small paths cluster into diagram bounds below)
                    if (b.Width > 4.0 && b.Height > 4.0)
                    {
                        bounds.Add(new TableMaskRegion(b.Left, b.Bottom, b.Right, b.Top));
                    }
                }
            }
            catch { }
            return bounds;
        }

        /// <summary>Merge nearby diagram path bounds into mask regions for overlay protection.</summary>
        private static List<TableMaskRegion> BuildDiagramMaskRegions(List<TableMaskRegion> rawBounds)
        {
            if (rawBounds.Count == 0) return rawBounds;
            var merged = new List<TableMaskRegion>();
            var used = new bool[rawBounds.Count];
            for (int i = 0; i < rawBounds.Count; i++)
            {
                if (used[i]) continue;
                var r = rawBounds[i];
                double x0 = r.X0, y0 = r.Y0, x1 = r.X1, y1 = r.Y1;
                used[i] = true;
                int count = 1;
                // Track whether any original constituent was already large enough to be a diagram on its own
                bool hasLargeOriginal = (r.X1 - r.X0 > 80 && r.Y1 - r.Y0 > 30) || (r.X1 - r.X0 > 30 && r.Y1 - r.Y0 > 60);
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int j = 0; j < rawBounds.Count; j++)
                    {
                        if (used[j]) continue;
                        var o = rawBounds[j];
                        bool closeX = o.X0 <= x1 + 25 && o.X1 >= x0 - 25;
                        bool closeY = o.Y0 <= y1 + 25 && o.Y1 >= y0 - 25;
                        if (closeX && closeY)
                        {
                            x0 = Math.Min(x0, o.X0);
                            y0 = Math.Min(y0, o.Y0);
                            x1 = Math.Max(x1, o.X1);
                            y1 = Math.Max(y1, o.Y1);
                            used[j] = true;
                            changed = true;
                            count++;
                            if ((o.X1 - o.X0 > 80 && o.Y1 - o.Y0 > 30) || (o.X1 - o.X0 > 30 && o.Y1 - o.Y0 > 60))
                                hasLargeOriginal = true;
                        }
                    }
                }
                double mergedW = x1 - x0;
                double mergedH = y1 - y0;
                // Retain: originally large element, cluster of 3+ small paths, or merged area is sizeable
                if (hasLargeOriginal || count >= 3 ||
                    (mergedW > 80 && mergedH > 40) || (mergedW > 40 && mergedH > 80))
                {
                    merged.Add(new TableMaskRegion(x0 - 4, y0 - 4, x1 + 4, y1 + 4));
                }
            }
            return merged;
        }

        private static double ParagraphLetterOverlapRatio(PdfParagraph para, IReadOnlyList<TableMaskRegion> regions)
        {
            if (para.AllLetters.Count == 0 || regions.Count == 0) return 0;
            int hit = 0;
            foreach (var letter in para.AllLetters)
            {
                foreach (var region in regions)
                {
                    if (letter.X >= region.X0 && letter.X <= region.X1 &&
                        letter.Y >= region.Y0 && letter.Y <= region.Y1)
                    {
                        hit++;
                        break;
                    }
                }
            }
            return (double)hit / para.AllLetters.Count;
        }

        private static bool IsRunningHeaderOrFooter(PdfParagraph para, double pageHeight)
        {
            if (para.Y1 > pageHeight * 0.88 && para.Height < 22)
            {
                return true;
            }
            if (para.Y0 < pageHeight * 0.08 && para.Height < 14 && para.Width < 45)
            {
                return true;
            }
            return false;
        }

        private static bool IsFigureTableCaptionParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            return txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTranslatableBodyProse(PdfParagraph para)
        {
            if (IsLikelyChartLabel(para)) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= 12 && para.Height >= 25 && txt.IndexOf('.') >= 0) return true;
            if (wordCount >= 10 && para.Width > 100 && txt.IndexOf('.') >= 0) return true;
            return false;
        }

        private static bool IsTranslatableCalloutProse(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;

            // RQ findings callout boxes (TOGLL p7/p8).
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^(?:RQ\d+\s+)?Findings?:",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Stage-marker body paragraphs inside workflow pages (section body, not diagram labels).
            if (System.Text.RegularExpressions.Regex.IsMatch(txt,
                    @"^(?:Intelligence Gathering|Vulnerability Analysis|Exploitation|Knowledge (?:Acquisition|Extraction)):",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (IsHeadingParagraph(para)) return true;
            return IsTranslatableBodyProse(para);
        }

        private static bool IsLikelyChartLabel(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount <= 4 && para.Height <= 22 && txt.IndexOf('.') < 0) return true;
            if (para.Height <= 14 && txt.Length <= 8) return true;
            if (txt.StartsWith("(a)", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("(b)", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("(c)", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^(I\.G\.|V\.A\.|E\.?|Cost|Models?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (txt.Contains('%') && para.Width < 30 && para.Height >= 25)
            {
                return true;
            }
            if (IsLikelyBarChartAxisLabel(para))
            {
                return true;
            }
            if (txt.Equals("LLM", StringComparison.OrdinalIgnoreCase) && para.Height <= 14)
            {
                return true;
            }
            if (wordCount <= 6 && para.Height <= 12 &&
                (txt.Contains('–') || txt.Contains('-')) &&
                txt.IndexOf('.') < 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Universal chart tick detector: any paragraph that is physically tiny
        /// (height &lt; 7pt, width &lt; 14pt) and contains only digits, a percent value,
        /// or a single letter is an axis tick / legend mark that must never be translated.
        /// Extremely tiny glyphs (height &lt; 5pt, width &lt; 8pt) are bypassed unconditionally
        /// since no body text can be this small — these are legend color patches or tick marks.
        /// </summary>
        private static bool IsChartTickGlyph(PdfParagraph para)
        {
            // Tier 1: unconditional bypass for micro-glyphs (legend patches, dot ticks, etc.)
            if (para.Height < 5.0 && para.Width < 8.0) return true;
            // Tier 2: tiny glyphs with numeric/single-letter content. Some ACM bar charts
            // render tick labels at ~7.6pt high and ~6.8pt wide (PentestAgent Fig. 7);
            // if these are translated/masked, the mask expands to the whole column and
            // erases the bars behind them.
            if (para.Height > 8.2 || para.Width > 20.0) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            // Pure integer or decimal (e.g. "0", "100", "3.5")
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+(\.\d+)?%?$")) return true;
            // Single ASCII letter (e.g. axis tick labels like "A", "B")
            if (txt.Length == 1 && char.IsLetter(txt[0]) && txt[0] < 128) return true;
            return false;
        }

        private static bool IsLikelyBarChartAxisLabel(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(txt,
                    @"^(?:Compeletion|Completion)\s+Level\s*\(\s*%\s*\)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(txt,
                    @"^Success\s+Rate\s*\(\s*%\s*\)(?:\s+\d+)?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (txt.Equals("Models", StringComparison.OrdinalIgnoreCase) && para.Height <= 22 && para.Width <= 70)
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\(\s*[abc]\s*\)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                para.Height <= 18)
            {
                return true;
            }
            return false;
        }

        /// <summary>When vector/image bounds are missing, infer diagram masks from large table bboxes on figure pages.</summary>
        private static List<TableMaskRegion> GetEffectiveDiagramMaskRegions(
            IReadOnlyList<TableMaskRegion> diagramRegions,
            IReadOnlyList<TableMaskRegion> tableMaskRegions,
            IReadOnlyList<PdfParagraph> pageList)
        {
            if (diagramRegions.Count > 0)
            {
                return new List<TableMaskRegion>(diagramRegions);
            }

            if (tableMaskRegions.Count == 0) return new List<TableMaskRegion>();

            bool hasFigureContent = pageList.Any(p =>
            {
                string t = p.TextWithPlaceholders.Trim();
                return t.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                       t.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) ||
                       t.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase);
            });
            if (!hasFigureContent) return new List<TableMaskRegion>();

            return tableMaskRegions
                .Where(r => (r.X1 - r.X0) > 100 && (r.Y1 - r.Y0) > 60)
                .ToList();
        }

        /// <summary>Bar-chart axis labels on pages without vector diagram bounds (PentestAgent p10/p11).</summary>
        private static void ReclassifyStandaloneChartLabelsAsDiagram(List<PdfParagraph> pageList)
        {
            bool pageHasBarChart = pageList.Any(p =>
            {
                string t = p.TextWithPlaceholders.Trim();
                return System.Text.RegularExpressions.Regex.IsMatch(t,
                    @"^Figure\s+\d+:.*(?:Success rate|Completion level|overhead|Backbone|difficulty levels)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            });
            if (!pageHasBarChart) return;

            foreach (var para in pageList)
            {
                if (IsFigureTableCaptionParagraph(para)) continue;
                if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para)) continue;
                if (IsLikelyChartLabel(para) || IsLikelyBarChartAxisLabel(para))
                {
                    para.IsTable = false;
                    para.IsDiagram = true;
                    continue;
                }
                string txt = para.TextWithPlaceholders.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+$") &&
                    para.Height <= 14 && para.Width <= 20)
                {
                    para.IsTable = false;
                    para.IsDiagram = true;
                }
            }
        }

        private static bool IsLongBodyProse(PdfParagraph para) => IsTranslatableBodyProse(para);

        /// <summary>
        /// Mark selectable chart labels whose letters overlap large vector/image bounds
        /// but were missed by paragraph-bbox intersection alone.
        /// </summary>
        private static void MarkDiagramFigureLabels(
            List<PdfParagraph> pageList,
            UglyToad.PdfPig.Content.Page pigPage,
            IReadOnlyList<TableMaskRegion> diagramRegions)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (para.IsTable && !para.IsDiagram) continue;
                if (IsRunningHeaderOrFooter(para, pigPage.Height)) continue;
                if (IsFigureTableCaptionParagraph(para)) continue;
                if (IsTranslatableCalloutProse(para)) continue;
                if (IsHeadingParagraph(para)) continue;

                double letterRatio = ParagraphLetterOverlapRatio(para, diagramRegions);
                bool bboxHits = OverlapsWithLargeImage(para, pigPage);
                bool regionHits = OverlapsAnyRegion(para, diagramRegions);
                if (letterRatio >= 0.35 || (bboxHits && IsLikelyChartLabel(para)) ||
                    (letterRatio >= 0.2 && IsLikelyChartLabel(para)) ||
                    (regionHits && IsLikelyChartLabel(para)))
                {
                    para.IsDiagram = true;
                    para.IsTable = false;
                }
            }
        }

        /// <summary>Last pass: any short text inside diagram mask regions becomes a figure label.</summary>
        private static void FinalizeDiagramFigureLabels(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions,
            double pageHeight)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (para.IsTable) continue;
                if (IsRunningHeaderOrFooter(para, pageHeight)) continue;
                if (IsFigureTableCaptionParagraph(para)) continue;
                if (IsTranslatableCalloutProse(para)) continue;
                if (IsHeadingParagraph(para)) continue;
                if (!OverlapsAnyRegion(para, diagramRegions)) continue;
                if (para.Height > 50) continue;
                string txt = para.TextWithPlaceholders.Trim();
                double letterRatio = ParagraphLetterOverlapRatio(para, diagramRegions);
                if (IsTranslatableBodyProse(para) && letterRatio < 0.35) continue;
                if (txt.Length > 140)
                {
                    if (letterRatio < 0.45 || IsTranslatableBodyProse(para)) continue;
                }
                if (!IsLikelyChartLabel(para) &&
                    letterRatio < 0.2 &&
                    !(para.Height <= 20 && txt.Length <= 120 && txt.IndexOf('.') < 0))
                {
                    continue;
                }

                para.IsDiagram = true;
                para.IsTable = false;
            }
        }

        /// <summary>Workflow figure banner lines (PentestAgent p5 Fig.1 headers) inside diagram masks.</summary>
        private static void MarkWorkflowBannerTextInDiagramRegions(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions,
            double pageHeight)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (para.IsCode || para.IsGrayPromptContent || IsGrayPromptCodeParagraph(para)) continue;
                if (para.IsTable) continue;
                if (IsRunningHeaderOrFooter(para, pageHeight)) continue;
                if (IsFigureTableCaptionParagraph(para) || IsHeadingParagraph(para)) continue;
                if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para)) continue;
                if (!OverlapsAnyRegion(para, diagramRegions)) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (para.Height > 24 || txt.Length > 220) continue;
                double letterRatio = ParagraphLetterOverlapRatio(para, diagramRegions);
                if (para.Height <= 22 && letterRatio >= 0.08)
                {
                    para.IsDiagram = true;
                    para.IsTable = false;
                    continue;
                }
                if (IsLikelyChartLabel(para) || letterRatio >= 0.15)
                {
                    para.IsDiagram = true;
                    para.IsTable = false;
                }
            }
        }

        /// <summary>
        /// Preserve selectable labels in a full-width workflow figure immediately
        /// above its caption. Some PDFs draw each box independently, so vector
        /// region clustering never yields one enclosing diagram rectangle.
        /// </summary>
        private static void MarkWorkflowFigureLabelsAboveCaption(
            List<PdfParagraph> pageList,
            double pageHeight)
        {
            foreach (var caption in pageList.Where(IsFigureTableCaptionParagraph))
            {
                if (caption.Width < 300) continue;

                double bandBottom = caption.Y1 + 8;
                double bandTop = Math.Min(pageHeight - 30, caption.Y1 + 105);
                var candidates = pageList.Where(para =>
                    !ReferenceEquals(para, caption) &&
                    para.Y0 >= bandBottom &&
                    para.Y1 <= bandTop &&
                    para.Height <= 35)
                    .ToList();

                // Require several independent labels so a normal figure caption
                // below prose cannot accidentally protect unrelated body text.
                if (candidates.Count < 4) continue;
                foreach (var para in candidates)
                {
                    para.IsDiagram = true;
                    para.IsTable = false;
                    para.IsBypassed = true;
                }
            }
        }

        /// <summary>
        /// Preserve selectable source code inside narrow, right-column figures.
        /// TOGLL Figures 4 and 5 use a caption below a code screenshot whose
        /// width is too small for the full-width workflow-figure heuristic.
        /// </summary>
        private static void MarkCodeFigureContentAboveCaption(
            List<PdfParagraph> pageList,
            double pageWidth,
            double pageHeight)
        {
            foreach (var caption in pageList.Where(IsFigureTableCaptionParagraph))
            {
                string text = caption.TextWithPlaceholders.Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        text, @"^Fig\.\s*[45]\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    continue;
                }

                double captionCenter = caption.X0 + caption.Width / 2;
                if (captionCenter < pageWidth / 2) continue;

                double bandBottom = caption.Y1 + 4;
                double bandTop = Math.Min(pageHeight - 20, caption.Y1 + 260);
                var candidates = pageList.Where(para =>
                    !ReferenceEquals(para, caption) &&
                    para.X0 >= pageWidth / 2 - 12 &&
                    para.Y0 >= bandBottom &&
                    para.Y1 <= bandTop)
                    .ToList();

                bool hasCodeAnchor = candidates.Any(para =>
                {
                    string candidateText = para.TextWithPlaceholders;
                    return candidateText.Contains("public ", StringComparison.Ordinal) ||
                           candidateText.Contains("assert", StringComparison.Ordinal) ||
                           candidateText.Contains("//TOGLL", StringComparison.OrdinalIgnoreCase) ||
                           candidateText.Contains("//EvoSuite", StringComparison.OrdinalIgnoreCase) ||
                           candidateText.Contains("//Ground Truth", StringComparison.OrdinalIgnoreCase);
                });
                if (!hasCodeAnchor) continue;

                foreach (var para in candidates)
                {
                    para.IsDiagram = true;
                    para.IsTable = false;
                    para.IsGrayPromptContent = false;
                    para.IsBypassed = true;
                }
            }
        }

        private static void ClearDiagramFlagOnFigureCaptions(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (!IsFigureTableCaptionParagraph(para)) continue;
                para.IsDiagram = false;
                para.IsTable = false;
                if (!para.IsCode)
                {
                    para.IsBypassed = false;
                }
            }
        }

        private static void ClearDiagramFlagOnSectionHeadings(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (!para.IsDiagram) continue;
                if (!IsHeadingParagraph(para) && !IsAppendixSectionHeading(para)) continue;
                para.IsDiagram = false;
                if (!para.IsTable && !para.IsCode)
                {
                    para.IsBypassed = false;
                }
            }
        }

        private static void ClearDiagramFlagOnTranslatableProse(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions)
        {
            foreach (var para in pageList)
            {
                if (!para.IsDiagram) continue;

                // Selectable text embedded in a vector workflow diagram can look
                // prose-like (lowercase words, colons, periods). If its letters
                // materially overlap the detected diagram geometry, keep the
                // original label instead of masking and reflowing it as body text.
                if (ParagraphLetterOverlapRatio(para, diagramRegions) >= 0.15)
                {
                    continue;
                }

                bool shouldClear = false;
                if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para))
                {
                    shouldClear = true;
                }
                else
                {
                    string txt = para.TextWithPlaceholders.Trim();
                    if (para.Width >= 120 && txt.Any(char.IsLower) &&
                        (txt.IndexOf('.') >= 0 || txt.Contains("{v")))
                    {
                        shouldClear = true;
                    }
                }

                if (!shouldClear) continue;
                para.IsDiagram = false;
                if (!para.IsTable && !para.IsCode)
                {
                    para.IsBypassed = false;
                }
            }
        }

        private static bool IsSectionIntroProse(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            return txt.StartsWith("The following", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("From our", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("In this section", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("After obtaining", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGrayPromptBoxParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;
            if (!IsGrayPromptBoxTitleParagraph(para)) return false;
            if (txt.Contains("(Simplified)", StringComparison.OrdinalIgnoreCase)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"\bPrompt\s*(?:\(Simplified\))?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"\bExample\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(txt,
                    @"(?:^|\b)(?:Prompt\s+for|System Message|Role-?play|\bCoT\b|Structured Output\b|Analysis Prompt\b)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (txt.Contains("JSON format", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("FORMAT SPEC", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("OUTPUT FORMAT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        /// <summary>Gray prompt titles are single-line box headers, not body prose ending in "prompt".</summary>
        private static bool IsGrayPromptBoxTitleParagraph(PdfParagraph para)
        {
            return para.Height <= 22 && para.Width <= 280;
        }

        private static bool IsGrayPromptBoxContinuationParagraph(PdfParagraph para, PdfParagraph? anchor)
        {
            if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para) ||
                IsHeadingParagraph(para) || IsAppendixSectionHeading(para))
            {
                return false;
            }
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\)"))
            {
                // Section body like "2) Loss of Context:" — not a prompt list item inside gray boxes.
                if (para.Height > 28 || para.Width > 250) return false;
                if (txt.Contains(" of ", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\(\d+\)"))
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^AMPLE\}?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (txt.StartsWith("LLM:", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (txt.StartsWith("You ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("You\u2019re ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("You're ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Analyze ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Use your ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("For example", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Generate a ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Your next task", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("You should use ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("You should always ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("You should ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("When the results", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (txt.Contains("JSON format", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("FORMAT SPEC", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("OUTPUT FORMAT", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("{FORMAT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (anchor == null) return false;
            double gap = anchor.Y1 - para.Y1;
            double overlap = Math.Min(para.X1, anchor.X1) - Math.Max(para.X0, anchor.X0);
            double minWidth = Math.Min(para.Width, anchor.Width);
            if (gap >= -2 && gap <= 32 && minWidth > 0 && overlap / minWidth >= 0.55 &&
                para.Height <= 22 && txt.Length <= 160 &&
                !IsTranslatableBodyProse(para) && !IsTranslatableCalloutProse(para))
            {
                return true;
            }
            // Hyphenated prompt lines split across PDF text blocks (e.g. "EX-" / "AMPLE}").
            if (gap >= -2 && gap <= 18 && minWidth > 0 && overlap / minWidth >= 0.55 &&
                para.Height <= 14 && txt.Length <= 16)
            {
                return true;
            }
            return false;
        }

        private static bool IsGrayPromptSubheading(PdfParagraph para)
        {
            if (para.Height > 20) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (txt.Length > 48) return false;
            return txt.Equals("RAG", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("CoT", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Role-play", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Self-reflection", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Structured Output", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("GPT-4", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNearbyGrayPromptAbove(
            PdfParagraph para, List<PdfParagraph> pageList, double maxGap = 55)
        {
            foreach (var other in pageList)
            {
                if (!other.IsGrayPromptContent) continue;
                if (!SharesGrayPromptColumn(para, other)) continue;
                double gap = other.Y0 - para.Y1;
                if (gap >= -2 && gap <= maxGap) return true;
            }
            return false;
        }

        private static bool SharesGrayPromptColumn(PdfParagraph a, PdfParagraph b)
        {
            double overlap = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
            double minWidth = Math.Min(a.Width, b.Width);
            return minWidth > 0 && overlap / minWidth >= 0.45;
        }

        /// <summary>
        /// Gray prompt / system-message boxes are treated like code: bypass, keep English, no strip.
        /// </summary>
        private static void MarkGrayPromptBoxesAsCode(
            List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayShadedRegions)
        {
            bool inGrayPromptBlock = false;
            PdfParagraph? anchor = null;
            foreach (var para in pageList.OrderByDescending(p => p.Y1))
            {
                if (IsGrayPromptBoxParagraph(para))
                {
                    if (inGrayPromptBlock && anchor != null && SharesGrayPromptColumn(para, anchor))
                    {
                        MarkAsGrayPromptContent(para);
                        anchor = para;
                        continue;
                    }

                    inGrayPromptBlock = true;
                    anchor = para;
                    MarkAsGrayPromptContent(para);
                    continue;
                }

                if (!inGrayPromptBlock || anchor == null) continue;

                if (!SharesGrayPromptColumn(para, anchor)) continue;

                // Descending-Y iteration: only extend block to paragraphs below the anchor.
                if (para.Y1 >= anchor.Y1 - 1) continue;

                double gapBelow = anchor.Y0 - para.Y1;
                if (gapBelow > 45)
                {
                    inGrayPromptBlock = false;
                    anchor = null;
                    continue;
                }

                if (IsGrayPromptSubheading(para))
                {
                    MarkAsGrayPromptContent(para);
                    anchor = para;
                    continue;
                }

                if (IsGrayPromptBoxContinuationParagraph(para, anchor))
                {
                    MarkAsGrayPromptContent(para);
                    anchor = para;
                    continue;
                }

                if (grayShadedRegions.Count > 0 &&
                    (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para)) &&
                    !IsParagraphInsideGrayShadedRegion(para, grayShadedRegions))
                {
                    inGrayPromptBlock = false;
                    anchor = null;
                    continue;
                }

                string txt = para.TextWithPlaceholders.Trim();
                if (IsHeadingParagraph(para) ||
                    (IsFigureTableCaptionParagraph(para) && SharesGrayPromptColumn(para, anchor)) ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\.\d+\s") ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\d+\s"))
                {
                    inGrayPromptBlock = false;
                    anchor = null;
                    continue;
                }

                if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para))
                {
                    if (IsSectionIntroProse(para))
                    {
                        inGrayPromptBlock = false;
                        anchor = null;
                        continue;
                    }
                    if (grayShadedRegions.Count > 0 &&
                        IsParagraphInsideGrayShadedRegion(para, grayShadedRegions))
                    {
                        MarkAsGrayPromptContent(para);
                        anchor = para;
                        continue;
                    }
                    inGrayPromptBlock = false;
                    anchor = null;
                    continue;
                }

                MarkAsGrayPromptContent(para);
                anchor = para;
            }
        }

        private static void MarkAsGrayPromptContent(PdfParagraph para)
        {
            para.IsCode = true;
            para.IsGrayPromptContent = true;
            para.IsDiagram = false;
            para.IsTable = false;
        }

        /// <summary>Shaded vector rects that wrap gray System Message / Prompt / Example boxes (either column).</summary>
        private static List<TableMaskRegion> GetGrayPromptShadedRegions(
            IReadOnlyList<TableMaskRegion> diagramRegions, double pageWidth,
            IReadOnlyList<PdfParagraph>? pageList = null)
        {
            if (diagramRegions.Count == 0) return new List<TableMaskRegion>();
            if (pageList != null && pageList.Any(p =>
                    p.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<TableMaskRegion>();
            }
            double center = pageWidth / 2.0;
            double maxColWidth = pageWidth * 0.52;
            var result = new List<TableMaskRegion>();
            foreach (var r in diagramRegions)
            {
                double w = r.X1 - r.X0;
                double h = r.Y1 - r.Y0;
                if (h < 70 || h > 320) continue;

                if (w >= 180 && w <= maxColWidth)
                {
                    double regionCenter = (r.X0 + r.X1) / 2.0;
                    if (regionCenter < center + 8 || regionCenter > center - 8)
                    {
                        result.Add(r);
                    }
                    continue;
                }

                // Merged workflow + gray-box paths (e.g. PentestAgent p6): split by column.
                if (w > maxColWidth)
                {
                    var left = new TableMaskRegion(r.X0, r.Y0, Math.Min(r.X1, center - 5), r.Y1);
                    var right = new TableMaskRegion(Math.Max(r.X0, center + 5), r.Y0, r.X1, r.Y1);
                    foreach (var part in new[] { left, right })
                    {
                        double pw = part.X1 - part.X0;
                        if (pw >= 180 && pw <= maxColWidth && (part.Y1 - part.Y0) >= 70)
                        {
                            result.Add(part);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>Union of vector gray boxes, gray path fills, and clustered gray-prompt paragraph bboxes.</summary>
        private static List<TableMaskRegion> BuildEffectiveGrayMaskRegions(
            UglyToad.PdfPig.Content.Page pigPage,
            IReadOnlyList<TableMaskRegion> diagramMaskRegions,
            IReadOnlyList<PdfParagraph> pageList,
            double pageWidth)
        {
            if (pageList.Any(p =>
                    p.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<TableMaskRegion>();
            }

            var combined = new List<TableMaskRegion>();
            combined.AddRange(GetGrayPromptShadedRegions(diagramMaskRegions, pageWidth, pageList));
            combined.AddRange(GetGrayVectorFillRegions(pigPage));
            combined.AddRange(BuildGrayPromptBoxUnionRegions(pageList, pageWidth));
            combined = MergeOverlappingGrayRegions(combined, pageWidth);
            return FilterSpuriousEffectiveGrayRegions(combined, pageList);
        }

        /// <summary>Drop vector gray boxes that sit on translatable body prose without any gray-prompt paragraph inside.</summary>
        private static List<TableMaskRegion> FilterSpuriousEffectiveGrayRegions(
            List<TableMaskRegion> regions, IReadOnlyList<PdfParagraph> pageList)
        {
            if (regions.Count == 0) return regions;
            var filtered = new List<TableMaskRegion>();
            foreach (var region in regions)
            {
                bool hasGrayPrompt = pageList.Any(p =>
                {
                    return (p.IsGrayPromptContent || IsGrayPromptCodeParagraph(p)) &&
                           ParagraphCenterInsideAnyRegion(p, new[] { region });
                });
                if (hasGrayPrompt)
                {
                    filtered.Add(region);
                    continue;
                }
                bool overlapsBodyProse = pageList.Any(p =>
                    !p.IsBypassed &&
                    !p.IsGrayPromptContent &&
                    !IsGrayPromptCodeParagraph(p) &&
                    (IsTranslatableBodyProse(p) || IsHeadingParagraph(p) || IsTranslatableCalloutProse(p)) &&
                    ParagraphCenterInsideAnyRegion(p, new[] { region }));
                if (!overlapsBodyProse)
                    filtered.Add(region);
            }
            return filtered;
        }

        /// <summary>Detect light-gray filled vector rectangles (prompt box backgrounds).</summary>
        private static List<TableMaskRegion> GetGrayVectorFillRegions(UglyToad.PdfPig.Content.Page pigPage)
        {
            var result = new List<TableMaskRegion>();
            try
            {
                foreach (var path in pigPage.ExperimentalAccess.Paths)
                {
                    var rectOpt = path.GetBoundingRectangle();
                    if (!rectOpt.HasValue) continue;
                    var b = rectOpt.Value;
                    if (b.Width < 50 || b.Height < 20) continue;
                    if (b.Width > pigPage.Width * 0.92 || b.Height > pigPage.Height * 0.92) continue;

                    bool grayFill = TryGetPathGrayFill(path, out double r, out double g, out double blue) &&
                                    IsLightGrayRgb(r, g, blue);
                    if (!grayFill) continue;
                    result.Add(new TableMaskRegion(b.Left, b.Bottom, b.Right, b.Top));
                }
            }
            catch { }
            return result;
        }

        private static bool TryGetPathGrayFill(object path, out double r, out double g, out double b)
        {
            r = g = b = 0;
            try
            {
                var props = path.GetType().GetProperty("Fill",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (props?.GetValue(path) is UglyToad.PdfPig.Graphics.Colors.IColor fill)
                {
                    return TryExtractRgb(fill, out r, out g, out b);
                }
            }
            catch { }
            return false;
        }

        private static bool TryExtractRgb(UglyToad.PdfPig.Graphics.Colors.IColor color, out double r, out double g, out double b)
        {
            r = g = b = 0;
            try
            {
                if (color is UglyToad.PdfPig.Graphics.Colors.RGBColor rgb)
                {
                    r = rgb.R; g = rgb.G; b = rgb.B;
                    return true;
                }
                var rgbProp = color.GetType().GetProperty("RGB");
                if (rgbProp?.GetValue(color) is UglyToad.PdfPig.Graphics.Colors.RGBColor nested)
                {
                    r = nested.R; g = nested.G; b = nested.B;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsLightGrayRgb(double r, double g, double b)
        {
            if (r > 1.5) { r /= 255.0; g /= 255.0; b /= 255.0; }
            return r >= 0.68 && r <= 0.96 && g >= 0.68 && g <= 0.96 && b >= 0.68 && b <= 0.98 &&
                   Math.Abs(r - g) < 0.1 && Math.Abs(g - b) < 0.1;
        }

        /// <summary>Merge flagged gray-prompt paragraphs into contiguous box bboxes per column.</summary>
        private static List<TableMaskRegion> BuildGrayPromptBoxUnionRegions(
            IReadOnlyList<PdfParagraph> paragraphs, double pageWidth, double pad = 6.0)
        {
            double center = pageWidth / 2.0;
            var grayParas = paragraphs
                .Where(p => p.IsGrayPromptContent || IsGrayPromptCodeParagraph(p))
                .ToList();
            if (grayParas.Count == 0) return new List<TableMaskRegion>();

            var result = new List<TableMaskRegion>();
            foreach (bool leftCol in new[] { true, false })
            {
                var colParas = grayParas
                    .Where(p => leftCol
                        ? (p.X0 + p.X1) / 2.0 < center - 5
                        : (p.X0 + p.X1) / 2.0 > center + 5)
                    .OrderByDescending(p => p.Y1)
                    .ToList();
                if (colParas.Count == 0) continue;

                var cluster = new List<PdfParagraph> { colParas[0] };
                for (int i = 1; i < colParas.Count; i++)
                {
                    var prev = cluster[^1];
                    var curr = colParas[i];
                    double gap = prev.Y0 - curr.Y1;
                    if (gap > 55)
                    {
                        result.Add(UnionParagraphBboxes(cluster, 4.0));
                        cluster = new List<PdfParagraph>();
                    }
                    cluster.Add(curr);
                }
                if (cluster.Count > 0)
                    result.Add(UnionParagraphBboxes(cluster, 4.0));
            }
            return result;
        }

        private static TableMaskRegion UnionParagraphBboxes(IReadOnlyList<PdfParagraph> paras, double pad)
        {
            return new TableMaskRegion(
                paras.Min(p => p.X0) - pad,
                paras.Min(p => p.Y0) - pad,
                paras.Max(p => p.X1) + pad,
                paras.Max(p => p.Y1) + pad);
        }

        private static List<TableMaskRegion> MergeOverlappingGrayRegions(List<TableMaskRegion> rawBounds, double pageWidth = 0)
        {
            if (rawBounds.Count <= 1) return rawBounds;
            var merged = new List<TableMaskRegion>();
            var used = new bool[rawBounds.Count];
            double center = pageWidth / 2.0;
            for (int i = 0; i < rawBounds.Count; i++)
            {
                if (used[i]) continue;
                var r = rawBounds[i];
                double x0 = r.X0, y0 = r.Y0, x1 = r.X1, y1 = r.Y1;
                bool rLeftCol = pageWidth <= 0 || (r.X0 + r.X1) / 2.0 < center - 5;
                used[i] = true;
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int j = 0; j < rawBounds.Count; j++)
                    {
                        if (used[j]) continue;
                        var o = rawBounds[j];
                        if (pageWidth > 0)
                        {
                            bool oLeftCol = (o.X0 + o.X1) / 2.0 < center - 5;
                            if (rLeftCol != oLeftCol) continue;
                        }
                        bool closeX = o.X0 <= x1 + 12 && o.X1 >= x0 - 12;
                        bool closeY = o.Y0 <= y1 + 12 && o.Y1 >= y0 - 12;
                        if (closeX && closeY)
                        {
                            x0 = Math.Min(x0, o.X0);
                            y0 = Math.Min(y0, o.Y0);
                            x1 = Math.Max(x1, o.X1);
                            y1 = Math.Max(y1, o.Y1);
                            used[j] = true;
                            changed = true;
                        }
                    }
                }
                merged.Add(new TableMaskRegion(x0, y0, x1, y1));
            }
            return merged;
        }

        /// <summary>Force gray-prompt bypass for any paragraph whose center lies inside gray geometry.</summary>
        private static void MarkAllParagraphsByGrayGeometry(
            List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions, double pageHeight)
        {
            if (grayRegions.Count == 0) return;
            var expanded = ExpandGrayShadedRegions(grayRegions, 2.0);
            foreach (var para in pageList)
            {
                if (IsRunningHeaderOrFooter(para, pageHeight)) continue;
                if (IsFigureTableCaptionParagraph(para)) continue;
                if (IsHeadingParagraph(para) || IsAppendixSectionHeading(para)) continue;
                bool insideGray = ParagraphCenterInsideAnyRegion(para, expanded) ||
                    IsParagraphInsideGrayShadedRegion(para, grayRegions);
                if (IsParagraphInsideAnchoredGrayPromptRegion(para, grayRegions, pageList))
                {
                    MarkAsGrayPromptContent(para);
                    continue;
                }
                if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para)) continue;
                if (insideGray &&
                    (IsGrayPromptBoxParagraph(para) ||
                     IsGrayPromptSubheading(para) ||
                     IsGrayPromptBoxContinuationParagraph(para, null)))
                {
                    MarkAsGrayPromptContent(para);
                }
            }
        }

        /// <summary>Strict geometry test: any mask pixel overlap with gray box blocks Pass 1 white paint.</summary>
        private static bool MaskRectIntersectsAnyGrayRegion(
            double maskX0, double maskY0, double maskX1, double maskY1,
            IReadOnlyList<TableMaskRegion> grayRegions)
        {
            if (grayRegions.Count == 0) return false;
            foreach (var region in ExpandGrayShadedRegions(grayRegions, 16.0))
            {
                double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
                double overlapY = Math.Min(maskY1, region.Y1) - Math.Max(maskY0, region.Y0);
                if (overlapX > 0.5 && overlapY > 0.5)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool MaskRectOverlapsPageOneAuthorBand(
            double maskX0, double maskY0, double maskX1, double maskY1,
            double titleBottom, double abstractTop)
        {
            if (titleBottom <= abstractTop) return false;
            double overlapY = Math.Min(maskY1, titleBottom) - Math.Max(maskY0, abstractTop);
            return overlapY > 0.5;
        }

        private static List<TableMaskRegion> ExpandGrayShadedRegions(
            IReadOnlyList<TableMaskRegion> grayRegions, double inset = 3.0)
        {
            return grayRegions
                .Select(r => new TableMaskRegion(r.X0 - inset, r.Y0 - inset, r.X1 + inset, r.Y1 + inset))
                .ToList();
        }

        /// <summary>Union bbox of flagged gray-prompt paragraphs (covers p7 workflow pages without vector gray rects).</summary>
        private static List<TableMaskRegion> BuildGrayPromptParagraphMaskRegions(
            IReadOnlyList<PdfParagraph> paragraphs, double pad = 2.0)
        {
            var regions = new List<TableMaskRegion>();
            foreach (var para in paragraphs)
            {
                if (!para.IsGrayPromptContent && !IsGrayPromptCodeParagraph(para)) continue;
                regions.Add(new TableMaskRegion(
                    para.X0 - pad, para.Y0 - pad, para.X1 + pad, para.Y1 + pad));
            }
            return regions;
        }

        private static List<TableMaskRegion> CombineGrayMaskRegions(
            IReadOnlyList<TableMaskRegion> shadedRegions,
            IReadOnlyList<TableMaskRegion> paragraphRegions)
        {
            var combined = new List<TableMaskRegion>();
            if (shadedRegions.Count > 0) combined.AddRange(shadedRegions);
            if (paragraphRegions.Count > 0) combined.AddRange(paragraphRegions);
            return combined;
        }

        private static bool MaskRectOverlapsGrayRegions(
            double maskX0, double maskY0, double maskX1, double maskY1,
            IReadOnlyList<TableMaskRegion> grayRegions,
            double pageWidth = 0)
        {
            if (grayRegions.Count == 0) return false;
            var expanded = ExpandGrayShadedRegions(grayRegions, 2.0);
            foreach (var region in expanded)
            {
                if (pageWidth > 0 &&
                    !ParagraphSharesColumnWithRegion(maskX0, maskX1, region, pageWidth, 8.0))
                {
                    continue;
                }
                double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
                double overlapY = Math.Min(maskY1, region.Y1) - Math.Max(maskY0, region.Y0);
                if (overlapX > 0.5 && overlapY > 0.5)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Clip or reject a mask rect so it cannot paint over gray prompt shaded areas.</summary>
        private static bool TryClipMaskBelowGrayRegions(
            ref double maskX0, ref double maskY0, ref double maskX1, ref double maskY1,
            IReadOnlyList<TableMaskRegion> grayRegions, double pageWidth)
        {
            if (grayRegions.Count == 0) return maskY1 > maskY0 + 0.5;
            const double clearance = 4.0;
            foreach (var region in grayRegions.OrderBy(r => r.Y0))
            {
                double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
                if (overlapX < 8.0) continue;

                if (maskY0 >= region.Y0 - 0.5 && maskY1 <= region.Y1 + 0.5)
                {
                    return false;
                }

                if (maskY1 > region.Y0 + 0.5 && maskY0 < region.Y1)
                {
                    maskY1 = Math.Min(maskY1, region.Y0 - clearance);
                }
            }
            return maskY1 > maskY0 + 0.5;
        }

        private static bool ParagraphCenterInsideAnyRegion(
            PdfParagraph para, IReadOnlyList<TableMaskRegion> regions)
        {
            double cx = para.X0 + para.Width / 2;
            double cy = para.Y0 + para.Height / 2;
            foreach (var region in regions)
            {
                if (cx >= region.X0 && cx <= region.X1 && cy >= region.Y0 && cy <= region.Y1)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Paragraph center or majority of letters inside shaded gray box only.</summary>
        private static bool IsParagraphInsideGrayShadedRegion(
            PdfParagraph para, IReadOnlyList<TableMaskRegion> grayRegions)
        {
            if (grayRegions.Count == 0) return false;
            var expanded = ExpandGrayShadedRegions(grayRegions);
            if (ParagraphCenterInsideAnyRegion(para, expanded)) return true;
            return ParagraphLetterOverlapRatio(para, expanded) >= 0.5;
        }

        private static bool IsParagraphInsideAnchoredGrayPromptRegion(
            PdfParagraph para,
            IReadOnlyList<TableMaskRegion> grayRegions,
            IReadOnlyList<PdfParagraph> pageList)
        {
            if (grayRegions.Count == 0) return false;
            foreach (var region in ExpandGrayShadedRegions(grayRegions, 8.0))
            {
                if (!ParagraphCenterInsideAnyRegion(para, new[] { region }) &&
                    ParagraphLetterOverlapRatio(para, new[] { region }) < 0.5)
                {
                    continue;
                }

                bool hasPromptAnchor = pageList.Any(anchor =>
                    anchor != para &&
                    (IsGrayPromptBoxParagraph(anchor) || IsGrayPromptSubheading(anchor)) &&
                    ParagraphCenterInsideAnyRegion(anchor, new[] { region }) &&
                    SharesGrayPromptColumn(anchor, para));
                if (hasPromptAnchor) return true;
            }
            return false;
        }

        private static void MarkGrayPromptContentInShadedRegions(
            List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions)
        {
            if (grayRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (para.IsTable) continue;
                if (IsFigureTableCaptionParagraph(para)) continue;
                if (IsHeadingParagraph(para) || IsAppendixSectionHeading(para)) continue;
                if (IsGrayPromptSubheading(para)) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\.\d+\s")) continue;

                if (IsParagraphInsideGrayShadedRegion(para, grayRegions))
                {
                    if (IsParagraphInsideAnchoredGrayPromptRegion(para, grayRegions, pageList))
                    {
                        MarkAsGrayPromptContent(para);
                        continue;
                    }
                    if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para)) continue;
                    if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\)\s+[A-Za-z]") &&
                        !IsGrayPromptBoxContinuationParagraph(para, null))
                    {
                        continue;
                    }
                    MarkAsGrayPromptContent(para);
                    continue;
                }

                bool overlapsGray = ParagraphOverlapsAnyTableMask(
                    para.X0, para.Y0, para.X1, para.Y1, ExpandGrayShadedRegions(grayRegions), 15.0, 3.0);
                if (overlapsGray && IsGrayPromptBoxContinuationParagraph(para, null))
                {
                    MarkAsGrayPromptContent(para);
                    continue;
                }

                if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para)) continue;

                if (IsGrayPromptSubheading(para) &&
                    IsParagraphInsideGrayShadedRegion(para, grayRegions))
                {
                    MarkAsGrayPromptContent(para);
                    continue;
                }

                if (IsGrayPromptBoxContinuationParagraph(para, null) &&
                    IsParagraphInsideGrayShadedRegion(para, grayRegions))
                {
                    MarkAsGrayPromptContent(para);
                }
            }
        }

        /// <summary>Strip gray/code flags from column body clearly below the shaded vector box bottom.</summary>
        private static void ClearGrayPromptFlagsBelowShadedBottom(
            List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions, double pageWidth)
        {
            if (grayRegions.Count == 0) return;
            double center = pageWidth / 2.0;
            bool leftColumnGrayBox = grayRegions.Any(r => (r.X0 + r.X1) / 2.0 < center - 8);
            if (!leftColumnGrayBox) return;
            double shadedBottom = grayRegions.Where(r => (r.X0 + r.X1) / 2.0 < center - 8).Min(r => r.Y0);
            foreach (var para in pageList)
            {
                if (para.Y1 >= shadedBottom - 6) continue;
                if ((para.X0 + para.X1) / 2.0 >= center - 8) continue;
                if (IsGrayPromptSubheading(para)) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (txt.StartsWith("You should", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Use Nmap", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Use your ", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("LLM:", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("{Information", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("You're", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("You\u2019re", StringComparison.OrdinalIgnoreCase) ||
                    txt.Equals("EX-", StringComparison.OrdinalIgnoreCase) ||
                    txt.EndsWith("AMPLE}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                para.IsGrayPromptContent = false;
                para.IsCode = false;
            }
        }

        private static void ClearGrayPromptContentOutsideShadedRegions(
            List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions)
        {
            foreach (var para in pageList)
            {
                if (!para.IsGrayPromptContent) continue;
                if (IsGrayPromptBoxParagraph(para)) continue;
                if (grayRegions.Count > 0)
                {
                    double shadedBottom = grayRegions.Min(r => r.Y0);
                    if (para.Y1 < shadedBottom - 8 &&
                        !IsGrayPromptBoxContinuationParagraph(para, null) &&
                        !IsGrayPromptSubheading(para))
                    {
                        para.IsGrayPromptContent = false;
                        para.IsCode = false;
                        continue;
                    }
                }
                if (grayRegions.Count > 0 &&
                    !IsParagraphInsideGrayShadedRegion(para, grayRegions) &&
                    !IsGrayPromptBoxContinuationParagraph(para, null) &&
                    !IsGrayPromptSubheading(para))
                {
                    string txt = para.TextWithPlaceholders.Trim();
                    int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    bool multiSentenceBody = IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para) ||
                        (wordCount >= 8 && para.Width > 100 && txt.IndexOf('.') >= 0 && txt.Any(char.IsLower));
                    if (multiSentenceBody)
                    {
                        para.IsGrayPromptContent = false;
                        para.IsCode = false;
                        continue;
                    }
                }
                if (grayRegions.Count > 0 && IsParagraphInsideGrayShadedRegion(para, grayRegions)) continue;
                if (HasNearbyGrayPromptAbove(para, pageList)) continue;
                if (IsHeadingParagraph(para))
                {
                    para.IsGrayPromptContent = false;
                    para.IsCode = false;
                    continue;
                }
                if (IsAppendixSectionHeading(para))
                {
                    para.IsGrayPromptContent = false;
                    para.IsCode = false;
                    continue;
                }
            }
        }

        /// <summary>Body prose must never remain flagged as gray prompt content when outside shaded boxes.</summary>
        private static void ClearTranslatableProseFromGrayPromptFlags(
            List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions)
        {
            foreach (var para in pageList)
            {
                if (!para.IsGrayPromptContent && !para.IsCode) continue;
                if (IsGrayPromptBoxParagraph(para)) continue;
                if (!IsTranslatableBodyProse(para) && !IsTranslatableCalloutProse(para)) continue;
                if (IsSectionIntroProse(para))
                {
                    para.IsGrayPromptContent = false;
                    para.IsCode = false;
                    continue;
                }
                if (IsGrayPromptSubheading(para)) continue;
                if (grayRegions.Count > 0 && IsParagraphInsideGrayShadedRegion(para, grayRegions)) continue;
                if (HasNearbyGrayPromptAbove(para, pageList)) continue;
                if (IsGrayPromptBoxContinuationParagraph(para, null) &&
                    grayRegions.Count > 0 &&
                    ParagraphOverlapsAnyTableMask(
                        para.X0, para.Y0, para.X1, para.Y1,
                        ExpandGrayShadedRegions(grayRegions), 15.0, 3.0))
                {
                    continue;
                }
                if (IsGrayPromptBoxContinuationParagraph(para, null) &&
                    grayRegions.Count > 0 &&
                    IsParagraphInsideGrayShadedRegion(para, grayRegions))
                {
                    continue;
                }
                para.IsGrayPromptContent = false;
                para.IsCode = false;
            }
        }

        /// <summary>Re-apply gray flags on prompt continuations cleared by translatable-prose heuristics (p4/p7/p14).</summary>
        private static void RestoreGrayPromptContinuations(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (para.IsGrayPromptContent) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (txt.EndsWith("AMPLE}", StringComparison.OrdinalIgnoreCase) ||
                    txt.Equals("EX-", StringComparison.OrdinalIgnoreCase))
                {
                    MarkAsGrayPromptContent(para);
                    continue;
                }
                if ((txt.StartsWith("LLM:", StringComparison.OrdinalIgnoreCase) ||
                     txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase) ||
                     txt.StartsWith("{Information", StringComparison.OrdinalIgnoreCase)) &&
                    HasNearbyGrayPromptTitleAbove(para, pageList, 140))
                {
                    MarkAsGrayPromptContent(para);
                    continue;
                }
                if (!IsGrayPromptBoxContinuationParagraph(para, null)) continue;
                if (!HasNearbyGrayPromptAbove(para, pageList, 65)) continue;
                if (IsFigureTableCaptionParagraph(para) || IsHeadingParagraph(para)) continue;
                MarkAsGrayPromptContent(para);
            }
        }

        private static bool HasNearbyGrayPromptTitleAbove(
            PdfParagraph para, List<PdfParagraph> pageList, double maxGap)
        {
            foreach (var other in pageList)
            {
                if (!IsGrayPromptBoxParagraph(other)) continue;
                if (!SharesGrayPromptColumn(para, other)) continue;
                double gap = other.Y0 - para.Y1;
                if (gap >= -2 && gap <= maxGap) return true;
            }
            return false;
        }

        /// <summary>Gray prompt boxes bypass as code, never as diagram; no Pass 1 white masks.</summary>
        private static void FinalizeGrayPromptContentFlags(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (IsHeadingParagraph(para) || IsAppendixSectionHeading(para))
                {
                    para.IsGrayPromptContent = false;
                    if (!IsGrayPromptBoxParagraph(para) && !IsGrayPromptSubheading(para))
                    {
                        para.IsCode = false;
                    }
                    continue;
                }

                if (IsGrayPromptBoxParagraph(para) || IsGrayPromptSubheading(para))
                    para.IsDiagram = false;

                if (!para.IsGrayPromptContent) continue;
                para.IsCode = true;
                para.IsDiagram = false;
                para.IsTable = false;
            }
        }

        private static bool IsGrayPromptCodeParagraph(PdfParagraph para)
        {
            if (para.IsGrayPromptContent) return true;
            if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para) ||
                IsHeadingParagraph(para) || IsAppendixSectionHeading(para))
            {
                return false;
            }
            if (IsGrayPromptBoxParagraph(para) || IsGrayPromptSubheading(para))
            {
                return true;
            }
            return IsGrayPromptBoxContinuationParagraph(para, null);
        }

        private static bool IsMisclassifiedPromptCode(PdfParagraph para)
        {
            if (!para.IsCode) return false;
            if (IsGrayPromptCodeParagraph(para)) return false;
            if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para) || IsHeadingParagraph(para) ||
                IsAppendixSectionHeading(para))
            {
                return true;
            }
            string txt = para.TextWithPlaceholders.Trim();
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= 8 && para.Width > 80 && txt.IndexOf('.') >= 0) return true;
            if (wordCount >= 6 && para.Height >= 14 && txt.IndexOf('.') >= 0 && txt.Any(char.IsLower))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Undo false-positive IsCode from loose prompt heuristics on body prose and figure labels.
        /// </summary>
        private static void ClearMisclassifiedCodeFlags(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (!para.IsCode) continue;
                if (IsMisclassifiedPromptCode(para))
                {
                    if (para.IsGrayPromptContent) continue;
                    para.IsCode = false;
                    continue;
                }
                if (para.IsDiagram && !IsGrayPromptCodeParagraph(para))
                {
                    para.IsCode = false;
                    continue;
                }
                if (IsLikelyChartLabel(para) && !IsGrayPromptCodeParagraph(para))
                {
                    para.IsCode = false;
                }
            }
        }

        /// <summary>
        /// Trim bloated bottom gutter from tall merged diagram masks so column body text
        /// below workflow figures (PentestAgent p5 §3.1) is not skip-rendered.
        /// </summary>
        private static List<TableMaskRegion> ShrinkDiagramMaskRegionsBottomGutter(List<TableMaskRegion> regions)
        {
            if (regions.Count == 0) return regions;
            var trimmed = new List<TableMaskRegion>(regions.Count);
            foreach (var r in regions)
            {
                double h = r.Y1 - r.Y0;
                if (h > 100)
                {
                    double trim = Math.Min(55, h * 0.28);
                    trimmed.Add(new TableMaskRegion(r.X0, r.Y0 + trim, r.X1, r.Y1));
                }
                else
                {
                    trimmed.Add(r);
                }
            }
            return trimmed;
        }

        /// <summary>
        /// Cap tall merged diagram bounds so translated figure captions below diagrams are not skip-rendered
        /// (PentestAgent p7 Fig. 4–6).
        /// </summary>
        private static List<TableMaskRegion> CapDiagramMaskBelowFigureCaptions(
            List<TableMaskRegion> regions, IReadOnlyList<PdfParagraph> pageList, double pageWidth)
        {
            if (regions.Count == 0) return regions;
            var captions = pageList
                .Where(p => IsFigureTableCaptionParagraph(p))
                .Where(p =>
                {
                    string t = p.TextWithPlaceholders.Trim();
                    return t.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                           t.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) ||
                           t.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            if (captions.Count < 2) return regions;

            double center = pageWidth / 2.0;
            var result = new List<TableMaskRegion>(regions.Count);
            foreach (var r in regions)
            {
                bool rightCol = (r.X0 + r.X1) / 2.0 >= center - 8;
                var colCaptions = captions
                    .Where(c => (c.X0 + c.Width / 2) >= center - 8 == rightCol)
                    .ToList();
                if (colCaptions.Count == 0)
                {
                    result.Add(r);
                    continue;
                }

                double capY0 = colCaptions.Min(c => c.Y0) - 12;
                if (r.Y0 < capY0 && capY0 < r.Y1 - 40)
                {
                    result.Add(new TableMaskRegion(r.X0, capY0, r.X1, r.Y1));
                }
                else
                {
                    result.Add(r);
                }
            }
            return result;
        }

        /// <summary>
        /// Skip white masks / translated overlay only when a paragraph is a diagram label or
        /// substantially inside a figure region — not when column body text barely touches the gutter.
        /// </summary>
        private static bool ShouldProtectDiagramRegionFromParagraph(
            PdfParagraph para, IReadOnlyList<TableMaskRegion> diagramMaskRegions,
            IReadOnlyList<PdfParagraph>? pageParagraphs = null, double pageWidth = 0)
        {
            var protectRegions = pageParagraphs != null && pageWidth > 0
                ? GetFigureClipRegions(pageParagraphs, diagramMaskRegions, pageWidth)
                : diagramMaskRegions;
            if (protectRegions.Count == 0) return false;
            if (para.IsDiagram) return true;
            if (para.IsCode && IsGrayPromptCodeParagraph(para)) return true;
            if (IsFigureTableCaptionParagraph(para)) return false;
            if (IsGrayPromptBoxParagraph(para)) return false;
            // Body prose, callouts, and section headings always receive Pass 1 mask + Pass 2 overlay.
            if (IsTranslatableBodyProse(para) || IsTranslatableCalloutProse(para) ||
                IsHeadingParagraph(para) || IsAppendixSectionHeading(para))
            {
                return false;
            }

            string txt = para.TextWithPlaceholders.Trim();
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (!IsLikelyChartLabel(para) && wordCount >= 8 && para.Width >= 100 && txt.Any(char.IsLower))
            {
                return false;
            }
            if (!IsLikelyChartLabel(para) && para.Width >= 120 && txt.Length >= 25 && txt.Any(char.IsLower))
            {
                return false;
            }

            double letterRatio = ParagraphLetterOverlapRatio(para, protectRegions);
            if (letterRatio >= 0.4) return true;

            if (IsLikelyChartLabel(para))
            {
                foreach (var region in protectRegions)
                {
                    if (pageWidth > 0 &&
                        !ParagraphSharesColumnWithRegion(para.X0, para.X1, region, pageWidth, 15.0))
                    {
                        continue;
                    }
                    if (ParagraphOverlapsTableMask(para.X0, para.Y0, para.X1, para.Y1,
                            region.X0, region.Y0, region.X1, region.Y1, 15.0, 3.0))
                    {
                        return true;
                    }
                }
            }

            if (txt.Length <= 80)
            {
                double cx = para.X0 + para.Width / 2;
                double cy = para.Y0 + para.Height / 2;
                foreach (var region in protectRegions)
                {
                    if (pageWidth > 0 &&
                        !ParagraphSharesColumnWithRegion(para.X0, para.X1, region, pageWidth, 15.0))
                    {
                        continue;
                    }
                    if (cx >= region.X0 && cx <= region.X1 && cy >= region.Y0 && cy <= region.Y1)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ClearDiagramFlagOnRunningHeaders(List<PdfParagraph> pageList, double pageHeight)
        {
            foreach (var para in pageList)
            {
                if (!IsRunningHeaderOrFooter(para, pageHeight)) continue;
                para.IsDiagram = false;
                para.IsBypassed = para.IsCode || para.IsOnlyMath ||
                                  string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                                  IsEquationParagraph(para) || IsTableParagraph(para) || para.IsTable;
            }
        }

        /// <summary>Bar-chart legend/axis labels misclassified as table cells on chart-heavy pages.</summary>
        private static void ReclassifyChartLabelsMisclassifiedAsTable(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (!para.IsTable) continue;
                if (IsFigureTableCaptionParagraph(para)) continue;

                double letterRatio = ParagraphLetterOverlapRatio(para, diagramRegions);
                bool inDiagram = letterRatio >= 0.25 || OverlapsAnyRegion(para, diagramRegions);
                if (!inDiagram) continue;

                if (IsLikelyChartLabel(para) || para.Height <= 60 || letterRatio >= 0.4)
                {
                    para.IsTable = false;
                    para.IsDiagram = true;
                }
            }
        }

        private static bool OverlapsAnyRegion(PdfParagraph para, IReadOnlyList<TableMaskRegion> regions)
        {
            double cx = para.X0 + para.Width / 2;
            double cy = para.Y0 + para.Height / 2;
            foreach (var region in regions)
            {
                bool intersectX = para.X0 <= region.X1 && para.X1 >= region.X0;
                bool intersectY = para.Y0 <= region.Y1 && para.Y1 >= region.Y0;
                if (intersectX && intersectY) return true;
                if (cx >= region.X0 && cx <= region.X1 && cy >= region.Y0 && cy <= region.Y1) return true;
            }
            return false;
        }

        private static double RenderParagraph(XGraphics gfx, PdfParagraph para, string targetFontName, bool measureOnly = false)
        {
            double pageHeight = gfx.PageSize.Height;
            double paragraphX = para.X0;
            double paragraphY = pageHeight - para.Y1;
            double paragraphWidth = para.Width;
            double paragraphHeight = para.Height;

            string text = (para.TranslatedText ?? "").Replace('∗', '*');
            text = text.Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "").Replace("\uFEFF", "");
            text = FormulaLiteralCleaner.RemoveDuplicateFormulaLiterals(text, para.Formulas);
            var tokens = TokenizeTranslatedText(text);

            double fontSize = para.AverageFontSize;
            string fontNameForPara = targetFontName;
            if (para.IsCode)
            {
                fontNameForPara = "Courier New";
            }
            else if (para.IsBypassed)
            {
                if (text.Any(FontUtilities.IsCjkCharacter))
                {
                    fontNameForPara = targetFontName;
                }
                else
                {
                    fontNameForPara = "Times New Roman";
                    if (para.AllLetters.Count > 0)
                    {
                        string fn = para.AllLetters[0].FontName.ToLowerInvariant();
                        if (fn.Contains("times") || fn.Contains("serif") || fn.Contains("liberation"))
                            fontNameForPara = "Times New Roman";
                        else if (fn.Contains("arial") || fn.Contains("helvetica") || fn.Contains("sans"))
                            fontNameForPara = "Arial";
                        else if (fn.Contains("courier") || fn.Contains("mono") || fn.Contains("consolas"))
                            fontNameForPara = "Courier New";
                    }
                }
            }
            XFontStyleEx fontStyle = XFontStyleEx.Regular;
            // Translated CJK must use regular kaiu.ttf; bold/italic from source (e.g. NimbusRom Medi) maps to
            // simsunb.ttf via ClickraFontResolver and produces SimSun-ExtB garbled overlays.
            if (!para.IsBypassed && !para.IsCode && FontUtilities.IsCjkTranslationFont(fontNameForPara))
            {
                fontStyle = XFontStyleEx.Regular;
            }
            else if (para.IsBold || IsHeadingParagraph(para))
            {
                fontStyle = para.IsItalic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold;
            }
            else
            {
                fontStyle = para.IsItalic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
            }
            XFont mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
            XBrush brush = XBrushes.Black;

            // Handle rotations (90, 180, 270)
            bool isRotated = false;
            double layoutWidth = paragraphWidth;
            if (!isRotated && IsHeadingParagraph(para))
            {
                double pageCenter = gfx.PageSize.Width / 2.0;
                double maxBoundary = gfx.PageSize.Width - 54.0; // Default right margin
                
                // If it's in the left column, limit expansion to the middle of the page
                if (para.OriginalX1 <= pageCenter + 10.0)
                {
                    maxBoundary = pageCenter - 10.0;
                }

                double remainingWidth = maxBoundary - paragraphX;
                if (remainingWidth > layoutWidth)
                {
                    layoutWidth = remainingWidth;
                }
            }
            XGraphicsState? state = null;
            string dirStr = para.TextDirection?.ToString() ?? "";

            if (dirStr == "Rotate270")
            {
                double startX = para.X0;
                double startY = pageHeight - para.Y0;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(-90);
                layoutWidth = para.Height;
                isRotated = true;
            }
            else if (dirStr == "Rotate90")
            {
                double startX = para.X1;
                double startY = pageHeight - para.Y1;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(90);
                layoutWidth = para.Height;
                isRotated = true;
            }
            else if (dirStr == "Rotate180")
            {
                double startX = para.X1;
                double startY = pageHeight - para.Y0;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(180);
                layoutWidth = paragraphWidth;
                isRotated = true;
            }
            List<PdfLayoutRow> rows = LayoutParagraph(tokens, mainFont, para.Formulas, layoutWidth, fontSize, para.AverageFontSize, gfx);

            // Compute dynamic line spacing
            double lineSpacingMultiplier = 1.35; // Default CJK line height
            if (targetFontName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
            {
                lineSpacingMultiplier = 1.2;
            }
            if (ReferenceSectionDetector.IsReferenceParagraph(para))
            {
                lineSpacingMultiplier = 1.15;
            }
            double lineHeight = fontSize * lineSpacingMultiplier;

            double limitHeight = isRotated ? para.Width : paragraphHeight;
            double totalHeight = rows.Count * lineHeight;
            
            bool disableScaling = (rows.Count <= 1) || IsHeadingParagraph(para);
            if (totalHeight > limitHeight && !disableScaling)
            {
                double requiredLineSpacingMultiplier = limitHeight / (rows.Count * fontSize);
                if (requiredLineSpacingMultiplier >= 1.0)
                {
                    lineSpacingMultiplier = requiredLineSpacingMultiplier;
                    lineHeight = fontSize * lineSpacingMultiplier;
                }
                else
                {
                    double scale = limitHeight / totalHeight;
                    scale = Math.Max(0.8, scale);
                    fontSize *= scale;
                    mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
                    lineHeight = fontSize * lineSpacingMultiplier;
                    rows = LayoutParagraph(tokens, mainFont, para.Formulas, layoutWidth, fontSize, para.AverageFontSize, gfx);
                }
            }

            // Actual rendered height = number of rows × line height
            double renderedHeight = rows.Count * lineHeight;

            // In measure-only mode, skip all drawing and just return the height
            if (measureOnly)
            {
                if (state != null) gfx.Restore(state);
                return renderedHeight;
            }

            double currentY = isRotated ? fontSize : (paragraphY + fontSize);
            var renderedChars = new List<RenderedChar>();

            // Clip to prevent horizontal overflow into adjacent columns; vertical clip uses rendered height
            // so multi-line translations are not cut when Chinese text needs more rows than the original English.
            XGraphicsState? clipState = null;
            if (!isRotated)
            {
                clipState = gfx.Save();
                double clipX = paragraphX - 1.5;
                double clipY = paragraphY - 1.5;
                double clipW = layoutWidth + 3.0;
                double clipH = Math.Max(paragraphHeight, renderedHeight) + lineHeight * 0.4 + 4.0;
                gfx.IntersectClip(new XRect(clipX, clipY, clipW, clipH));
            }

            foreach (var row in rows)

            {
                double rowWidth = row.Elements.Sum(e => e.Width);
                double startX = paragraphX;
                if (isRotated)
                {
                    startX = 0;
                    if (para.Alignment == PdfParagraph.TextAlignment.Center) startX = (layoutWidth - rowWidth) / 2;
                    else if (para.Alignment == PdfParagraph.TextAlignment.Right) startX = layoutWidth - rowWidth;
                }
                else
                {
                    if (para.Alignment == PdfParagraph.TextAlignment.Center) startX = paragraphX + (paragraphWidth - rowWidth) / 2;
                    else if (para.Alignment == PdfParagraph.TextAlignment.Right) startX = paragraphX + (paragraphWidth - rowWidth);
                }

                double currentX = startX;
                int idx = 0;
                while (idx < row.Elements.Count)
                {
                    var element = row.Elements[idx];
                    if (element.IsFormula && element.FormulaId >= 0 && element.FormulaId < para.Formulas.Count)
                    {
                        var formula = para.Formulas[element.FormulaId];
                        double scale = fontSize / para.AverageFontSize;

                        bool hasMono = formula.Letters.Any(l => FontUtilities.IsMonospaceFont(l.FontName));
                        double formulaScale = scale;
                        if (hasMono)
                        {
                            formulaScale *= 1.0;
                        }

                        if (FontUtilities.ShouldMergeFormula(formula, para.AverageFontSize))
                        {
                            string mergedText = string.Join("", formula.Letters.Select(l => l.Value));
                            double fSize = formula.Letters[0].FontSize * formulaScale;
                            
                            string fontToUse = formula.Letters[0].FontName;
                            foreach (var l in formula.Letters)
                            {
                                if (FontUtilities.IsMonospaceFont(l.FontName))
                                {
                                    fontToUse = l.FontName;
                                    break;
                                }
                            }
                            
                            XFont mathFont = FontUtilities.GetMathFont(fontToUse, fSize);

                            double avgY = formula.Letters.Average(l => l.RelativeY);
                            double my = currentY - avgY * formulaScale - (fontSize * 0.15);

                            string normText = FontUtilities.NormalizeMathValue(mergedText.Normalize(NormalizationForm.FormKD));
                            gfx.DrawString(normText, mathFont, brush, currentX, my);
                            
                            double offset = 0;
                            for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                            {
                                char ch = normText[cIdx];
                                double mChW = gfx.MeasureString(ch.ToString(), mathFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = ch,
                                    Left = currentX + offset,
                                    Right = currentX + offset + mChW,
                                    Bottom = pageHeight - my - fSize * 0.15,
                                    Top = pageHeight - my + fSize * 0.85
                                });
                                offset += mChW;
                            }
                        }
                        else
                        {
                            foreach (var ml in formula.Letters)
                            {
                                double fSize = ml.FontSize * formulaScale;
                                XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);

                                double mx = currentX + ml.RelativeX * formulaScale;
                                // Align math letter baseline with CJK baseline by shifting up slightly instead of down
                                double my = currentY - ml.RelativeY * formulaScale - (fontSize * 0.15);

                                string drawVal = FontUtilities.NormalizeMathValue(ml.Value.Normalize(NormalizationForm.FormKD));
                                if (drawVal.Length == 1 && FontUtilities.IsMathOrGreekCharacter(drawVal[0]))
                                {
                                    mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                                }

                                gfx.DrawString(drawVal, mathFont, brush, mx, my);
                                
                                double offset = 0;
                                for (int cIdx = 0; cIdx < drawVal.Length; cIdx++)
                                {
                                    char ch = drawVal[cIdx];
                                    double mlChW = gfx.MeasureString(ch.ToString(), mathFont).Width;
                                    renderedChars.Add(new RenderedChar
                                    {
                                        Character = ch,
                                        Left = mx + offset,
                                        Right = mx + offset + mlChW,
                                        Bottom = pageHeight - my - fSize * 0.15,
                                        Top = pageHeight - my + fSize * 0.85
                                    });
                                    offset += mlChW;
                                }
                            }
                        }
                        currentX += element.Width;
                        idx++;
                    }
                    else if (element.IsFormula)
                    {
                        // Defensive: LayoutParagraph should demote invalid {vN}, but render as text if not.
                        string normText = FontUtilities.NormalizeMathValue(element.Text.Normalize(NormalizationForm.FormKD));
                        gfx.DrawString(normText, mainFont, brush, currentX, currentY);
                        double offset = 0;
                        for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                        {
                            char ch = normText[cIdx];
                            double tChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                            renderedChars.Add(new RenderedChar
                            {
                                Character = ch,
                                Left = currentX + offset,
                                Right = currentX + offset + tChW,
                                Bottom = pageHeight - currentY - fontSize * 0.15,
                                Top = pageHeight - currentY + fontSize * 0.85
                            });
                            offset += tChW;
                        }
                        currentX += element.Width;
                        idx++;
                    }
                    else
                    {
                        var sbMerged = new StringBuilder();
                        double textStartX = currentX;
                        double textWidth = 0;
                        while (idx < row.Elements.Count && !row.Elements[idx].IsFormula)
                        {
                            var elem = row.Elements[idx];
                            if (elem.Text.Length == 1 && FontUtilities.IsLatinExtendedOrSymbol(elem.Text[0]))
                            {
                                if (sbMerged.Length > 0)
                                {
                                    string normText = FontUtilities.NormalizeMathValue(sbMerged.ToString().Normalize(NormalizationForm.FormKD));
                                    gfx.DrawString(normText, mainFont, brush, textStartX, currentY);
                                    
                                    double offset = 0;
                                    for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                                    {
                                        char ch = normText[cIdx];
                                        double tChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                                        renderedChars.Add(new RenderedChar
                                        {
                                            Character = ch,
                                            Left = textStartX + offset,
                                            Right = textStartX + offset + tChW,
                                            Bottom = pageHeight - currentY - fontSize * 0.15,
                                            Top = pageHeight - currentY + fontSize * 0.85
                                        });
                                        offset += tChW;
                                    }
                                    sbMerged.Clear();
                                }
                                char c = elem.Text[0];
                                string fallbackFontName;
                                if (c >= 0x0080 && c <= 0x024F)
                                {
                                    fallbackFontName = mainFont.FontFamily.Name.Contains("Courier") ? "Courier New" : "Arial";
                                }
                                else
                                {
                                    fallbackFontName = "Segoe UI Symbol";
                                }
                                XFont fallbackFont = new XFont(fallbackFontName, mainFont.Size, mainFont.Style);
                                string normChar = FontUtilities.NormalizeMathValue(elem.Text.Normalize(NormalizationForm.FormKD));
                                gfx.DrawString(normChar, fallbackFont, brush, currentX, currentY);
                                
                                double fChW = gfx.MeasureString(normChar, fallbackFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = normChar[0],
                                    Left = currentX,
                                    Right = currentX + fChW,
                                    Bottom = pageHeight - currentY - fontSize * 0.15,
                                    Top = pageHeight - currentY + fontSize * 0.85
                                });
                                
                                textStartX = currentX + elem.Width;
                            }
                            else
                            {
                                sbMerged.Append(elem.Text);
                            }
                            textWidth += elem.Width;
                            currentX += elem.Width;
                            idx++;
                        }
                        if (sbMerged.Length > 0)
                        {
                            string normText = FontUtilities.NormalizeMathValue(sbMerged.ToString().Normalize(NormalizationForm.FormKD));
                            gfx.DrawString(normText, mainFont, brush, textStartX, currentY);
                            
                            double offset = 0;
                            for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                            {
                                char ch = normText[cIdx];
                                double eChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = ch,
                                    Left = textStartX + offset,
                                    Right = textStartX + offset + eChW,
                                    Bottom = pageHeight - currentY - fontSize * 0.15,
                                    Top = pageHeight - currentY + fontSize * 0.85
                                });
                                offset += eChW;
                            }
                        }
                    }
                }
                currentY += lineHeight;
            }

            // Restore clipping state
            if (clipState != null)
            {
                gfx.Restore(clipState);
            }

            if (state != null)
            {
                gfx.Restore(state);
            }

            // Align annotations
            if (!isRotated && para.Annotations.Count > 0 && renderedChars.Count > 0)
            {
                foreach (var annotInfo in para.Annotations)
                {
                    try
                    {
                        var matched = FindAnnotationCharacters(
                            renderedChars,
                            annotInfo.Text,
                            annotInfo.OccurrenceIndex,
                            annotInfo.RelCenterX,
                            annotInfo.RelCenterY,
                            annotInfo.RelWidth,
                            para.X0,
                            para.Y0,
                            para.Width,
                            para.Height);
                        if (matched != null && matched.Count > 0)
                        {
                            double minLeft = matched.Min(rc => rc.Left);
                            double maxRight = matched.Max(rc => rc.Right);
                            double minBottom = matched.Min(rc => rc.Bottom);
                            double maxTop = matched.Max(rc => rc.Top);

                            double paddingX = 1.0;
                            double paddingY = 1.5;

                            annotInfo.PdfAnnotation.Rectangle = new PdfRectangle(
                                new XPoint(minLeft - paddingX, minBottom - paddingY),
                                new XPoint(maxRight + paddingX, maxTop + paddingY)
                            );
                        }
                        // else: keep original annotation rect (avoid bad spatial fallback)
                    }
                    catch { }
                }
            }

            return renderedHeight;
        }

        private static List<string> TokenizeTranslatedText(string text)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            int i = 0;
            int len = text.Length;
            while (i < len)
            {
                if (text[i] == '{' && i + 2 < len && text[i + 1] == 'v')
                {
                    int j = i;
                    while (j < len && text[j] != '}') j++;
                    if (j < len && text[j] == '}')
                    {
                        if (sb.Length > 0)
                        {
                            list.Add(sb.ToString());
                            sb.Clear();
                        }
                        list.Add(text.Substring(i, j - i + 1));
                        i = j + 1;
                        continue;
                    }
                }

                char c = text[i];
                if (c == '\n' || c == '\r')
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add("\n");
                    if (c == '\r' && i + 1 < len && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    i++;
                    continue;
                }

                if (FontUtilities.IsCjkCharacter(c) || FontUtilities.IsLatinExtendedOrSymbol(c))
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add(c.ToString());
                    i++;
                    continue;
                }

                if (c == ' ')
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add(" ");
                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        private static List<PdfLayoutRow> LayoutParagraph(List<string> tokens, XFont font, List<MathFormula> formulas, double maxWidth, double fontSize, double averageFontSize, XGraphics gfx)
        {
            var rows = new List<PdfLayoutRow>();
            var currentRow = new PdfLayoutRow();
            double currentX = 0;

            foreach (var token in tokens)
            {
                if (token == "\n")
                {
                    rows.Add(currentRow);
                    currentRow = new PdfLayoutRow();
                    currentX = 0;
                    continue;
                }

                bool isFormula = token.StartsWith("{v") && token.EndsWith("}");
                double width = 0;
                int formulaId = -1;

                if (isFormula)
                {
                    if (int.TryParse(token.Substring(2, token.Length - 3), out formulaId) && formulaId >= 0 && formulaId < formulas.Count)
                    {
                        var formula = formulas[formulaId];
                        double formulaScale = fontSize / averageFontSize;
                        bool hasMono = formula.Letters.Any(l => FontUtilities.IsMonospaceFont(l.FontName));
                        if (hasMono)
                        {
                            formulaScale *= 1.0;
                        }
                        width = formula.Width * formulaScale;
                    }
                    else
                    {
                        // Placeholder {vN} without a matching formula (e.g. CCS/footnote body text) — render as text.
                        isFormula = false;
                        formulaId = -1;
                        width = gfx.MeasureString(FontUtilities.NormalizeMathValue(token), font).Width;
                    }
                }
                else
                {
                    if (token == " ")
                    {
                        width = gfx.MeasureString(" ", font).Width;
                    }
                    else if (token.Length == 1 && FontUtilities.IsLatinExtendedOrSymbol(token[0]))
                    {
                        char c = token[0];
                        string fontName;
                        if (c >= 0x0080 && c <= 0x024F)
                        {
                            fontName = font.FontFamily.Name.Contains("Courier") ? "Courier New" : "Arial";
                        }
                        else
                        {
                            fontName = "Segoe UI Symbol";
                        }
                        XFont fallbackFont = new XFont(fontName, font.Size, font.Style);
                        width = gfx.MeasureString(FontUtilities.NormalizeMathValue(token), fallbackFont).Width;
                    }
                    else
                    {
                        width = gfx.MeasureString(FontUtilities.NormalizeMathValue(token), font).Width;
                    }
                }
                
                // If single token is wider than maxWidth, split at URL-friendly breakpoints
                if (width > maxWidth && !isFormula && token.Length > 1 && token != " ")
                {
                    // Try to split the token at URL/path-friendly characters
                    var breakChars = new char[] { '/', '-', '.', '_', '=' };
                    var subTokens = new List<string>();
                    var sb2 = new System.Text.StringBuilder();
                    foreach (char ch in token)
                    {
                        if (Array.IndexOf(breakChars, ch) >= 0)
                        {
                            sb2.Append(ch);
                            subTokens.Add(sb2.ToString());
                            sb2.Clear();
                        }
                        else
                        {
                            sb2.Append(ch);
                        }
                    }
                    if (sb2.Length > 0) subTokens.Add(sb2.ToString());

                    if (subTokens.Count > 1)
                    {
                        foreach (var sub in subTokens)
                        {
                            double subWidth = gfx.MeasureString(FontUtilities.NormalizeMathValue(sub), font).Width;
                            if (currentX + subWidth > maxWidth && currentRow.Elements.Count > 0)
                            {
                                rows.Add(currentRow);
                                currentRow = new PdfLayoutRow();
                                currentX = 0;
                            }
                            currentRow.Elements.Add(new PdfLayoutElement { Text = sub, IsFormula = false, FormulaId = -1, Width = subWidth });
                            currentX += subWidth;
                        }
                        continue;
                    }
                }

                if (currentX + width > maxWidth && currentRow.Elements.Count > 0)
                {
                    rows.Add(currentRow);
                    currentRow = new PdfLayoutRow();
                    currentX = 0;
                    if (token == " ") continue;
                }

                currentRow.Elements.Add(new PdfLayoutElement
                {
                    Text = token,
                    IsFormula = isFormula,
                    FormulaId = formulaId,
                    Width = width
                });
                currentX += width;
            }

            if (currentRow.Elements.Count > 0)
            {
                rows.Add(currentRow);
            }
 
            return rows;
        }

        private static bool HasColumnGap(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line, double minGap = 20.0)
        {
            if (line == null || line.Words.Count <= 1) return false;
            var sortedWords = line.Words.OrderBy(w => w.BoundingBox.Left).ToList();
            for (int i = 0; i < sortedWords.Count - 1; i++)
            {
                double gap = sortedWords[i + 1].BoundingBox.Left - sortedWords[i].BoundingBox.Right;
                if (gap >= minGap) return true;
            }
            return false;
        }

        private static bool IsLineInLeftColumn(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line, double pageWidth)
        {
            double center = pageWidth / 2.0;
            double lineCenter = (line.BoundingBox.Left + line.BoundingBox.Right) / 2.0;
            return lineCenter < center;
        }

        private static bool CharEqualsNormalized(char c1, char c2)
        {
            if (c1 == c2) return true;
            if (char.ToLowerInvariant(c1) == char.ToLowerInvariant(c2)) return true;
            if ((c1 == '-' || c1 == '–' || c1 == '—') && (c2 == '-' || c2 == '–' || c2 == '—')) return true;
            return false;
        }

        private static int GetOccurrenceIndex(List<PdfLetter> allLetters, List<PdfLetter> targetLetters, string searchText)
        {
            if (allLetters == null || targetLetters == null || string.IsNullOrEmpty(searchText)) return 0;
            
            var occurrences = new List<int>();
            for (int i = 0; i <= allLetters.Count - searchText.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < searchText.Length; j++)
                {
                    if (allLetters[i + j].Value.Length == 0 || !CharEqualsNormalized(allLetters[i + j].Value[0], searchText[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    occurrences.Add(i);
                }
            }
            
            if (occurrences.Count <= 1) return 0;
            
            double targetAvgIndex = targetLetters.Average(tl => allLetters.IndexOf(tl));
            int bestIdx = 0;
            double minDist = double.MaxValue;
            for (int k = 0; k < occurrences.Count; k++)
            {
                double occurrenceAvgIndex = occurrences[k] + (searchText.Length - 1) / 2.0;
                double dist = Math.Abs(occurrenceAvgIndex - targetAvgIndex);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIdx = k;
                }
            }
            return bestIdx;
        }

        private static int ScoreAnnotationParagraph(
            PdfParagraph para,
            List<PdfLetter> overlappingLetters,
            double annotCenterX,
            double annotCenterY)
        {
            int score = overlappingLetters.Count;
            bool centerInside = annotCenterX >= para.X0 && annotCenterX <= para.X1 &&
                                annotCenterY >= para.Y0 && annotCenterY <= para.Y1;
            if (centerInside) score += 1000;
            if (para.IsBypassed || para.IsCode) score += 2000;
            if (!para.IsBypassed && !para.IsCode && overlappingLetters.Count <= 4) score -= 300;
            return score;
        }

        private static List<RenderedChar> FindAnnotationCharacters(
            List<RenderedChar> renderedChars,
            string searchText,
            int occurrenceIdx,
            double relCenterX,
            double relCenterY,
            double relWidth,
            double paraX0,
            double paraY0,
            double paraWidth,
            double paraHeight)
        {
            if (renderedChars == null || renderedChars.Count == 0) return null;

            var cleanRendered = renderedChars.Where(rc => !char.IsWhiteSpace(rc.Character)).ToList();
            if (cleanRendered.Count == 0) return null;

            double targetPdfX = paraX0 + relCenterX * paraWidth;
            double targetPdfY = paraY0 + relCenterY * paraHeight;

            string figureDigits = new string(searchText.Where(char.IsDigit).ToArray());
            if (figureDigits.Length > 0 && figureDigits.Length <= 2)
            {
                bool includeParen = searchText.Contains(')');
                var figureOccurrences = FindFigureRefOccurrences(cleanRendered, figureDigits, includeParen);
                if (figureOccurrences.Count > 0)
                {
                    return PickOccurrenceBySpatialPosition(
                        cleanRendered, figureOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                }

                if (figureDigits.Length == 1)
                {
                    var looseFigure = FindLooseFigureDigitOccurrences(cleanRendered, figureDigits);
                    if (looseFigure.Count > 0)
                    {
                        return PickOccurrenceBySpatialPosition(
                            cleanRendered, looseFigure, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                    }

                    var digitOccurrences = FindTextOccurrences(cleanRendered, figureDigits);
                    if (digitOccurrences.Count > 0)
                    {
                        return PickOccurrenceBySpatialPosition(
                            cleanRendered, digitOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                    }
                }
            }

            var searchPatterns = BuildAnnotationSearchPatterns(searchText);
            foreach (var pattern in searchPatterns)
            {
                var occurrences = FindTextOccurrences(cleanRendered, pattern);
                if (occurrences.Count > 0)
                {
                    bool preferVertical = pattern.StartsWith("圖", StringComparison.Ordinal) ||
                        pattern.StartsWith(":圖", StringComparison.Ordinal) ||
                        pattern.StartsWith("即圖", StringComparison.Ordinal) ||
                        pattern.StartsWith("表", StringComparison.Ordinal) ||
                        IsRomanNumeralPattern(pattern);
                    return PickOccurrenceBySpatialPosition(
                        cleanRendered, occurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: preferVertical);
                }
            }

            string romanSection = ExtractRomanSectionNumeral(searchText);
            if (!string.IsNullOrEmpty(romanSection))
            {
                var sectionOccurrences = FindSectionRomanOccurrences(cleanRendered, romanSection);
                if (sectionOccurrences.Count > 0)
                {
                    return PickOccurrenceBySpatialPosition(
                        cleanRendered, sectionOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                }
            }

            var spatial = MapRenderedCharsBySpatialPosition(cleanRendered, targetPdfX, targetPdfY, relWidth, paraWidth);
            if (spatial != null && spatial.Count > 0)
            {
                double cx = spatial.Average(rc => (rc.Left + rc.Right) / 2.0);
                double cy = spatial.Average(rc => (rc.Bottom + rc.Top) / 2.0);
                double dx = cx - targetPdfX;
                double dy = cy - targetPdfY;
                if (Math.Sqrt(dx * dx + dy * dy) <= Math.Max(24.0, paraWidth * 0.15))
                {
                    return spatial;
                }
            }

            return null;
        }

        private static bool IsRomanNumeralPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            string stripped = pattern.TrimStart('第').Trim();
            return stripped.Length >= 1 && stripped.Length <= 6 &&
                stripped.All(c => "IVXLCDMivxlcdm".Contains(c));
        }

        private static string ExtractRomanSectionNumeral(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return "";

            string clean = new string(searchText.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.Length == 0) return "";

            var sectionRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"Section\s*([IVXLCDM]+)\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionRoman.Success)
            {
                return sectionRoman.Groups[1].Value.ToUpperInvariant();
            }

            var embeddedRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"([IVXLCDM]{2,})\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (embeddedRoman.Success && clean.Length <= 12)
            {
                return embeddedRoman.Groups[1].Value.ToUpperInvariant();
            }

            return "";
        }

        private static List<List<RenderedChar>> FindSectionRomanOccurrences(
            List<RenderedChar> cleanRendered,
            string roman)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(roman)) return occurrences;

            for (int i = 0; i < cleanRendered.Count; i++)
            {
                if (cleanRendered[i].Character != '第') continue;

                int digitStart = i + 1;
                while (digitStart < cleanRendered.Count && cleanRendered[digitStart].Character == ' ')
                {
                    digitStart++;
                }

                if (digitStart + roman.Length > cleanRendered.Count) continue;

                bool match = true;
                for (int r = 0; r < roman.Length; r++)
                {
                    if (!CharEqualsNormalized(cleanRendered[digitStart + r].Character, roman[r]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    occurrences.Add(cleanRendered.GetRange(digitStart, roman.Length));
                }
            }

            if (occurrences.Count == 0)
            {
                var plainOccurrences = FindTextOccurrences(cleanRendered, roman);
                occurrences.AddRange(plainOccurrences);
            }

            string chinese = RomanToChineseSectionNumeral(roman);
            if (occurrences.Count == 0 && !string.IsNullOrEmpty(chinese))
            {
                for (int i = 0; i < cleanRendered.Count; i++)
                {
                    if (cleanRendered[i].Character != chinese[0]) continue;
                    if (i + chinese.Length > cleanRendered.Count) continue;
                    bool match = true;
                    for (int c = 0; c < chinese.Length; c++)
                    {
                        if (cleanRendered[i + c].Character != chinese[c])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        occurrences.Add(cleanRendered.GetRange(i, chinese.Length));
                    }
                }
            }

            return occurrences;
        }

        private static List<List<RenderedChar>> FindFigureRefOccurrences(
            List<RenderedChar> cleanRendered,
            string digits,
            bool includeClosingParen)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(digits)) return occurrences;

            for (int i = 0; i < cleanRendered.Count; i++)
            {
                char c = cleanRendered[i].Character;
                if (c != '圖' && c != '图') continue;

                int digitStart = i + 1;
                while (digitStart < cleanRendered.Count &&
                       (cleanRendered[digitStart].Character == ':' ||
                        cleanRendered[digitStart].Character == '：'))
                {
                    digitStart++;
                }

                if (digitStart + digits.Length > cleanRendered.Count) continue;

                bool match = true;
                for (int d = 0; d < digits.Length; d++)
                {
                    if (cleanRendered[digitStart + d].Character != digits[d])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;

                int end = digitStart + digits.Length;
                if (includeClosingParen && end < cleanRendered.Count && cleanRendered[end].Character == ')')
                {
                    end++;
                }

                occurrences.Add(cleanRendered.GetRange(i, end - i));
            }

            return occurrences;
        }

        private static List<string> BuildAnnotationSearchPatterns(string searchText)
        {
            var patterns = new List<string>();
            if (string.IsNullOrWhiteSpace(searchText)) return patterns;

            string cleanSearch = new string(searchText.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (cleanSearch.Length == 0) return patterns;

            void AddPattern(string pattern)
            {
                if (!string.IsNullOrEmpty(pattern) && !patterns.Contains(pattern))
                {
                    patterns.Add(pattern);
                }
            }

            int openBracket = cleanSearch.IndexOf('[');
            int closeBracket = cleanSearch.IndexOf(']');
            if (openBracket >= 0 && closeBracket > openBracket)
            {
                AddPattern(cleanSearch.Substring(openBracket, closeBracket - openBracket + 1));
            }

            var sectionRomanMatch = System.Text.RegularExpressions.Regex.Match(
                cleanSearch,
                @"(?:Section\s*)?([IVXLCDM]{2,})\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionRomanMatch.Success)
            {
                string roman = sectionRomanMatch.Groups[1].Value.ToUpperInvariant();
                AddPattern(roman);
                AddPattern($"第{roman}");
                AddPattern($"第 {roman}");
                AddPattern($"表{roman}");
                AddPattern($"表 {roman}");
                string chinese = RomanToChineseSectionNumeral(roman);
                if (!string.IsNullOrEmpty(chinese))
                {
                    AddPattern($"第{chinese}");
                    AddPattern(chinese);
                    AddPattern($"表{chinese}");
                }
            }

            var singleRomanMatch = System.Text.RegularExpressions.Regex.Match(
                cleanSearch,
                @"^([IVXLCDM]{1,4})[,.]?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (singleRomanMatch.Success)
            {
                string roman = singleRomanMatch.Groups[1].Value.ToUpperInvariant();
                AddPattern(roman);
                AddPattern($"第{roman}");
                AddPattern($"表{roman}");
                AddPattern($"表 {roman}");
                string chinese = RomanToChineseSectionNumeral(roman);
                if (!string.IsNullOrEmpty(chinese))
                {
                    AddPattern(chinese);
                    AddPattern($"第{chinese}");
                    AddPattern($"表{chinese}");
                }
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(cleanSearch, @"^\d+\)$"))
            {
                string listingNum = new string(cleanSearch.Where(char.IsDigit).ToArray());
                AddPattern(cleanSearch);
                AddPattern($"{listingNum})");
                AddPattern($"第{listingNum}");
                AddPattern($"清單{listingNum}");
                AddPattern($"清單 {listingNum}");
            }

            var sectionMatch = System.Text.RegularExpressions.Regex.Match(
                cleanSearch,
                @"^([IVXLCDM]+)-([A-Z])\)?[,;.:]?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionMatch.Success)
            {
                AddPattern($"{sectionMatch.Groups[1].Value}-{sectionMatch.Groups[2].Value}");
            }
            else
            {
                var embeddedSection = System.Text.RegularExpressions.Regex.Match(
                    cleanSearch,
                    @"([IVXLCDM]+)-([A-Z])\)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (embeddedSection.Success)
                {
                    AddPattern($"{embeddedSection.Groups[1].Value}-{embeddedSection.Groups[2].Value}");
                }
            }

            string trimmed = cleanSearch.TrimEnd(')', ',', '.', ';', ':');
            string digitsOnly = new string(cleanSearch.Where(char.IsDigit).ToArray());
            bool looksLikeFigureNum = digitsOnly.Length > 0 && digitsOnly.Length <= 2 &&
                (cleanSearch.TrimEnd(')', ',', '.', ';', ':').All(c => char.IsDigit(c)) ||
                 System.Text.RegularExpressions.Regex.IsMatch(cleanSearch, @"^\d+\)$"));
            if (looksLikeFigureNum)
            {
                foreach (var prefix in new[] { "圖", ":圖", "即圖", "表", "Fig.", "Figure", "Table" })
                {
                    AddPattern(prefix + digitsOnly);
                    AddPattern(prefix + digitsOnly + ")");
                }
                if (cleanSearch.Contains(')'))
                {
                    AddPattern(digitsOnly + ")");
                }
            }
            else if (trimmed.Length > 0)
            {
                AddPattern(trimmed);
            }

            if (!looksLikeFigureNum)
            {
                AddPattern(cleanSearch);
            }

            var romanOrDigits = new string(cleanSearch.Where(c => char.IsDigit(c) || "IVXLCDMivxlcdm".Contains(c)).ToArray());
            bool isBareNumber = cleanSearch.Length <= 3 && romanOrDigits.Length == cleanSearch.TrimEnd(')', ',', '.').Length;
            if (!looksLikeFigureNum && (romanOrDigits.Length >= 2 || isBareNumber))
            {
                AddPattern(romanOrDigits);
            }

            return patterns;
        }

        private static string NormalizeAnnotationSearchText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            string clean = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.Length == 0) return raw.Trim();

            var citation = System.Text.RegularExpressions.Regex.Match(clean, @"\[\d+\]");
            if (citation.Success) return citation.Value;

            var tableRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"(?:Table|TABLE)\s*([IVXLCDM]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tableRoman.Success)
            {
                return tableRoman.Groups[1].Value.ToUpperInvariant();
            }

            var sectionRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"Section\s*([IVXLCDM]+)\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionRoman.Success)
            {
                return sectionRoman.Groups[1].Value.ToUpperInvariant();
            }

            if (clean.Length <= 8)
            {
                var embeddedRoman = System.Text.RegularExpressions.Regex.Match(
                    clean,
                    @"([IVXLCDM]{2,})\)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (embeddedRoman.Success)
                {
                    return embeddedRoman.Groups[1].Value.ToUpperInvariant();
                }

                var leadingRoman = System.Text.RegularExpressions.Regex.Match(
                    clean,
                    @"^([IVXLCDM]{1,4})(?![IVXLCDM])",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (leadingRoman.Success)
                {
                    return leadingRoman.Groups[1].Value.ToUpperInvariant();
                }

                var bareRomanPunct = System.Text.RegularExpressions.Regex.Match(
                    clean,
                    @"^([IVXLCDM]{1,4})[,.]$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bareRomanPunct.Success)
                {
                    return bareRomanPunct.Groups[1].Value.ToUpperInvariant();
                }
            }

            var section = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"([IVXLCDM]+)-([A-Z])\)?\.?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (section.Success)
            {
                string core = $"{section.Groups[1].Value}-{section.Groups[2].Value}";
                return clean.Contains(core + ")", System.StringComparison.OrdinalIgnoreCase) ? core + ")" : core;
            }

            var figure = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"(?:Figure|Fig\.?|圖)(\d+)\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (figure.Success)
            {
                string num = figure.Groups[1].Value;
                return clean.Contains(num + ")", System.StringComparison.Ordinal) ? num + ")" : num;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"^\d\)?$"))
            {
                return clean;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"^\d+\)$"))
            {
                return clean;
            }

            var loneDigit = System.Text.RegularExpressions.Regex.Match(clean, @"(?<!\d)(\d)\)?(?!\d)");
            if (loneDigit.Success && clean.Length <= 8)
            {
                return loneDigit.Value;
            }

            return raw.Trim();
        }

        private static string RomanToChineseSectionNumeral(string roman)
        {
            if (string.IsNullOrEmpty(roman)) return "";
            return roman.ToUpperInvariant() switch
            {
                "I" => "一",
                "II" => "二",
                "III" => "三",
                "IV" => "四",
                "V" => "五",
                "VI" => "六",
                "VII" => "七",
                "VIII" => "八",
                "IX" => "九",
                "X" => "十",
                _ => ""
            };
        }

        private static List<List<RenderedChar>> FindTextOccurrences(List<RenderedChar> cleanRendered, string cleanSearch)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(cleanSearch)) return occurrences;

            bool requireDigitBoundary = cleanSearch.All(c => char.IsDigit(c) || c == ')') &&
                cleanSearch.Any(char.IsDigit);
            bool requireRomanBoundary = IsRomanNumeralPattern(cleanSearch);

            for (int i = 0; i <= cleanRendered.Count - cleanSearch.Length; i++)
            {
                if (requireDigitBoundary && !IsStandaloneDigitOccurrence(cleanRendered, i, cleanSearch.Length))
                {
                    continue;
                }
                if (requireRomanBoundary && !IsStandaloneRomanOccurrence(cleanRendered, i, cleanSearch.Length))
                {
                    continue;
                }

                bool match = true;
                for (int j = 0; j < cleanSearch.Length; j++)
                {
                    if (!CharEqualsNormalized(cleanRendered[i + j].Character, cleanSearch[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    occurrences.Add(cleanRendered.GetRange(i, cleanSearch.Length));
                }
            }

            return occurrences;
        }

        private static bool IsStandaloneDigitOccurrence(List<RenderedChar> chars, int start, int length)
        {
            if (start > 0 && char.IsDigit(chars[start - 1].Character)) return false;
            int end = start + length;
            if (end < chars.Count && char.IsDigit(chars[end].Character)) return false;
            return true;
        }

        private static List<List<RenderedChar>> FindLooseFigureDigitOccurrences(
            List<RenderedChar> cleanRendered,
            string digits)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(digits)) return occurrences;

            for (int i = 0; i < cleanRendered.Count; i++)
            {
                if (!char.IsDigit(cleanRendered[i].Character)) continue;
                if (!IsStandaloneDigitOccurrence(cleanRendered, i, 1)) continue;

                bool match = true;
                for (int d = 0; d < digits.Length; d++)
                {
                    if (i + d >= cleanRendered.Count || cleanRendered[i + d].Character != digits[d])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;

                int figStart = i;
                for (int back = 1; back <= 6 && i - back >= 0; back++)
                {
                    char c = cleanRendered[i - back].Character;
                    if (c == '圖' || c == '图')
                    {
                        figStart = i - back;
                        break;
                    }
                    if (!char.IsPunctuation(c) && c != '即' && c != ':' && c != '：')
                    {
                        break;
                    }
                }

                occurrences.Add(cleanRendered.GetRange(figStart, i + digits.Length - figStart));
            }

            return occurrences;
        }

        private static bool IsStandaloneRomanOccurrence(List<RenderedChar> chars, int start, int length)
        {
            if (start > 0)
            {
                char prev = chars[start - 1].Character;
                if (char.IsLetter(prev) && prev < 128) return false;
            }
            int end = start + length;
            if (end < chars.Count)
            {
                char next = chars[end].Character;
                if (char.IsLetter(next) && next < 128) return false;
            }
            return true;
        }

        private static List<RenderedChar> PickOccurrenceBySpatialPosition(
            List<RenderedChar> cleanRendered,
            List<List<RenderedChar>> occurrences,
            double targetPdfX,
            double targetPdfY,
            int occurrenceIdx,
            bool preferVerticalAlignment = false)
        {
            if (occurrences.Count == 1) return occurrences[0];

            int bestIdx = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < occurrences.Count; i++)
            {
                double dist = GetOccurrenceCenterDistance(
                    occurrences[i], targetPdfX, targetPdfY, preferVerticalAlignment);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIdx = i;
                }
            }

            if (occurrenceIdx > 0 && occurrenceIdx < occurrences.Count)
            {
                double idxDist = GetOccurrenceCenterDistance(
                    occurrences[occurrenceIdx], targetPdfX, targetPdfY, preferVerticalAlignment);
                if (idxDist <= minDist * 1.5 + 2.0)
                {
                    bestIdx = occurrenceIdx;
                }
            }

            return occurrences[bestIdx];
        }

        private static double GetOccurrenceCenterDistance(
            List<RenderedChar> occurrence,
            double targetPdfX,
            double targetPdfY,
            bool preferVerticalAlignment = false)
        {
            double cx = occurrence.Average(rc => (rc.Left + rc.Right) / 2.0);
            double cy = occurrence.Average(rc => (rc.Bottom + rc.Top) / 2.0);
            double dx = cx - targetPdfX;
            double dy = cy - targetPdfY;
            if (preferVerticalAlignment)
            {
                return Math.Abs(dy) * 4.0 + Math.Abs(dx);
            }
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static List<RenderedChar> MapRenderedCharsBySpatialPosition(
            List<RenderedChar> cleanRendered,
            double targetPdfX,
            double targetPdfY,
            double relWidth,
            double paraWidth)
        {
            if (cleanRendered.Count == 0) return null;

            double targetWidth = Math.Max(8.0, paraWidth * Math.Max(relWidth, 0.02));
            if (relWidth < 0.08)
            {
                targetWidth = Math.Min(targetWidth, 14.0);
            }
            double bestLineY = cleanRendered
                .OrderBy(rc => Math.Abs(((rc.Bottom + rc.Top) / 2.0) - targetPdfY))
                .Select(rc => (rc.Bottom + rc.Top) / 2.0)
                .First();
            double lineTolerance = 4.0;

            var lineChars = cleanRendered
                .Select((rc, idx) => (rc, idx))
                .Where(t => Math.Abs(((t.rc.Bottom + t.rc.Top) / 2.0) - bestLineY) <= lineTolerance)
                .ToList();
            if (lineChars.Count == 0)
            {
                lineChars = cleanRendered.Select((rc, idx) => (rc, idx)).ToList();
            }

            int bestStart = 0;
            double minDist = double.MaxValue;
            for (int start = 0; start < lineChars.Count; start++)
            {
                var cluster = new List<RenderedChar>();
                double usedWidth = 0;
                for (int j = start; j < lineChars.Count; j++)
                {
                    cluster.Add(lineChars[j].rc);
                    usedWidth += lineChars[j].rc.Right - lineChars[j].rc.Left;
                    if (usedWidth >= targetWidth) break;
                }
                if (cluster.Count == 0) continue;

                double cx = cluster.Average(rc => (rc.Left + rc.Right) / 2.0);
                double cy = cluster.Average(rc => (rc.Bottom + rc.Top) / 2.0);
                double dx = cx - targetPdfX;
                double dy = cy - targetPdfY;
                double dist = dx * dx + dy * dy;
                if (dist < minDist)
                {
                    minDist = dist;
                    bestStart = start;
                }
            }

            var result = new List<RenderedChar>();
            double widthUsed = 0;
            for (int j = bestStart; j < lineChars.Count; j++)
            {
                result.Add(lineChars[j].rc);
                widthUsed += lineChars[j].rc.Right - lineChars[j].rc.Left;
                if (widthUsed >= targetWidth) break;
            }

            return result.Count > 0 ? result : null;
        }

        private static void MergeVerticallyAdjacentParagraphs(List<PdfParagraph> paragraphs)
        {
            if (paragraphs.Count <= 1) return;

            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                // Sort by Y1 descending (top to bottom on the page)
                var sorted = paragraphs.OrderByDescending(p => p.Y1).ToList();

                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var p1 = sorted[i];
                    if (p1.IsBypassed || string.IsNullOrWhiteSpace(p1.TextWithPlaceholders)) continue;

                    // If p1 is a heading, do not merge anything into it
                    if (IsHeadingParagraph(p1)) continue;

                    // If p1 ends with sentence-ending punctuation, do not merge subsequent paragraphs
                    string clean1 = p1.TextWithPlaceholders.Trim();
                    if (clean1.EndsWith(".") || clean1.EndsWith("?") || clean1.EndsWith("!") || clean1.EndsWith(":") || 
                        clean1.EndsWith("。") || clean1.EndsWith("」") || clean1.EndsWith("\""))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < sorted.Count; j++)
                    {
                        var p2 = sorted[j];
                        if (p2.IsBypassed || string.IsNullOrWhiteSpace(p2.TextWithPlaceholders)) continue;

                        // Check same column / horizontal overlap > 60%
                        double minWidth = Math.Min(p1.Width, p2.Width);
                        if (minWidth <= 0) continue;

                        double overlap = Math.Min(p1.X1, p2.X1) - Math.Max(p1.X0, p2.X0);
                        if (overlap / minWidth <= 0.6) continue;

                        // Check vertical gap
                        double gap = p1.Y0 - p2.Y1;

                        // Allow a vertical gap of up to 6 pt (tightened from 14 pt to prevent paragraph merging)
                        if (gap > 6 || gap < -10) continue;

                        // Ensure p2 does not start a new list item, reference, or heading
                        if (PdfParagraphBlockMerger.StartsNewParagraphOrSection(p2.TextWithPlaceholders)) continue;

                        // Only merge reference/list multi-line items; never merge ordinary body paragraphs
                        bool isP1RefOrList = ReferenceSectionDetector.IsReferenceParagraph(p1) || PdfParagraphBlockMerger.StartsNewParagraphOrSection(p1.TextWithPlaceholders);
                        bool isP2RefOrList = ReferenceSectionDetector.IsReferenceParagraph(p2) || PdfParagraphBlockMerger.StartsNewParagraphOrSection(p2.TextWithPlaceholders);
                        if (!isP1RefOrList && !isP2RefOrList) continue;

                        // Merge p2 into p1
                        p1.MergeWith(p2);

                        // Remove p2 from the lists
                        paragraphs.Remove(p2);
                        mergedAny = true;
                        break;
                    }
                    if (mergedAny) break;
                }
            }
        }

        private static bool IsHeadingParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            if (txt.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("Keyword", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("關鍵字", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("关键字", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Section numbering like "1. Introduction" or "3.4.1 Projection before Fusion" or "3.2.1 資料收集"
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2}){0,4}\.?(?:\s+[^a-z]|$)")) return true;

            // Lettered subsections like "A. Background" or "C. Case Studies"
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\s+")) return true;

            // Appendix subsections like "B.3 Benchmark Coverage"
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\d+\s")) return true;

            // Uppercase section headers like "REFERENCES", "ABSTRACT", "APPENDIX"
            if (txt.Length < 30 && txt.Any(char.IsLetter) && txt.All(c => !char.IsLower(c)))
            {
                if (txt.Length <= 6 && !txt.Contains(' ') &&
                    txt.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '&'))
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>Appendix headings (A Prompts, B., B.1, B.2, B.3) must never be gray-prompt content.</summary>
        private static bool IsAppendixSectionHeading(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^Appendix\s+[A-Z]", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\s+Prompts\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\d*\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
            return txt.Length < 80 &&
                   System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static List<PdfParagraph> GetPageReadingOrder(List<PdfParagraph> pageList, double pageWidth)
        {
            double center = pageWidth / 2.0;
            var left = pageList.Where(p => p.X0 + p.Width / 2 < center).OrderByDescending(p => p.Y1).ToList();
            var right = pageList.Where(p => p.X0 + p.Width / 2 >= center).OrderByDescending(p => p.Y1).ToList();
            var result = new List<PdfParagraph>(left.Count + right.Count);
            result.AddRange(left);
            result.AddRange(right);
            return result;
        }
    }
}

