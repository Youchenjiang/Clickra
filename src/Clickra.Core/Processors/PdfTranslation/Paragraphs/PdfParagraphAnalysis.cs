using Clickra.Core.Models;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Processors
{
    internal sealed class PdfParagraphAnalysis
    {
        public string TextWithPlaceholders { get; init; } = "";
        public double AverageFontSize { get; init; }
        public bool IsOnlyMath { get; init; }
        public bool IsCode { get; init; }
        public bool IsBold { get; init; }
        public bool IsItalic { get; init; }
        public bool HasLineBreak { get; init; }
        public List<MathFormula> Formulas { get; init; } = new();
        public List<PdfLetter> AllLetters { get; init; } = new();
    }

    internal static class PdfParagraphAnalyzer
    {
        public static PdfParagraphAnalysis Analyze(IReadOnlyList<TextLine> lines)
        {
            var sb = new StringBuilder();
            var formulas = new List<MathFormula>();
            var allLetters = new List<PdfLetter>();
            var currentFormula = new List<Letter>();
            int bracketsCount = 0;

            double totalFontSize = 0;
            int letterCount = 0;

            int boldCount = 0;
            int italicCount = 0;
            int totalCount = 0;

            foreach (var line in lines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        totalFontSize += letter.PointSize;
                        letterCount++;

                        totalCount++;
                        if (FontUtilities.IsSourceFontBold(letter.FontName))
                        {
                            boldCount++;
                        }
                        if (IsItalicFont(letter.FontName))
                        {
                            italicCount++;
                        }

                        allLetters.Add(CreatePdfLetter(letter));
                    }
                }
            }

            double averageFontSize = letterCount > 0 ? totalFontSize / letterCount : 10;
            bool hasLineBreak = false;

            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var line = lines[lineIdx];
                for (int wordIdx = 0; wordIdx < line.Words.Count; wordIdx++)
                {
                    var word = line.Words[wordIdx];
                    bool isMathWord = PdfParagraphMathClassifier.IsMathWord(word);
                    for (int letterIdx = 0; letterIdx < word.Letters.Count; letterIdx++)
                    {
                        var letter = word.Letters[letterIdx];
                        bool curV = PdfParagraphMathClassifier.IsMathCharacter(letter, isMathWord, averageFontSize);
                        if (!curV)
                        {
                            if (currentFormula.Count > 0 && letter.Value == "(")
                            {
                                curV = true;
                                bracketsCount++;
                            }
                            else if (bracketsCount > 0 && letter.Value == ")")
                            {
                                curV = true;
                                bracketsCount--;
                            }
                        }

                        if (curV)
                        {
                            currentFormula.Add(letter);
                        }
                        else
                        {
                            FlushFormula(sb, formulas, currentFormula, ref bracketsCount);
                            sb.Append(letter.Value);
                        }
                    }

                    if (wordIdx < line.Words.Count - 1 && currentFormula.Count == 0)
                    {
                        sb.Append(" ");
                    }
                }

                if (lineIdx < lines.Count - 1)
                {
                    FlushFormula(sb, formulas, currentFormula, ref bracketsCount);
                    sb.Append(" ");
                    hasLineBreak = true;
                }
            }

            FlushFormula(sb, formulas, currentFormula, ref bracketsCount);

            string textWithPlaceholders = sb.ToString();
            return new PdfParagraphAnalysis
            {
                TextWithPlaceholders = textWithPlaceholders,
                AverageFontSize = averageFontSize,
                IsBold = totalCount > 0 && ((double)boldCount / totalCount) > 0.5,
                IsItalic = totalCount > 0 && ((double)italicCount / totalCount) > 0.5,
                IsOnlyMath = formulas.Count == 1 && textWithPlaceholders.Trim() == "{v0}",
                IsCode = PdfParagraphCodeClassifier.IsCodeBlock(textWithPlaceholders) ||
                         PdfParagraphCodeClassifier.IsMonospaceBlock(lines),
                HasLineBreak = hasLineBreak,
                Formulas = formulas,
                AllLetters = allLetters
            };
        }

        private static PdfLetter CreatePdfLetter(Letter letter) => new()
        {
            Value = letter.Value ?? "",
            FontName = letter.FontName ?? "Times New Roman",
            FontSize = letter.PointSize,
            X = letter.Location.X,
            Y = letter.Location.Y,
            Left = letter.BoundingBox.Left,
            Bottom = letter.BoundingBox.Bottom,
            Right = letter.BoundingBox.Right,
            Top = letter.BoundingBox.Top
        };

        private static bool IsItalicFont(string? fontName) =>
            fontName != null &&
            (fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
             fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase) ||
             fontName.Contains("it", StringComparison.OrdinalIgnoreCase) ||
             fontName.Contains("ob", StringComparison.OrdinalIgnoreCase));

        private static void FlushFormula(
            StringBuilder sb,
            List<MathFormula> formulas,
            List<Letter> currentFormula,
            ref int bracketsCount)
        {
            if (currentFormula.Count == 0) return;

            int id = formulas.Count;
            formulas.Add(new MathFormula(id, currentFormula));
            sb.Append($"{{v{id}}}");
            currentFormula.Clear();
            bracketsCount = 0;
        }
    }

}
