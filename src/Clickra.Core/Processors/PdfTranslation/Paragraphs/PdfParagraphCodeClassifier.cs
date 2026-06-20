using Clickra.Core.Models;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Processors
{
    internal static class PdfParagraphCodeClassifier
    {
        public static bool IsMonospaceBlock(IReadOnlyList<TextLine> lines)
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
                        if (fontName == null) continue;

                        string cleanFontName = PdfParagraphMathClassifier.CleanFontName(fontName);
                        if (cleanFontName.Contains("Type3", StringComparison.OrdinalIgnoreCase) ||
                            (PdfParagraph.MathFontRegex.IsMatch(cleanFontName) &&
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
            return totalCount > 0 && ((double)monoCount / totalCount) > 0.6;
        }

        public static bool IsCodeBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var lineNumRegex = new Regex(@"^[ \t]*\d+:", RegexOptions.Multiline);
            if (lineNumRegex.Matches(text).Count >= 2) return true;

            string textWithoutPlaceholders = Regex.Replace(text, @"\{v\d+\}", "");
            bool containsBrace = textWithoutPlaceholders.Contains("{") || textWithoutPlaceholders.Contains("}");
            if (!containsBrace) return false;

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

            return keywordMatches >= 3 && proseMatches <= 1;
        }
    }
}
