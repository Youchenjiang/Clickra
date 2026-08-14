using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;
using UglyToad.PdfPig.Content;

namespace Clickra.Core.Processors
{
    internal static class PdfDiagramLabelMarker
    {
        /// <summary>Bar-chart axis labels on pages without vector diagram bounds (PentestAgent p10/p11).</summary>
        public static void ReclassifyStandaloneChartLabelsAsDiagram(List<PdfParagraph> pageList)
        {
            bool pageHasBarChart = pageList.Any(p =>
            {
                string t = p.TextWithPlaceholders.Trim();
                return Regex.IsMatch(t,
                    @"^(?:Figure|Fig\.)\s+\d+\s*[:.].*(?:Success rate|Completion level|overhead|Backbone|difficulty levels|line,?\s+branch,?.*method|coverage achieved)",
                    RegexOptions.IgnoreCase);
            });
            if (!pageHasBarChart) return;

            foreach (var para in pageList)
            {
                if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
                if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                    PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) continue;
                if (PdfChartLabelClassifier.IsLikelyChartLabel(para) ||
                    PdfChartLabelClassifier.IsLikelyBarChartAxisLabel(para))
                {
                    para.IsTable = false;
                    para.IsDiagram = true;
                    continue;
                }
                string txt = para.TextWithPlaceholders.Trim();
                if (Regex.IsMatch(txt, @"^\d+$") &&
                    para.Height <= 14 && para.Width <= 20)
                {
                    para.IsTable = false;
                    para.IsDiagram = true;
                }
            }
        }

