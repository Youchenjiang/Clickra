using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Models
{
    internal sealed class PdfParagraphLayoutAnalysis
    {
        public double X0 { get; init; }
        public double Y0 { get; init; }
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public object TextDirection { get; init; } = "Rotate0";
        public PdfParagraph.TextAlignment Alignment { get; init; } = PdfParagraph.TextAlignment.Left;
    }

    internal static class PdfParagraphLayoutAnalyzer
    {
        public static PdfParagraphLayoutAnalysis Analyze(IReadOnlyList<TextLine> lines, string textWithPlaceholders)
        {
            double x0 = lines.Min(line => Math.Min(line.BoundingBox.Left, line.BoundingBox.Right));
            double y0 = lines.Min(line => Math.Min(line.BoundingBox.Bottom, line.BoundingBox.Top));
            double x1 = lines.Max(line => Math.Max(line.BoundingBox.Left, line.BoundingBox.Right));
            double y1 = lines.Max(line => Math.Max(line.BoundingBox.Bottom, line.BoundingBox.Top));

            return new PdfParagraphLayoutAnalysis
            {
                X0 = x0,
                Y0 = y0,
                X1 = x1,
                Y1 = y1,
                TextDirection = DetectTextDirection(lines),
                Alignment = DetectAlignment(lines, x0, x1, textWithPlaceholders)
            };
        }

        private static object DetectTextDirection(IReadOnlyList<TextLine> lines)
        {
            var directions = new Dictionary<object, int>();
            foreach (var line in lines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        var dir = GetLetterDirection(letter);
                        directions[dir] = directions.GetValueOrDefault(dir, 0) + 1;
                    }
                }
            }

            return directions.Count > 0
                ? directions.OrderByDescending(kv => kv.Value).First().Key
                : "Rotate0";
        }

        private static string GetLetterDirection(Letter letter)
        {
            double dx = letter.EndBaseLine.X - letter.StartBaseLine.X;
            double dy = letter.EndBaseLine.Y - letter.StartBaseLine.Y;
            double angleDeg = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angleDeg < 0) angleDeg += 360;

            if (angleDeg >= 45 && angleDeg < 135) return "Rotate270";
            if (angleDeg >= 135 && angleDeg < 225) return "Rotate180";
            if (angleDeg >= 225 && angleDeg < 315) return "Rotate90";
            return "Rotate0";
        }

        private static PdfParagraph.TextAlignment DetectAlignment(
            IReadOnlyList<TextLine> lines,
            double x0,
            double x1,
            string textWithPlaceholders)
        {
            if (IsReferenceText(textWithPlaceholders))
            {
                return PdfParagraph.TextAlignment.Left;
            }

            double totalLeftGap = 0;
            double totalRightGap = 0;
            int lineCountWithGaps = 0;
            foreach (var line in lines)
            {
                double leftGap = line.BoundingBox.Left - x0;
                double rightGap = x1 - line.BoundingBox.Right;
                if (leftGap > 5 && rightGap > 5)
                {
                    totalLeftGap += leftGap;
                    totalRightGap += rightGap;
                    lineCountWithGaps++;
                }
            }

            if (lineCountWithGaps <= 0)
            {
                return PdfParagraph.TextAlignment.Left;
            }

            double avgLeft = totalLeftGap / lineCountWithGaps;
            double avgRight = totalRightGap / lineCountWithGaps;
            double diff = Math.Abs(avgLeft - avgRight);
            if (diff < 15)
            {
                return PdfParagraph.TextAlignment.Center;
            }

            return avgLeft > avgRight + 15
                ? PdfParagraph.TextAlignment.Right
                : PdfParagraph.TextAlignment.Left;
        }

        private static bool IsReferenceText(string textWithPlaceholders)
        {
            string trimmedText = textWithPlaceholders.Trim();
            return Regex.IsMatch(trimmedText, @"^\[\d+\]") ||
                   trimmedText.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                   trimmedText.Contains("doi:", StringComparison.OrdinalIgnoreCase) ||
                   trimmedText.Contains("www.", StringComparison.OrdinalIgnoreCase);
        }
    }
}
