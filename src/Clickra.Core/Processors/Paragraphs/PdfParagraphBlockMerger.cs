using System;
using System.Collections.Generic;
using System.Linq;

namespace Clickra.Core.Processors
{
    public static class PdfParagraphBlockMerger
    {
        public static bool StartsNewParagraphOrSection(string text)
        {
            string trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return false;

            if (trimmed.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Keyword", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("關鍵字", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("关键字", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Matches "[1]", "1.", "1)", "a.", "a)", "•", "-", "*"
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^(?:\[\d+\]|\d+[\.\)]|[a-zA-Z][\.\)]|[•\-\*])(?:\s|$)")) return true;

            // Check if it's a section header
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d{1,2}(?:\.\d{1,2}){0,4}\.?(?:\s+[^a-z]|$)")) return true;
            if (trimmed.Length < 30 && trimmed.Any(char.IsLetter) && trimmed.All(c => !char.IsLower(c))) return true;

            // Check for Table/Figure/RQ captions/headings to prevent them from merging with nearby text blocks
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^(?:Table|Figure|Fig|表|圖|RQ\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;

            return false;
        }

        public static bool IsHeadingLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line)
        {
            string txt = line.Text.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

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


        private static (UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine? left, UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine? right) SplitLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line, double center)
        {
            if (line.BoundingBox.Left >= center || line.BoundingBox.Right <= center)
            {
                return (null, null);
            }

            var sortedWords = line.Words.OrderBy(w => w.BoundingBox.Left).ToList();
            if (sortedWords.Count < 2) return (null, null);

            for (int i = 0; i < sortedWords.Count - 1; i++)
            {
                var w1 = sortedWords[i];
                var w2 = sortedWords[i + 1];

                if (w1.BoundingBox.Right < center && w2.BoundingBox.Left > center)
                {
                    double gap = w2.BoundingBox.Left - w1.BoundingBox.Right;
                    if (gap >= 8.0) // gutter threshold
                    {
                        var leftWords = sortedWords.Take(i + 1).ToList();
                        var rightWords = sortedWords.Skip(i + 1).ToList();

                        var leftLine = new UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine(leftWords, " ");
                        var rightLine = new UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine(rightWords, " ");

                        return (leftLine, rightLine);
                    }
                }
            }

            return (null, null);
        }

        public class MergedBlock
        {
            public List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> TextLines { get; set; } = new();
            public double Right { get; set; }
        }

        public static List<MergedBlock> GetMergedBlocks(IEnumerable<UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock> docstrumBlocks, double pageWidth, bool isTablePage = false)
        {
            double maxGap = isTablePage ? 8.0 : 15.0;
            double center = pageWidth / 2.0;

            var initialBlocks = docstrumBlocks.Select(b => new MergedBlock
            {
                TextLines = b.TextLines.ToList(),
                Right = b.BoundingBox.Right
            }).ToList();

            var list = new List<MergedBlock>();

            // Always split Docstrum blocks at the page center. Skipping this on table pages
            // merges left-column tables with right-column body text into full-width paragraphs.
            foreach (var b in initialBlocks)
            {
                var leftLines = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
                var rightLines = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
                bool hasSpanningLine = false;

                foreach (var line in b.TextLines)
                {
                    if (line.BoundingBox.Left < center && line.BoundingBox.Right > center)
                    {
                        var (leftPart, rightPart) = SplitLine(line, center);
                        if (leftPart != null && rightPart != null)
                        {
                            leftLines.Add(leftPart);
                            rightLines.Add(rightPart);
                        }
                        else
                        {
                            hasSpanningLine = true;
                            break;
                        }
                    }
                    else if (line.BoundingBox.Right <= center)
                    {
                        leftLines.Add(line);
                    }
                    else
                    {
                        rightLines.Add(line);
                    }
                }

                if (hasSpanningLine)
                {
                    list.Add(b);
                }
                else
                {
                    if (leftLines.Count > 0)
                    {
                        list.Add(new MergedBlock
                        {
                            TextLines = leftLines,
                            Right = leftLines.Max(l => l.BoundingBox.Right)
                        });
                    }
                    if (rightLines.Count > 0)
                    {
                        list.Add(new MergedBlock
                        {
                            TextLines = rightLines,
                            Right = rightLines.Max(l => l.BoundingBox.Right)
                        });
                    }
                }
            }

            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var b1 = list[i];
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var b2 = list[j];

                        // Never merge blocks from different columns
                        double b1Center = b1.TextLines.Average(l => (l.BoundingBox.Left + l.BoundingBox.Right) / 2.0);
                        double b2Center = b2.TextLines.Average(l => (l.BoundingBox.Left + l.BoundingBox.Right) / 2.0);
                        if ((b1Center < center) != (b2Center < center)) continue;

                        // Check if they should be merged horizontally
                        bool canMerge = false;
                        foreach (var l1 in b1.TextLines)
                        {
                            foreach (var l2 in b2.TextLines)
                            {
                                double verticalOverlap = Math.Min(l1.BoundingBox.Top, l2.BoundingBox.Top) - Math.Max(l1.BoundingBox.Bottom, l2.BoundingBox.Bottom);
                                double minHeight = Math.Min(l1.BoundingBox.Height, l2.BoundingBox.Height);
                                if (minHeight <= 0 || verticalOverlap / minHeight <= 0.5) continue;

                                // Check gap between their horizontal boundaries
                                double gap = l1.BoundingBox.Left < l2.BoundingBox.Left
                                    ? l2.BoundingBox.Left - l1.BoundingBox.Right
                                    : l1.BoundingBox.Left - l2.BoundingBox.Right;

                                double c1 = (l1.BoundingBox.Left + l1.BoundingBox.Right) / 2.0;
                                double c2 = (l2.BoundingBox.Left + l2.BoundingBox.Right) / 2.0;
                                bool isL1Left = c1 < center;
                                bool isL2Left = c2 < center;
                                double allowedGap = (isL1Left != isL2Left) ? 5.0 : maxGap;

                                if (gap >= -5.0 && gap <= allowedGap)
                                {
                                    canMerge = true;
                                    break;
                                }
                            }
                            if (canMerge) break;
                        }

                        if (canMerge)
                        {
                            // Merge b2 into b1
                            b1.TextLines.AddRange(b2.TextLines);
                            b1.Right = Math.Max(b1.Right, b2.Right);

                            list.RemoveAt(j);
                            mergedAny = true;
                            break;
                        }
                    }
                    if (mergedAny) break;
                }
            }

            return list;
        }

    }
}
