using System;
using System.Collections.Generic;

namespace Clickra.Core
{
    public static partial class Localization
    {
        private const string LangTw = "zh-TW";
        private const string LangCn = "zh-CN";
        private const string LangEn = "en-US";
        private const string LangJa = "ja-JP";
        private const string LangKo = "ko-KR";
        private const string AppExcel = "Excel";
        private const string KeyCmdCompressPdf = "cmd_compress_pdf";
        private const string KeyLibreOfficeExternalNote = "setting_libreoffice_external_note";
        private const string KeyLibreOfficeExternalHint = "setting_libreoffice_external_hint";
        private const string MicrosoftOfficeLabel = "Microsoft Office";
        private const string LibreOfficeLabel = "LibreOffice";
        private const string WordToPdfLabel = "Word → PDF";
        private const string ExcelToPdfLabel = "Excel → PDF";
        private const string PptToPdfLabel = "PPT → PDF";
        private const string MarkdownToPdfLabel = "Markdown → PDF";
        private const string MarkdownToWordLabel = "Markdown → Word";
        private const string MarkdownParsingZh = "正在解析 Markdown：{0}";
        private const string RetentionDayZh = "{0} 天";
        private const string OfficeName = "Office";

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
        {
            [LangTw] = new(StringComparer.OrdinalIgnoreCase),
            [LangCn] = new(StringComparer.OrdinalIgnoreCase),
            [LangEn] = new(StringComparer.OrdinalIgnoreCase),
            [LangJa] = new(StringComparer.OrdinalIgnoreCase),
            [LangKo] = new(StringComparer.OrdinalIgnoreCase)
        };

        /// <summary>Maps a language code (or the current UI culture when empty) to one of the
        /// supported language keys, defaulting to Traditional Chinese.</summary>
        public static string NormalizeLanguageCode(string langCode)
        {
            if (string.IsNullOrEmpty(langCode))
            {
                langCode = System.Globalization.CultureInfo.CurrentUICulture.Name;
            }

            if (langCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return LangEn;
            if (langCode.Equals(LangCn, StringComparison.OrdinalIgnoreCase)) return LangCn;
            if (langCode.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return LangJa;
            if (langCode.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return LangKo;
            if (langCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return LangTw;

            return LangTw;
        }

        /// <summary>Translates a resource key into the target language, falling back to
        /// Traditional Chinese and finally to the key itself.</summary>
        public static string T(string key, string langCode)
        {
            string targetKey = NormalizeLanguageCode(langCode);

            // Try looking up the logical ID in the target language
            if (Translations.TryGetValue(targetKey, out var dict) && dict.TryGetValue(key, out var translated))
            {
                return translated;
            }

            // Fallback: If not found in target language, try Traditional Chinese (zh-TW)
            if (targetKey != LangTw && Translations.TryGetValue(LangTw, out var twDict) && twDict.TryGetValue(key, out var twTranslated))
            {
                return twTranslated;
            }

            // Fallback: If not found anywhere, return the key as-is
            return key;
        }

        /// <summary>
        /// Translates a resource key using the currently configured language setting.
        /// </summary>
        public static string T(string key) =>
            T(key, ClickraStorage.GetSetting(ClickraSettings.Language));

        /// <summary>
        /// Translates a resource key using the currently configured language setting and formats it with arguments.
        /// </summary>
        public static string T(string key, params object[] args) =>
            args is { Length: > 0 } ? string.Format(T(key), args) : T(key);

        /// <summary>Canonical list of the 5 supported language codes.</summary>
        public static IReadOnlyList<string> SupportedLanguages => new[] { LangTw, LangCn, LangEn, LangJa, LangKo };

        /// <summary>
        /// Checks if an exact, non-empty translation exists for the given key in the specified language (bypassing fallback to zh-TW).
        /// </summary>
        public static bool HasExactTranslation(string key, string langCode)
        {
            string targetKey = NormalizeLanguageCode(langCode);
            return Translations.TryGetValue(targetKey, out var dict) &&
                   dict.TryGetValue(key, out var val) &&
                   !string.IsNullOrWhiteSpace(val);
        }

        /// <summary>
        /// Returns all unique translation keys registered in any language dictionary.
        /// </summary>
        public static IReadOnlyCollection<string> GetAllKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dict in Translations.Values)
            {
                foreach (var k in dict.Keys)
                {
                    keys.Add(k);
                }
            }
            return keys;
        }

        /// <summary>
        /// Analyzes translations for the specified keys (or all registered keys if null) across
        /// the given languages (or all supported languages if null), and returns a dictionary
        /// mapping each language to its list of missing keys.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> FindMissingTranslations(
            IEnumerable<string>? keysToCheck = null,
            IEnumerable<string>? languagesToCheck = null)
        {
            var langs = (languagesToCheck ?? SupportedLanguages).ToArray();
            var keys = (keysToCheck ?? GetAllKeys()).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string lang in langs)
            {
                List<string> missing = keys
                    .Where(key => !HasExactTranslation(key, lang))
                    .ToList();

                if (missing.Count > 0)
                {
                    result[lang] = missing;
                }
            }

            return result;
        }

        /// <summary>
        /// Formats a human-readable diagnosis report grouping missing keys by language.
        /// </summary>
        public static string FormatMissingReport(IReadOnlyDictionary<string, IReadOnlyList<string>> missing)
        {
            if (missing.Count == 0) return string.Empty;

            int total = 0;
            foreach (var list in missing.Values) total += list.Count;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Missing translations detected across languages (total missing: {total} in {missing.Count} language(s)):");

            foreach (var kvp in missing.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine($"[{kvp.Key}] ({kvp.Value.Count} missing):");
                foreach (string key in kvp.Value)
                {
                    sb.AppendLine($"  - {key}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        static Localization()
        {
            RegisterGeneralTranslations();
            RegisterCompressionTranslations();
            RegisterProcessingTranslations();
            RegisterLibreOfficeManagementTranslations();
            RegisterFluentDashboardTranslations();
            RegisterCliTranslations();
            RegisterDiagnosticsEmailTranslations();
        }


        private static void RegisterTranslations((string Key, string Tw, string Cn, string En, string Ja, string Ko)[] data)
        {
            foreach (var item in data)
            {
                Translations[LangTw][item.Key] = item.Tw;
                Translations[LangCn][item.Key] = item.Cn;
                Translations[LangEn][item.Key] = item.En;
                Translations[LangJa][item.Key] = item.Ja;
                Translations[LangKo][item.Key] = item.Ko;
            }
        }

        /// <summary>
        /// 組裝本地化的診斷回報郵件主旨與內文範本。
        /// </summary>
        public static (string Subject, string Body) BuildDiagnosticsEmail(string version, string time, string? langCode = null)
        {
            string lang = langCode ?? ClickraStorage.GetSetting(ClickraSettings.Language);
            string subject = T("diag_email_subject", lang);
            string body =
                T("diag_email_thanks", lang) + "\r\n\r\n" +
                T("diag_email_attachment_hint", lang) + "\r\n\r\n" +
                T("diag_email_system_info", lang) + "\r\n" +
                T("diag_email_os", lang) + "\r\n" +
                string.Format(T("diag_email_version", lang), version) + "\r\n" +
                string.Format(T("diag_email_time", lang), time) + "\r\n\r\n" +
                T("diag_email_problem_desc", lang) + "\r\n" +
                T("diag_email_problem_placeholder", lang);

            return (subject, body);
        }
    }
}
