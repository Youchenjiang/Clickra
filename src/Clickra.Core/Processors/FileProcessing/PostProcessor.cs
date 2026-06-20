using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clickra.Core.Processors
{
    public static class PostProcessor
    {
        private static readonly Regex ReferencesSectionNumberedHeadingRegex =
            new(@"^(\d{1,2})\.\s*(?:REFERENCES?|BIBLIOGRAPHY|參考文獻)\s*\.?\s*$",
                RegexOptions.Compiled);

        public static string Process(string originalText, string translatedText, string targetLang)
        {
            if (string.IsNullOrEmpty(translatedText)) return translatedText;

            translatedText = RestoreEmails(originalText, translatedText);

            if (targetLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                bool isTraditional = !targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);
                translatedText = ApplyTerminologyReplacements(originalText, translatedText, isTraditional);
            }

            translatedText = RemoveFormulaArtifacts(translatedText);

            if (targetLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
                !targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                translatedText = ChineseTextConverter.SimplifiedToTraditional(translatedText);
            }

            return translatedText;
        }

        private static string RestoreEmails(string originalText, string translatedText)
        {
            try
            {
                var emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                var originalEmails = emailRegex.Matches(originalText).Cast<Match>().Select(m => m.Value).ToList();
                if (originalEmails.Count > 0)
                {
                    var transEmailRegex = new Regex(@"[a-zA-Z0-9._%+-]+\s*@\s*[a-zA-Z0-9.-]+(?:\s*\.\s*[a-zA-Z]{2,})+");
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
            return translatedText;
        }

        private static string ApplyTerminologyReplacements(string originalText, string translatedText, bool isTraditional)
        {
            if (originalText.Trim().Equals("ABSTRACT", StringComparison.OrdinalIgnoreCase))
            {
                translatedText = "摘要";
            }
            else
            {
                translatedText = translatedText.Replace("抽象", "摘要");
            }

            if (IsReferencesSectionHeading(originalText.Trim()))
            {
                var headingMatch = ReferencesSectionNumberedHeadingRegex.Match(originalText.Trim());
                string prefix = headingMatch.Success ? $"{headingMatch.Groups[1].Value}. " : "";
                translatedText = prefix + (isTraditional ? "參考文獻" : "参考文献");
            }

            if (originalText.Contains("title", StringComparison.OrdinalIgnoreCase) &&
                !originalText.Contains("entitle", StringComparison.OrdinalIgnoreCase))
            {
                translatedText = translatedText.Replace("標題", isTraditional ? "作品" : "作品");
                translatedText = translatedText.Replace("标题", "作品");
            }

            if (originalText.Contains("features", StringComparison.OrdinalIgnoreCase) ||
                originalText.Contains("feature", StringComparison.OrdinalIgnoreCase))
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

            return translatedText;
        }

        private static string RemoveFormulaArtifacts(string translatedText)
        {
            try
            {
                var fullArtifactRegex = new Regex(@"^(.+?)\)\s*:\s*\(.+\)\s*$", RegexOptions.Singleline);
                var fullMatch = fullArtifactRegex.Match(translatedText.Trim());
                if (fullMatch.Success)
                {
                    translatedText = fullMatch.Groups[1].Value.Trim();
                }
                else
                {
                    var trailingArtifact = new Regex(@"\)\s*:\s*\(.+\)\s*$", RegexOptions.Singleline);
                    translatedText = trailingArtifact.Replace(translatedText, "").Trim();
                }

                var leadingArtifact = new Regex(@"^\)\s*:\s*");
                translatedText = leadingArtifact.Replace(translatedText, "").Trim();
            }
            catch { }
            return translatedText;
        }

        private static bool IsReferencesSectionHeading(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return false;
            return ReferencesSectionNumberedHeadingRegex.IsMatch(txt) ||
                   txt.Equals("REFERENCES", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("REFERENCE", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("BIBLIOGRAPHY", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("參考文獻", StringComparison.Ordinal);
        }
    }
}
