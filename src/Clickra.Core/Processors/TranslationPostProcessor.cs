using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clickra.Core.Processors
{
    public static class TranslationPostProcessor
    {
        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang)
        {
            if (string.IsNullOrEmpty(translatedText)) return translatedText;

            // 1. Restore email addresses
            try
            {
                var emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                var originalEmails = emailRegex.Matches(originalText).Cast<Match>().Select(m => m.Value).ToList();
                if (originalEmails.Count > 0)
                {
                    var transEmailRegex = new Regex(
                        @"[a-zA-Z0-9._%+-]+\s*@\s*[a-zA-Z0-9.-]+(?:\s*\.\s*[a-zA-Z]{2,})+");
                    var transMatches = transEmailRegex.Matches(translatedText).Cast<Match>().ToList();
                    for (int i = 0; i < Math.Min(originalEmails.Count, transMatches.Count); i++)
                    {
                        int index = translatedText.IndexOf(transMatches[i].Value);
                        if (index >= 0)
                        {
                            translatedText = translatedText.Remove(index, transMatches[i].Value.Length).Insert(index, originalEmails[i]);
                        }
                    }
                }
            }
            catch { }

            // 2. Terminology replacements
            if (targetLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                bool isTraditional = !targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);

                if (originalText.Trim().Equals("ABSTRACT", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = "摘要";
                }
                else
                {
                    translatedText = translatedText.Replace("抽象", "摘要");
                }

                if (ReferenceSectionDetector.IsHeadingText(originalText.Trim()))
                {
                    string prefix = ReferenceSectionDetector.GetHeadingNumberPrefix(originalText.Trim());
                    translatedText = prefix + (isTraditional ? "參考文獻" : "参考文献");
                }

                if (originalText.Contains("title", StringComparison.OrdinalIgnoreCase) && !originalText.Contains("entitle", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("標題", isTraditional ? "作品" : "作品");
                    translatedText = translatedText.Replace("标题", "作品");
                }

                if (originalText.Contains("features", StringComparison.OrdinalIgnoreCase) || originalText.Contains("feature", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("功能", isTraditional ? "特徵" : "特征");
                    translatedText = translatedText.Replace("特性", isTraditional ? "特徵" : "特征");
                }

                if (originalText.Contains("character", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("字元", "角色");
                    translatedText = translatedText.Replace("字符", "角色");
                }

                if (originalText.Contains("LLM", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("法學碩士", isTraditional ? "大型語言模型" : "大型语言模型");
                    translatedText = translatedText.Replace("法学硕士", isTraditional ? "大型語言模型" : "大型语言模型");
                }

                if (originalText.Contains("sink", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("水槽", isTraditional ? "接收端" : "接收器");
                }
            }

            // 3. Remove stray formula-bracket artifacts like '):(Equation (1))' or '):' that appear
            //    when the formula extractor incorrectly consumed the opening '(' of a parenthetical phrase,
            //    leaving only the closing ')' in the text string.
            try
            {
                var fullArtifactRegex = new Regex(
                    @"^(.+?)\)\s*:\s*\(.+\)\s*$",
                    RegexOptions.Singleline);
                var fullMatch = fullArtifactRegex.Match(translatedText.Trim());
                if (fullMatch.Success)
                {
                    translatedText = fullMatch.Groups[1].Value.Trim();
                }
                else
                {
                    var trailingArtifact = new Regex(
                        @"\)\s*:\s*\(.+\)\s*$",
                        RegexOptions.Singleline);
                    translatedText = trailingArtifact.Replace(translatedText, "").Trim();
                }

                var leadingArtifact = new Regex(
                    @"^\)\s*:\s*",
                    RegexOptions.None);
                translatedText = leadingArtifact.Replace(translatedText, "").Trim();
            }
            catch { }

            // 4. Convert Simplified Chinese to Traditional when target is zh-TW/zh-HK
            if (targetLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
                !targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                translatedText = ChineseTextConverter.SimplifiedToTraditional(translatedText);
            }

            return translatedText;
        }
    }
}
