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
            if (IsAlgorithmPseudoCodeLine(textWithoutPlaceholders)) return true;

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

        internal static bool IsAlgorithmPseudoCodeLine(string text)
        {
            text = Regex.Replace(text, @"\{v\d+\}", " = ", RegexOptions.None, TimeSpan.FromSeconds(1));

            if (Regex.IsMatch(text, @"^\s*(?:Algorithm\s+\d+|Procedure\b|Input:|Output:|Step\s+\d+|/\*)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                return true;

            int wordCount = Regex.Matches(text, @"\b[A-Za-z][A-Za-z-]*\b", RegexOptions.None, TimeSpan.FromSeconds(1)).Count;
            if (wordCount >= 18 && text.Contains('.'))
                return false;

            int operatorCount = Regex.Matches(text, @"←|==|!=|<=|>=|:=|=|\[[^\]]*\]|\([^)]*\)|\.[A-Za-z_]\w*", RegexOptions.None, TimeSpan.FromSeconds(1)).Count;
            int keywordCount = Regex.Matches(text, @"\b(?:return|if|then|else|for|while|do|None|null|true|false|append|parse|get_child)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Count;
            int identifierCount = Regex.Matches(text, @"\b(?:ast|macro|macro_op|macro_node|args_node|child|operator|params?|temp_var|temp_count|oracle|input_oracle)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Count;
            return operatorCount >= 1 && (keywordCount >= 1 || identifierCount >= 2);
        }
    }
}
