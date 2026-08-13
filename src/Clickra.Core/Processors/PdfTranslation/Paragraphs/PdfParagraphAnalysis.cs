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
            var (averageFontSize, boldCount, italicCount, totalCount, allLetters) = ComputeFontStats(lines);
            var (sb, styled, formulas, hasLineBreak) = ProcessTextLines(lines, averageFontSize);

            string textWithPlaceholders = PdfParagraphMarkerNormalizer.Normalize(sb.ToString(), formulas);
            string translationTextWithStyles = PdfParagraphMarkerNormalizer.NormalizeStyleRuns(
                textWithPlaceholders,
                PdfParagraphMarkerNormalizer.Normalize(styled.ToString(), formulas),
                totalCount > 0 && boldCount == totalCount);
            bool isCode = PdfParagraphCodeClassifier.IsCodeBlock(textWithPlaceholders) ||
                          PdfParagraphCodeClassifier.IsMonospaceBlock(lines);
            if (isCode && LooksLikeLongProse(textWithPlaceholders, allLetters))
                isCode = false;
            return new PdfParagraphAnalysis
            {
                TextWithPlaceholders = textWithPlaceholders,
                TranslationTextWithStyles = translationTextWithStyles,
                AverageFontSize = averageFontSize,
                IsBold = totalCount > 0 && ((double)boldCount / totalCount) > 0.5,
                IsItalic = totalCount > 0 && ((double)italicCount / totalCount) > 0.5,
                IsOnlyMath = formulas.Count == 1 && textWithPlaceholders.Trim() == "{v0}",
                IsCode = isCode,
                HasLineBreak = hasLineBreak,
                Formulas = formulas,
                AllLetters = allLetters
            };
        }

        private static bool LooksLikeLongProse(string text, IReadOnlyList<PdfLetter> letters)
        {
            string plain = Regex.Replace(text.Trim(), @"\{v\d+\}", "", RegexOptions.None, TimeSpan.FromSeconds(1));
            int wordCount = Regex.Matches(plain, @"\b[A-Za-z][A-Za-z-]*\b", RegexOptions.None, TimeSpan.FromSeconds(1)).Count;
            if (wordCount < 18 || plain.All(c => !char.IsLower(c))) return false;
            if (Regex.IsMatch(plain, @"\b(?:public|private|class|void|return|assert|try|catch|if|else)\b",
                    RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) return false;

            double width = letters.Count == 0 ? 0 : letters.Max(l => l.Right) - letters.Min(l => l.Left);
            return width > 100 && (plain.Contains('.') || plain.Contains(':') || plain.Contains(';'));
        }

        private static (double averageFontSize, int boldCount, int italicCount, int totalCount, List<PdfLetter> allLetters) ComputeFontStats(IReadOnlyList<TextLine> lines)
        {
            double totalFontSize = 0;
            int letterCount = 0;
            int boldCount = 0;
            int italicCount = 0;
            int totalCount = 0;
            var allLetters = new List<PdfLetter>();

            foreach (var line in lines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        totalFontSize += letter.PointSize;
                        letterCount++;
                        totalCount++;
                        if (FontUtilities.IsSourceFontBold(letter.FontName)) boldCount++;
                        if (IsItalicFont(letter.FontName)) italicCount++;
                        allLetters.Add(CreatePdfLetter(letter));
                    }
                }
            }

            double averageFontSize = letterCount > 0 ? totalFontSize / letterCount : 10;
            return (averageFontSize, boldCount, italicCount, totalCount, allLetters);
        }

        private static (StringBuilder sb, StringBuilder styled, List<MathFormula> formulas, bool hasLineBreak) ProcessTextLines(IReadOnlyList<TextLine> lines, double averageFontSize)
        {
            var ctx = new WordProcessingContext();
            bool hasLineBreak = false;

            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var line = lines[lineIdx];
                for (int wordIdx = 0; wordIdx < line.Words.Count; wordIdx++)
                {
                    var word = line.Words[wordIdx];
                    ProcessWordLetters(word, averageFontSize, ctx);

                    if (wordIdx < line.Words.Count - 1)
                    {
                        ctx.Sb.Append(' ');
                        AppendStyledText(ctx.Styled, " ", false, ref ctx.StyledBoldOpen);
                    }
                }

                if (lineIdx < lines.Count - 1)
                {
                    hasLineBreak = true;
                }
            }

            int finalFormulaCountBefore = ctx.Formulas.Count;
            FlushFormula(ctx.Sb, ctx.Formulas, ctx.CurrentFormula, ref ctx.BracketsCount);
            if (ctx.Formulas.Count > finalFormulaCountBefore)
                ctx.Styled.Append($"{{v{ctx.Formulas.Count - 1}}}");
            CloseStyledBold(ctx.Styled, ref ctx.StyledBoldOpen);

            return (ctx.Sb, ctx.Styled, ctx.Formulas, hasLineBreak);
        }

        private sealed class WordProcessingContext
        {
            public StringBuilder Sb { get; } = new();
            public StringBuilder Styled { get; } = new();
            public List<MathFormula> Formulas { get; } = new();
            public List<Letter> CurrentFormula { get; } = new();
            public bool StyledBoldOpen;
            public int BracketsCount;
        }

        private static void ProcessWordLetters(Word word, double averageFontSize, WordProcessingContext ctx)
        {
            bool isMathWord = PdfParagraphMathClassifier.IsMathWord(word);
            for (int letterIdx = 0; letterIdx < word.Letters.Count; letterIdx++)
            {
                var letter = word.Letters[letterIdx];
                bool curV = PdfParagraphMathClassifier.IsMathCharacter(letter, isMathWord, averageFontSize);
                if (!curV)
                {
                    if (ctx.CurrentFormula.Count > 0 && letter.Value == "(")
                    {
                        curV = true;
                        ctx.BracketsCount++;
                    }
                    else if (ctx.BracketsCount > 0 && letter.Value == ")")
                    {
                        curV = true;
                        ctx.BracketsCount--;
                    }
                }

                if (curV)
                {
                    CloseStyledBold(ctx.Styled, ref ctx.StyledBoldOpen);
                    ctx.CurrentFormula.Add(letter);
                }
                else
                {
                    int formulaCountBefore = ctx.Formulas.Count;
                    FlushFormula(ctx.Sb, ctx.Formulas, ctx.CurrentFormula, ref ctx.BracketsCount);
                    if (ctx.Formulas.Count > formulaCountBefore)
                        ctx.Styled.Append($"{{v{ctx.Formulas.Count - 1}}}");
                    AppendStyledText(ctx.Styled, letter.Value, FontUtilities.IsSourceFontBold(letter.FontName), ref ctx.StyledBoldOpen);
                    ctx.Sb.Append(letter.Value);
                }
            }
        }

        private static PdfLetter CreatePdfLetter(Letter letter)
        {
            double dx = letter.EndBaseLine.X - letter.StartBaseLine.X;
            double dy = letter.EndBaseLine.Y - letter.StartBaseLine.Y;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angle < 0) angle += 360;
            double rotation = CalculateLetterRotation(angle);

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

        private static double CalculateLetterRotation(double angle)
        {
            if (angle is >= 45 and < 135) return -90;
            if (angle is >= 135 and < 225) return 180;
            if (angle is >= 225 and < 315) return 90;
            return 0;
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
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));
        private static readonly Regex AdjacentBoldRuns = new(
            @"\{/b\}\s+\{b\}",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        /// <summary>
        /// Avoid sending one bold marker pair per extracted PDF line. Providers
        /// can reorder or corrupt dozens of marker pairs and thereby damage the
        /// translation itself. A uniformly bold paragraph already carries its
        /// weight in <see cref="PdfParagraph.IsBold"/>, so it needs no inline
        /// markers. Adjacent bold runs in mixed paragraphs are coalesced.
        /// </summary>
        public static string NormalizeStyleRuns(
            string plainText,
            string styledText,
            bool isUniformlyBold)
        {
            if (isUniformlyBold) return plainText;
            if (string.IsNullOrEmpty(styledText)) return styledText;
            return AdjacentBoldRuns.Replace(styledText, " ");
        }

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
            var sequence = new Regex($"(?<!\\d){markerSequence}(?!\\d)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
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
