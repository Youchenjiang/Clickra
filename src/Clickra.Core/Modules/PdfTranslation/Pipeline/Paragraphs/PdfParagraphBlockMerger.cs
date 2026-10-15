using System;
using System.Collections.Generic;
using System.Linq;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline;

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

        foreach (var b in initialBlocks)
        {
            SplitBlockAtPageCenter(b, center, list);
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

                    if (CanMergeBlocksHorizontally(b1, b2, center, maxGap))
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

    private static void SplitBlockAtPageCenter(MergedBlock block, double center, List<MergedBlock> result)
    {
        // Split even on table pages so two-column tables remain isolated.
        var leftLines = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
        var rightLines = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
        foreach (var line in block.TextLines)
        {
            if (line.BoundingBox.Left < center && line.BoundingBox.Right > center)
            {
                var (leftPart, rightPart) = SplitLine(line, center);
                if (leftPart == null || rightPart == null)
                {
                    result.Add(block);
                    return;
                }
                leftLines.Add(leftPart);
                rightLines.Add(rightPart);
            }
            else if (line.BoundingBox.Right <= center) leftLines.Add(line);
            else rightLines.Add(line);
        }

        if (leftLines.Count > 0)
            result.Add(new MergedBlock { TextLines = leftLines, Right = leftLines.Max(l => l.BoundingBox.Right) });
        if (rightLines.Count > 0)
            result.Add(new MergedBlock { TextLines = rightLines, Right = rightLines.Max(l => l.BoundingBox.Right) });
    }

    private static bool CanMergeBlocksHorizontally(MergedBlock first, MergedBlock second, double center, double maxGap)
    {
        double firstCenter = first.TextLines.Average(l => (l.BoundingBox.Left + l.BoundingBox.Right) / 2.0);
        double secondCenter = second.TextLines.Average(l => (l.BoundingBox.Left + l.BoundingBox.Right) / 2.0);
        if ((firstCenter < center) != (secondCenter < center)) return false;

        return first.TextLines.Any(firstLine =>
            second.TextLines.Any(secondLine => CanMergeLinePair(firstLine, secondLine, center, maxGap)));
    }

    private static bool CanMergeLinePair(
        UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine first,
        UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine second,
        double center, double maxGap)
    {
        double overlap = Math.Min(first.BoundingBox.Top, second.BoundingBox.Top) -
                         Math.Max(first.BoundingBox.Bottom, second.BoundingBox.Bottom);
        double minHeight = Math.Min(first.BoundingBox.Height, second.BoundingBox.Height);
        if (minHeight <= 0 || overlap / minHeight <= 0.5) return false;

        double gap = first.BoundingBox.Left < second.BoundingBox.Left
            ? second.BoundingBox.Left - first.BoundingBox.Right
            : first.BoundingBox.Left - second.BoundingBox.Right;
        double firstCenter = (first.BoundingBox.Left + first.BoundingBox.Right) / 2.0;
        double secondCenter = (second.BoundingBox.Left + second.BoundingBox.Right) / 2.0;
        double allowedGap = (firstCenter < center) != (secondCenter < center) ? 5.0 : maxGap;
        return gap >= -5.0 && gap <= allowedGap;
    }

}
