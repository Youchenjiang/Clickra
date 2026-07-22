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
        private static readonly Regex EmailRegex =
            new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
        private static readonly Regex TranslatedEmailRegex =
            new(@"[a-zA-Z0-9._%+-]+\s*@\s*[a-zA-Z0-9.-]+(?:\s*\.\s*[a-zA-Z]{2,})+",
                RegexOptions.Compiled);
        private static readonly Regex FullFormulaArtifactRegex =
            new(@"^(.+?)\)\s*:\s*\(.+\)\s*$", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex TrailingFormulaArtifactRegex =
            new(@"\)\s*:\s*\(.+\)\s*$", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex LeadingFormulaArtifactRegex =
            new(@"^\)\s*:\s*", RegexOptions.Compiled);
        private static readonly Regex TestGenerationSourceRegex =
            new(@"\b(?:unit\s+)?tests?(?:\s+case)?\s+generation\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LlmGenerationContinuationRegex =
            new(@"^\s*generation\s+(?:with|using)\s+(?:an?\s+)?llms?\s*[.!?]?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CjkInternalWhitespaceRegex =
            new(@"(?<=[\u3400-\u9fff])\s+(?=[\u3400-\u9fff])", RegexOptions.Compiled);
        private static readonly Regex CjkBeforePunctuationWhitespaceRegex =
            new(@"(?<=[\u3400-\u9fff])\s+(?=[，。！？；：、）】》])", RegexOptions.Compiled);
        private static readonly Regex CjkAfterOpeningPunctuationWhitespaceRegex =
            new(@"(?<=[（【《])\s+(?=[\u3400-\u9fff])", RegexOptions.Compiled);
        private static readonly Regex AbstractDashRegex =
            new(@"^\s*(?:摘要|抽象)\s*[-–—:：]\s*", RegexOptions.Compiled);

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

            if (targetLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                translatedText = NormalizeChineseTypography(translatedText);
            }

            return translatedText;
        }

        private static string NormalizeChineseTypography(string translatedText)
        {
            translatedText = AbstractDashRegex.Replace(translatedText, "摘要—");
            translatedText = CjkInternalWhitespaceRegex.Replace(translatedText, "");
            translatedText = CjkBeforePunctuationWhitespaceRegex.Replace(translatedText, "");
            translatedText = CjkAfterOpeningPunctuationWhitespaceRegex.Replace(translatedText, "");
            return translatedText;
        }

        private static string RestoreEmails(string originalText, string translatedText)
        {
            try
            {
                var originalEmails = EmailRegex.Matches(originalText).Cast<Match>().Select(m => m.Value).ToList();
                if (originalEmails.Count > 0)
                {
                    var transMatches = TranslatedEmailRegex.Matches(translatedText).Cast<Match>().ToList();
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

            translatedText = NormalizeTestGenerationTerminology(
                originalText,
                translatedText,
                isTraditional);

            if (originalText.Contains("sink", StringComparison.OrdinalIgnoreCase))
            {
                translatedText = translatedText.Replace("水槽", isTraditional ? "接收端" : "接收器");
            }

            return translatedText;
        }

        private static string NormalizeTestGenerationTerminology(
            string originalText,
            string translatedText,
            bool isTraditional)
        {
            if (LlmGenerationContinuationRegex.IsMatch(originalText))
            {
                return isTraditional
                    ? "使用大型語言模型生成"
                    : "使用大型语言模型生成";
            }

            if (!TestGenerationSourceRegex.IsMatch(originalText))
                return translatedText;

            return translatedText
                .Replace("測試一代", "測試生成")
                .Replace("测试一代", "测试生成");
        }

        private static string RemoveFormulaArtifacts(string translatedText)
        {
            try
            {
                var fullMatch = FullFormulaArtifactRegex.Match(translatedText.Trim());
                if (fullMatch.Success)
                {
                    translatedText = fullMatch.Groups[1].Value.Trim();
                }
                else
                {
                    translatedText = TrailingFormulaArtifactRegex.Replace(translatedText, "").Trim();
                }

                translatedText = LeadingFormulaArtifactRegex.Replace(translatedText, "").Trim();
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
