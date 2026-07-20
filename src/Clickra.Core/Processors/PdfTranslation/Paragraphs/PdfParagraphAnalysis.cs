using Clickra.Core.Models;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Processors
{
    internal sealed class PdfParagraphAnalysis
    {
        public string TextWithPlaceholders { get; init; } = "";
        public string TranslationTextWithStyles { get; init; } = "";
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
            var styled = new StringBuilder();
            bool styledBoldOpen = false;
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
                            CloseStyledBold(styled, ref styledBoldOpen);
                            currentFormula.Add(letter);
                        }
                        else
                        {
                            int formulaCountBefore = formulas.Count;
                            FlushFormula(sb, formulas, currentFormula, ref bracketsCount);
                            if (formulas.Count > formulaCountBefore)
                                styled.Append($"{{v{formulas.Count - 1}}}");
                            AppendStyledText(styled, letter.Value, FontUtilities.IsSourceFontBold(letter.FontName), ref styledBoldOpen);
                            sb.Append(letter.Value);
                        }
                    }

                    if (wordIdx < line.Words.Count - 1 && currentFormula.Count == 0)
                    {
                        sb.Append(" ");
                        AppendStyledText(styled, " ", false, ref styledBoldOpen);
                    }
                }

                if (lineIdx < lines.Count - 1)
                {
                    int formulaCountBefore = formulas.Count;
                    FlushFormula(sb, formulas, currentFormula, ref bracketsCount);
                    CloseStyledBold(styled, ref styledBoldOpen);
                    if (formulas.Count > formulaCountBefore)
                        styled.Append($"{{v{formulas.Count - 1}}}");
                    sb.Append(" ");
                    styled.Append(' ');
                    hasLineBreak = true;
                }
            }

            int finalFormulaCountBefore = formulas.Count;
            FlushFormula(sb, formulas, currentFormula, ref bracketsCount);
            if (formulas.Count > finalFormulaCountBefore)
                styled.Append($"{{v{formulas.Count - 1}}}");
            CloseStyledBold(styled, ref styledBoldOpen);

            string textWithPlaceholders = PdfParagraphMarkerNormalizer.Normalize(sb.ToString(), formulas);
            string translationTextWithStyles = PdfParagraphMarkerNormalizer.Normalize(styled.ToString(), formulas);
            return new PdfParagraphAnalysis
            {
                TextWithPlaceholders = textWithPlaceholders,
                TranslationTextWithStyles = translationTextWithStyles,
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

        private static PdfLetter CreatePdfLetter(Letter letter)
        {
            double dx = letter.EndBaseLine.X - letter.StartBaseLine.X;
            double dy = letter.EndBaseLine.Y - letter.StartBaseLine.Y;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angle < 0) angle += 360;
            double rotation = angle >= 45 && angle < 135
                ? -90
                : angle >= 135 && angle < 225
                    ? 180
                    : angle >= 225 && angle < 315 ? 90 : 0;

            return new PdfLetter
            {
                Value = letter.Value ?? "",
                FontName = letter.FontName ?? "Times New Roman",
                FontSize = letter.PointSize,
                X = letter.Location.X,
                Y = letter.Location.Y,
                Left = letter.BoundingBox.Left,
                Bottom = letter.BoundingBox.Bottom,
                Right = letter.BoundingBox.Right,
                Top = letter.BoundingBox.Top,
                Rotation = rotation,
                IsBold = FontUtilities.IsSourceFontBold(letter.FontName)
            };
        }

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

        private static void AppendStyledText(StringBuilder styled, string value, bool bold, ref bool boldOpen)
        {
            if (bold && !boldOpen)
            {
                styled.Append("{b}");
                boldOpen = true;
            }
            else if (!bold && boldOpen)
            {
                styled.Append("{/b}");
                boldOpen = false;
            }

            styled.Append(value);
        }

        private static void CloseStyledBold(StringBuilder styled, ref bool boldOpen)
        {
            if (!boldOpen) return;
            styled.Append("{/b}");
            boldOpen = false;
        }
    }

    /// <summary>
    /// PdfPig can extract a circled number as a standalone combining-circle
    /// formula followed by the next digit, e.g. <c>{v0}, 1 {v1}, 2 {v2}3</c>.
    /// Keep those figure-caption markers as inline Unicode circled digits before
    /// translation and rendering; otherwise the circle formulas disappear and
    /// the remaining digits are painted at unrelated coordinates.
    /// </summary>
    internal static class PdfParagraphMarkerNormalizer
    {
        private static readonly Regex CircledMarkerSequence = new(
            @"\{v(?<a>\d+)\}\s*,\s*(?<d1>\d{1,2})\s+\{v(?<b>\d+)\}\s*,\s*(?<d2>\d{1,2})\s+\{v(?<c>\d+)\}(?<d3>\d{1,2})",
            RegexOptions.Compiled);

        public static string Normalize(string text, IReadOnlyList<MathFormula> formulas)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return CircledMarkerSequence.Replace(text, match =>
            {
                if (!IsCircleFormula(formulas, match, "a") ||
                    !IsCircleFormula(formulas, match, "b") ||
                    !IsCircleFormula(formulas, match, "c"))
                {
                    return match.Value;
                }

                string first = ToCircledDigit(match.Groups["d1"].Value);
                string second = ToCircledDigit(match.Groups["d2"].Value);
                string third = ToCircledDigit(match.Groups["d3"].Value);
                if (first == match.Groups["d1"].Value ||
                    second == match.Groups["d2"].Value ||
                    third == match.Groups["d3"].Value)
                {
                    return match.Value;
                }

                return $"{first}, {second}, {third}";
            });
        }

        public static string RestoreTranslatedMarkers(string source, string translated)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translated) ||
                !source.Contains('①', StringComparison.Ordinal) ||
                translated.Any(value => value is >= '①' and <= '⑳'))
            {
                return translated;
            }

            var sourceMarkers = source.Where(value => value is >= '①' and <= '⑳').ToArray();
            if (sourceMarkers.Length < 2) return translated;

            string markerSequence = string.Join("\\s*[,，、;；]\\s*", sourceMarkers.Select(marker =>
                Regex.Escape(CircledNumberValue(marker))));
            var sequence = new Regex($"(?<!\\d){markerSequence}(?!\\d)", RegexOptions.Compiled);
            return sequence.Replace(translated, _ => string.Join("、", sourceMarkers), 1);
        }

        private static string CircledNumberValue(char marker) =>
            marker == '⓪' ? "0" : (marker - '①' + 1).ToString();

        private static bool IsCircleFormula(
            IReadOnlyList<MathFormula> formulas,
            Match match,
            string groupName)
        {
            if (!int.TryParse(match.Groups[groupName].Value, out int id) ||
                id < 0 || id >= formulas.Count)
            {
                return false;
            }

            string value = string.Concat(formulas[id].Letters.Select(letter => letter.Value));
            return value.Contains('\u20DD', StringComparison.Ordinal);
        }

        private static string ToCircledDigit(string value)
        {
            if (!int.TryParse(value, out int number)) return value;
            if (number == 0) return "⓪";
            if (number is >= 1 and <= 20)
                return char.ConvertFromUtf32(0x245F + number);
            return value;
        }
    }

}
