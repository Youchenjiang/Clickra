using System.Text;
using System.Text.RegularExpressions;
using Clickra.Core;
using Clickra.Core.Processors;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Models
{
    public class PdfParagraph
    {
        public enum TextAlignment
        {
            Left,
            Center,
            Right
        }

        public static readonly Regex MathFontRegex = new(
            @"^(?:CMMI|CMSY|CMEX|CMIB|CMBSY|cmmi|cmsy|cmex|cmib|cmbsy|lasy|rsfs|txsy|wasy|stmary|XY|bbld|line\d*|lcircle\d*|TeX-|MS[AB]|MT(?:MI|SY|EX|2)|EU[RSF])|" +
            @"(?:Mono|Code|Math|Sym|Wingdings|Webdings|Dingbats|Courier|Console|Inconsolata|Typewriter|NimbusMon|MonL|cmtt|ectt|sftt|\btt\d+|Teletype)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public string TextWithPlaceholders { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double OriginalX0 { get; private set; }
        public double OriginalY0 { get; private set; }
        public double OriginalX1 { get; private set; }
        public double OriginalY1 { get; private set; }
        public double AverageFontSize { get; set; }
        public bool IsOnlyMath { get; set; }
        public bool IsCode { get; set; }
        public bool IsBypassed { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsTable { get; set; }
        public bool IsDiagram { get; set; }
        /// <summary>Paragraph belongs to a gray System Message / Prompt box; keep English.</summary>
        public bool IsGrayPromptContent { get; set; }
        public bool brk { get; set; }
        public List<MathFormula> Formulas { get; set; } = new List<MathFormula>();
        public object TextDirection { get; set; } = "Rotate0";
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public List<PdfLetter> AllLetters { get; set; } = new List<PdfLetter>();
        public List<ParagraphAnnotationInfo> Annotations { get; set; } = new();

        public double Width => X1 - X0;
        public double Height => Y1 - Y0;

        private string GetLetterDirection(UglyToad.PdfPig.Content.Letter letter)
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

        public PdfParagraph(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                X0 = 0; Y0 = 0; X1 = 0; Y1 = 0;
                return;
            }

            X0 = lines.Min(line => Math.Min(line.BoundingBox.Left, line.BoundingBox.Right));
            Y0 = lines.Min(line => Math.Min(line.BoundingBox.Bottom, line.BoundingBox.Top));
            X1 = lines.Max(line => Math.Max(line.BoundingBox.Left, line.BoundingBox.Right));
            Y1 = lines.Max(line => Math.Max(line.BoundingBox.Bottom, line.BoundingBox.Top));

            OriginalX0 = X0;
            OriginalY0 = Y0;
            OriginalX1 = X1;
            OriginalY1 = Y1;

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
            if (directions.Count > 0)
            {
                TextDirection = directions.OrderByDescending(kv => kv.Value).First().Key;
            }

            AnalyzeLines(lines);

            double totalLeftGap = 0;
            double totalRightGap = 0;
            int lineCountWithGaps = 0;
            foreach (var line in lines)
            {
                double leftGap = line.BoundingBox.Left - X0;
                double rightGap = X1 - line.BoundingBox.Right;
                if (leftGap > 5 && rightGap > 5)
                {
                    totalLeftGap += leftGap;
                    totalRightGap += rightGap;
                    lineCountWithGaps++;
                }
            }
            if (lineCountWithGaps > 0)
            {
                double avgLeft = totalLeftGap / lineCountWithGaps;
                double avgRight = totalRightGap / lineCountWithGaps;
                double diff = Math.Abs(avgLeft - avgRight);
                if (diff < 15)
                {
                    Alignment = TextAlignment.Center;
                }
                else if (avgLeft > avgRight + 15)
                {
                    Alignment = TextAlignment.Right;
                }
                else
                {
                    Alignment = TextAlignment.Left;
                }
            }
            else
            {
                Alignment = TextAlignment.Left;
            }

            string trimmedText = TextWithPlaceholders.Trim();
            bool isReference = Regex.IsMatch(trimmedText, @"^\[\d+\]") ||
                              trimmedText.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                              trimmedText.Contains("doi:", StringComparison.OrdinalIgnoreCase) ||
                              trimmedText.Contains("www.", StringComparison.OrdinalIgnoreCase);

            if (isReference)
            {
                Alignment = TextAlignment.Left;
            }
        }

        private void AnalyzeLines(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> lines)
        {
            var sb = new StringBuilder();
            var currentFormula = new List<UglyToad.PdfPig.Content.Letter>();
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
                        if (letter.FontName != null)
                        {
                            if (letter.FontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("it", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("ob", StringComparison.OrdinalIgnoreCase))
                            {
                                italicCount++;
                            }
                        }

                        AllLetters.Add(new PdfLetter
                        {
                            Value = letter.Value ?? "",
                            FontName = letter.FontName ?? "Times New Roman",
                            FontSize = letter.PointSize,
                            X = letter.Location.X,
                            Y = letter.Location.Y,
                            Left = letter.GlyphRectangle.Left,
                            Bottom = letter.GlyphRectangle.Bottom,
                            Right = letter.GlyphRectangle.Right,
                            Top = letter.GlyphRectangle.Top
                        });
                    }
                }
            }
            AverageFontSize = letterCount > 0 ? totalFontSize / letterCount : 10;
            IsBold = totalCount > 0 && ((double)boldCount / totalCount) > 0.5;
            IsItalic = totalCount > 0 && ((double)italicCount / totalCount) > 0.5;

            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var line = lines[lineIdx];
                for (int wordIdx = 0; wordIdx < line.Words.Count; wordIdx++)
                {
                    var word = line.Words[wordIdx];
                    bool isMathWord = IsMathWord(word);
                    for (int letterIdx = 0; letterIdx < word.Letters.Count; letterIdx++)
                    {
                        var letter = word.Letters[letterIdx];
                        bool isMath = IsMathCharacter(letter, isMathWord);

                        bool curV = isMath;
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
                            if (currentFormula.Count > 0)
                            {
                                int id = Formulas.Count;
                                Formulas.Add(new MathFormula(id, currentFormula));
                                sb.Append($"{{v{id}}}");
                                currentFormula.Clear();
                                bracketsCount = 0;
                            }

                            sb.Append(letter.Value);
                        }
                    }

                    if (wordIdx < line.Words.Count - 1)
                    {
                        if (currentFormula.Count == 0)
                        {
                            sb.Append(" ");
                        }
                    }
                }

                if (lineIdx < lines.Count - 1)
                {
                    if (currentFormula.Count > 0)
                    {
                        int id = Formulas.Count;
                        Formulas.Add(new MathFormula(id, currentFormula));
                        sb.Append($"{{v{id}}}");
                        currentFormula.Clear();
                        bracketsCount = 0;
                    }
                    sb.Append(" ");
                    brk = true;
                }
            }

            if (currentFormula.Count > 0)
            {
                int id = Formulas.Count;
                Formulas.Add(new MathFormula(id, currentFormula));
                sb.Append($"{{v{id}}}");
            }

            TextWithPlaceholders = sb.ToString();
            IsOnlyMath = Formulas.Count == 1 && TextWithPlaceholders.Trim() == "{v0}";
            IsCode = IsCodeBlock(TextWithPlaceholders) || IsMonospaceBlock(lines);
        }

        private bool IsMonospaceBlock(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> lines)
        {
            int monoCount = 0;
            int totalCount = 0;
            foreach (var line in lines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        totalCount++;
                        var fontName = letter.FontName;
                        if (fontName != null)
                        {
                            string cleanFontName = fontName;
                            int plusIdx = fontName.IndexOf('+');
                            if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                            {
                                cleanFontName = fontName.Substring(plusIdx + 1);
                            }
                            if (cleanFontName.Contains("Type3", StringComparison.OrdinalIgnoreCase) ||
                                (MathFontRegex.IsMatch(cleanFontName) &&
                                 (cleanFontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Inconsolata", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Typewriter", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("NimbusMon", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("MonL", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("cmtt", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("ectt", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("sftt", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Teletype", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Code", StringComparison.OrdinalIgnoreCase))))
                            {
                                monoCount++;
                            }
                        }
                    }
                }
            }
            return totalCount > 0 && ((double)monoCount / totalCount) > 0.6;
        }

        private bool IsCodeBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var lineNumRegex = new Regex(@"^[ \t]*\d+:", RegexOptions.Multiline);
            if (lineNumRegex.Matches(text).Count >= 2) return true;

            string textWithoutPlaceholders = Regex.Replace(text, @"\{v\d+\}", "");
            bool containsBrace = textWithoutPlaceholders.Contains("{") || textWithoutPlaceholders.Contains("}");
            if (containsBrace)
            {
                var codeKeywordsRegex = new Regex(
                    @"\b(function|const|let|typeof|module|exports|import|require|return|public|private|class|void|int|string|boolean|var|for|if|while)\b",
                    RegexOptions.IgnoreCase
                );
                int keywordMatches = codeKeywordsRegex.Matches(textWithoutPlaceholders).Count;

                var proseWordsRegex = new Regex(
                    @"\b(the|this|that|with|from|these|those|which|where|when|because|although|however|therefore)\b",
                    RegexOptions.IgnoreCase
                );
                int proseMatches = proseWordsRegex.Matches(textWithoutPlaceholders).Count;

                if (keywordMatches >= 3 && proseMatches <= 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMathCodepoint(int codepoint)
        {
            if ((codepoint >= 0x0370 && codepoint <= 0x03FF) || (codepoint >= 0x1F00 && codepoint <= 0x1FFF)) return true;
            if (codepoint >= 0x2200 && codepoint <= 0x22FF) return true;
            if (codepoint >= 0x2A00 && codepoint <= 0x2AFF) return true;
            if (codepoint >= 0x2100 && codepoint <= 0x214F) return true;
            if (codepoint >= 0x2190 && codepoint <= 0x21FF) return true;
            if (codepoint >= 0x27F0 && codepoint <= 0x27FF) return true;
            if (codepoint >= 0x2900 && codepoint <= 0x297F) return true;
            if ((codepoint >= 0x27C0 && codepoint <= 0x27EF) || (codepoint >= 0x2980 && codepoint <= 0x29FF)) return true;
            if (codepoint >= 0x1D400 && codepoint <= 0x1D7FF) return true;
            return false;
        }

        private static IEnumerable<int> GetCodepoints(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            for (int i = 0; i < s.Length; i++)
            {
                if (i < s.Length - 1 && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                {
                    yield return char.ConvertToUtf32(s[i], s[i + 1]);
                    i++;
                }
                else
                {
                    yield return s[i];
                }
            }
        }

        private bool IsMathWord(UglyToad.PdfPig.Content.Word word)
        {
            foreach (var letter in word.Letters)
            {
                var fontName = letter.FontName;
                if (fontName != null)
                {
                    string cleanFontName = fontName;
                    int plusIdx = fontName.IndexOf('+');
                    if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                    {
                        cleanFontName = fontName.Substring(plusIdx + 1);
                    }
                    if (MathFontRegex.IsMatch(cleanFontName))
                    {
                        return true;
                    }
                }

                if (letter.Value != null)
                {
                    if (letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase)) return true;
                    foreach (int cp in GetCodepoints(letter.Value))
                    {
                        if (IsMathCodepoint(cp)) return true;
                    }
                }
            }
            return false;
        }

        private bool IsMathCharacter(UglyToad.PdfPig.Content.Letter letter, bool isMathWord)
        {
            if (letter.Value == "\u2022" || letter.Value == "\u2022")
            {
                return false;
            }

            var fontName = letter.FontName;
            if (fontName != null)
            {
                string cleanFontName = fontName;
                int plusIdx = fontName.IndexOf('+');
                if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                {
                    cleanFontName = fontName.Substring(plusIdx + 1);
                }

                if (MathFontRegex.IsMatch(cleanFontName))
                {
                    return true;
                }
            }

            if (letter.Value != null && letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase)) return true;

            if (letter.Value != null)
            {
                foreach (int cp in GetCodepoints(letter.Value))
                {
                    if (IsMathCodepoint(cp)) return true;
                }

                if (isMathWord && letter.PointSize < AverageFontSize * 0.79) return true;
            }

            return false;
        }

        public static bool IsMathLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line)
        {
            if (Regex.IsMatch(line.Text.Trim(), @"\(\d+\)\s*$")) return true;

            if (Regex.IsMatch(line.Text.Trim(), @"^\s*(?:[•\-*]|\d+[\.\)]|[a-zA-Z][\.\)]|\[\d+\])\s*$")) return false;

            int proseLetters = 0;
            foreach (var word in line.Words)
            {
                bool isProseWord = true;
                if (word.Letters.Count <= 1)
                {
                    isProseWord = false;
                }
                else
                {
                    int nonAlphaCount = word.Letters.Count(l => l.Value.Length > 0 && !char.IsLetter(l.Value[0]));
                    if ((double)nonAlphaCount / word.Letters.Count > 0.3)
                    {
                        isProseWord = false;
                    }
                    else
                    {
                        foreach (var letter in word.Letters)
                        {
                            var fontName = letter.FontName;
                            if (fontName != null)
                            {
                                string cleanFontName = fontName;
                                int plusIdx = fontName.IndexOf('+');
                                if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                                {
                                    cleanFontName = fontName.Substring(plusIdx + 1);
                                }
                                if (MathFontRegex.IsMatch(cleanFontName))
                                {
                                    isProseWord = false;
                                    break;
                                }
                            }
                            if (letter.Value != null)
                            {
                                if (letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase))
                                {
                                    isProseWord = false;
                                    break;
                                }
                                foreach (int cp in GetCodepoints(letter.Value))
                                {
                                    if (IsMathCodepoint(cp))
                                    {
                                        isProseWord = false;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (isProseWord)
                {
                    proseLetters += word.Letters.Count;
                }
            }

            return proseLetters <= 2;
        }

        public static IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> MergeHorizontalLines(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> initialLines)
        {
            if (initialLines == null || initialLines.Count <= 1) return initialLines ?? new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();

            var groups = new List<List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>>();
            foreach (var line in initialLines.OrderByDescending(l => l.BoundingBox.Centroid.Y))
            {
                bool added = false;
                foreach (var g in groups)
                {
                    double avgY = g.Average(l => l.BoundingBox.Centroid.Y);
                    if (Math.Abs(line.BoundingBox.Centroid.Y - avgY) < 3.5)
                    {
                        g.Add(line);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line });
                }
            }

            var result = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
            foreach (var g in groups)
            {
                if (g.Count == 1)
                {
                    result.Add(g[0]);
                }
                else
                {
                    var sortedGroup = g.OrderBy(l => l.BoundingBox.Left).ToList();
                    var allWords = sortedGroup.SelectMany(l => l.Words).OrderBy(w => w.BoundingBox.Left).ToList();
                    var mergedLine = new UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine(allWords, " ");
                    result.Add(mergedLine);
                }
            }

            return result.OrderByDescending(l => l.BoundingBox.Centroid.Y).ToList();
        }

        public void MergeWith(PdfParagraph other)
        {
            if (other == null) return;

            int formulaIdOffset = this.Formulas.Count;
            foreach (var formula in other.Formulas)
            {
                var newFormula = new MathFormula
                {
                    Id = formula.Id + formulaIdOffset,
                    Letters = formula.Letters,
                    Width = formula.Width
                };
                this.Formulas.Add(newFormula);
            }

            string otherText = other.TextWithPlaceholders;
            if (formulaIdOffset > 0)
            {
                otherText = Regex.Replace(otherText, @"\{v(\d+)\}", m =>
                {
                    int oldId = int.Parse(m.Groups[1].Value);
                    return $"{{v{oldId + formulaIdOffset}}}";
                });
            }

            if (string.IsNullOrWhiteSpace(this.TextWithPlaceholders))
            {
                this.TextWithPlaceholders = otherText;
            }
            else
            {
                this.TextWithPlaceholders = this.TextWithPlaceholders + " " + otherText;
            }

            this.AllLetters.AddRange(other.AllLetters);

            this.X0 = Math.Min(this.X0, other.X0);
            this.Y0 = Math.Min(this.Y0, other.Y0);
            this.X1 = Math.Max(this.X1, other.X1);
            this.Y1 = Math.Max(this.Y1, other.Y1);

            this.OriginalX0 = Math.Min(this.OriginalX0, other.OriginalX0);
            this.OriginalY0 = Math.Min(this.OriginalY0, other.OriginalY0);
            this.OriginalX1 = Math.Max(this.OriginalX1, other.OriginalX1);
            this.OriginalY1 = Math.Max(this.OriginalY1, other.OriginalY1);

            if (this.AllLetters.Count > 0)
            {
                this.AverageFontSize = this.AllLetters.Average(l => l.FontSize);
            }

            this.brk = true;
            this.IsBold = this.IsBold || other.IsBold;
            this.IsOnlyMath = this.Formulas.Count == 1 && this.TextWithPlaceholders.Trim() == "{v0}";
            this.IsCode = this.IsCode || other.IsCode;
            this.IsDiagram = this.IsDiagram || other.IsDiagram;
            this.IsGrayPromptContent = this.IsGrayPromptContent || other.IsGrayPromptContent;
        }
    }
}