        /// <summary>
        /// Mark selectable chart labels whose letters overlap large vector/image bounds
        /// but were missed by paragraph-bbox intersection alone.
        /// </summary>
        public static void MarkDiagramFigureLabels(
            List<PdfParagraph> pageList,
            Page pigPage,
            IReadOnlyList<TableMaskRegion> diagramRegions)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (para.IsTable && !para.IsDiagram) continue;
                if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pigPage.Height)) continue;
                if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
                if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para) ||
                    PdfGrayPromptClassifier.IsGrayPromptSubheading(para)) continue;
                if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para)) continue;

                double letterRatio = PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, diagramRegions);
                bool bboxHits = PdfDiagramMaskBuilder.OverlapsWithLargeImage(para, pigPage);
                bool regionHits = PdfDiagramRegionGeometry.OverlapsAnyRegion(para, diagramRegions);
                if (PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) &&
                    !IsShortFigureLabel(para, regionHits)) continue;
                if (letterRatio >= 0.35 || (bboxHits && PdfChartLabelClassifier.IsLikelyChartLabel(para)) ||
                    (letterRatio >= 0.2 && PdfChartLabelClassifier.IsLikelyChartLabel(para)) ||
                    (regionHits && PdfChartLabelClassifier.IsLikelyChartLabel(para)))
                {
                    para.IsDiagram = true;
                    para.IsTable = false;
                }
            }
        }

        /// <summary>Last pass: any short text inside diagram mask regions becomes a figure label.</summary>
        public static void FinalizeDiagramFigureLabels(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions,
            double pageHeight)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList.Where(p => ShouldMarkAsDiagramFigureLabel(p, diagramRegions, pageHeight)))
            {
                para.IsDiagram = true;
                para.IsTable = false;
            }
        }

        private static bool IsEligibleDiagramCandidate(PdfParagraph para, IReadOnlyList<TableMaskRegion> diagramRegions, double pageHeight)
        {
            if (para.IsTable) return false;
            if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pageHeight)) return false;
            if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) return false;
            if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para) ||
                PdfGrayPromptClassifier.IsGrayPromptSubheading(para)) return false;
            if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para)) return false;
            return PdfDiagramRegionGeometry.OverlapsAnyRegion(para, diagramRegions);
        }

        private static bool ShouldMarkAsDiagramFigureLabel(
            PdfParagraph para,
            IReadOnlyList<TableMaskRegion> diagramRegions,
            double pageHeight)
        {
            if (!IsEligibleDiagramCandidate(para, diagramRegions, pageHeight)) return false;

            if (para.Height > 50) return false;
            if (PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) &&
                !IsShortFigureLabel(para, true)) return false;

            string txt = para.TextWithPlaceholders.Trim();
            double letterRatio = PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, diagramRegions);

            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) && letterRatio < 0.35) return false;
            if (txt.Length > 140 && (letterRatio < 0.45 || PdfParagraphRoleClassifier.IsTranslatableBodyProse(para))) return false;

            if (!PdfChartLabelClassifier.IsLikelyChartLabel(para) &&
                !IsShortFigureLabel(para, true))
            {
                return false;
            }

            return true;
        }

        private static readonly char[] LabelSeparators = [' ', '\t', '\r', '\n'];

        private static bool IsShortFigureLabel(PdfParagraph para, bool insideDiagramRegion)
        {
            if (!insideDiagramRegion) return false;
            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para)) return false;
            string text = para.TextWithPlaceholders.Trim();
            int wordCount = text.Split(LabelSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
            double height = Math.Max(0, para.Y1 - para.Y0);
            return height <= 22 && text.Length <= 80 && wordCount <= 6;
        }

        /// <summary>
        /// Re-apply the immutable workflow-label rule after later cleanup passes.
        /// Some cleanup passes intentionally clear diagram flags from prose-like
        /// paragraphs; short selectable labels inside the figure must not be
        /// cleared, otherwise Pass 1 masks erase the original diagram text.
        /// </summary>
        public static void FinalizeShortFigureLabels(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                // Gray prompt boxes bypass as code, never as diagram: this final
                // short-label pass must not re-flag their content (PentestAgent
                // p4/p7/p14 prompt boxes overlap diagram regions).
                if (para.IsGrayPromptContent || para.IsCode) continue;

                bool intersects = diagramRegions.Any(region =>
                    para.X0 <= region.X1 && para.X1 >= region.X0 &&
                    para.Y0 <= region.Y1 && para.Y1 >= region.Y0);
                if (!IsShortFigureLabel(para, intersects)) continue;

                para.IsDiagram = true;
                para.IsBypassed = true;
                para.IsTable = false;
            }
        }

        /// <summary>Workflow figure banner lines (PentestAgent p5 Fig.1 headers) inside diagram masks.</summary>
        public static void MarkWorkflowBannerTextInDiagramRegions(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions,
            double pageHeight,
            Func<PdfParagraph, bool> isGrayPromptCodeParagraph)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (para.IsCode || para.IsGrayPromptContent || isGrayPromptCodeParagraph(para)) continue;
                if (para.IsTable) continue;
                if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pageHeight)) continue;
                if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para) ||
                    PdfParagraphSemanticClassifier.IsHeadingParagraph(para)) continue;
                if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                    PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) continue;
                if (!PdfDiagramRegionGeometry.OverlapsAnyRegion(para, diagramRegions)) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (para.Height > 24 || txt.Length > 220) continue;
                double letterRatio = PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, diagramRegions);
                if (para.Height <= 22 && letterRatio >= 0.08)
                {
                    para.IsDiagram = true;
                    para.IsTable = false;
                    continue;
                }
                if (PdfChartLabelClassifier.IsLikelyChartLabel(para) || letterRatio >= 0.15)
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
        public static void MarkWorkflowFigureLabelsAboveCaption(
            List<PdfParagraph> pageList,
            double pageHeight)
        {
            foreach (var caption in pageList.Where(PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph))
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
        public static void MarkCodeFigureContentAboveCaption(
            List<PdfParagraph> pageList,
            double pageWidth,
            double pageHeight)
        {
            foreach (var caption in pageList.Where(PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph))
            {
                string text = caption.TextWithPlaceholders.Trim();
                if (!Regex.IsMatch(
                        text, @"^Fig\.\s*[45]\b",
                        RegexOptions.IgnoreCase))
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
    }
}
