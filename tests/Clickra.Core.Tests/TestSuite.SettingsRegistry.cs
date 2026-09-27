using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

/// <summary>
/// 設定登錄表的守門：鍵與預設值只能在 ClickraSettings.cs 定義一次；任何讀取端
/// 都不得把鍵寫成字串、不得自行宣告鍵常數、不得在呼叫點發明預設值。
/// </summary>
static partial class TestSuite
{
    private static readonly TimeSpan SettingsRegexTimeout = TimeSpan.FromSeconds(1);
    private const string LiteralSettingKeyPattern = @"(GetSetting|SaveSetting)\(\s*""";
    private const string SettingKeyDeclarationPattern = @"const\s+string\s+\w+\s*=\s*""([^""]+)""";
    private const string NullCoalescedSettingPattern = @"GetSetting\([^)]*\)\s*\?\?";
    private const string LiteralSettingComparisonPattern = "GetSetting\\([^)]*\\)\\.Equals\\(\"";
    private const string RetiredPdfTargetDpi = "PdfCompressTargetDpi";
    private const string RetiredPdfJpegQuality = "PdfCompressJpegQuality";
    private const string RetiredPdfDpi = "PdfCompressDpi";
    private const string LanguageZhTw = "zh-TW";
    private const string LanguageEnUs = "en-US";
    private const string LanguageJaJp = "ja-JP";
    private static readonly string[] SupportedLocalizationLanguages =
        { LanguageZhTw, "zh-CN", LanguageEnUs, LanguageJaJp, "ko-KR" };

    public static void RegisterSettingsRegistryTests(TestRunner runner)
    {
        runner.Run("Settings registry: every setting key is declared exactly once", TestSettingKeysDeclaredExactlyOnce);
        runner.Run("Settings registry: GetSetting applies defaults for unset keys", TestSettingDefaults);
        runner.Run("Settings registry: readers must not invent keys or defaults", TestSettingReadersUseRegistry);
        runner.Run("Settings registry: numeric accessors take their fallback from the registry", TestNumericAccessorsUseRegistry);
        runner.Run("Settings registry: retired keys stay outside the active registry", TestRetiredKeysStayDisjoint);
        runner.Run("Settings storage: retired keys are purged and rewritten on load", TestRetiredSettingsPurgedOnLoad);
        runner.Run("Settings registry: CLI localization keys coverage across all 5 languages", TestCliLocalizationKeysCoverage);
        runner.Run("Settings registry: Diagnostics email localization coverage across all 5 languages", TestDiagnosticsEmailLocalizationCoverage);
        runner.Run("Settings registry: Tray and visual splitter localization coverage across all 5 languages", TestTraySplitterLocalizationCoverage);
        runner.Run("Localization guard: No hardcoded Chinese strings in Clickra.Fluent and Dashboard paint files", TestNoHardcodedChineseUiStrings);
        runner.Run("Settings registry: Translation diagnostics lists gaps grouped by language when translations are missing", TestTranslationDiagnosticsGapReport);
        runner.Run("Settings registry: All registered keys must have complete translations across all 5 languages", TestLocalizationDictionaryParity);
    }

    private static void TestSettingKeysDeclaredExactlyOnce()
    {
        var keys = ClickraSettings.All.Select(s => s.Key).ToList();
        Assert.True(keys.Count == keys.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Setting keys must be unique in the registry.");

        string[] expected =
        {
            "Language", "OutputDir", "QuietMode", "Notification",
            "OfficeEngine", "LibreOfficePath", "LibreOfficeRemovalPendingRestart", "LibreOfficeInstalledByClickra",
            "TranslateTargetLang", "PdfCompressImageLevel", "PdfCompressStripFonts", "PdfCompressMinifyContent",
            "ImageCompressLevel", "ImageCompressMaxDimension", "ParkedTaskRetention"
        };
        string[] actual = keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string[] want = expected.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(actual.SequenceEqual(want),
            $"The registry must cover exactly the keys the codebase uses.\nGot:      {string.Join(", ", actual)}\nExpected: {string.Join(", ", want)}\nAdd missing keys to ClickraSettings.All (and remove stale ones).");
        foreach (string key in expected)
        {
            Assert.True(ClickraSettings.IsRegistered(key), $"{key} must be registered.");
        }
    }

    private static void TestSettingDefaults()
    {
        // These keys are never written by the test suite, so the fallback path is what is under test.
        Assert.Equal("", ClickraStorage.GetSetting(ClickraSettings.Language));
        Assert.Equal("source", ClickraStorage.GetSetting(ClickraSettings.OutputDir));
        Assert.Equal("false", ClickraStorage.GetSetting(ClickraSettings.QuietMode));
        Assert.Equal("true", ClickraStorage.GetSetting(ClickraSettings.Notification));
        Assert.Equal("auto", ClickraStorage.GetSetting(ClickraSettings.OfficeEngine));
        Assert.Equal(LanguageZhTw, ClickraStorage.GetSetting(ClickraSettings.TranslateTargetLang));
        Assert.Equal("1", ClickraStorage.GetSetting(ClickraSettings.PdfCompressImageLevel));
        Assert.Equal("0", ClickraStorage.GetSetting(ClickraSettings.ImageCompressMaxDimension));

        Assert.False(ClickraStorage.GetSettingBool(ClickraSettings.QuietMode), "Unset QuietMode must default to off.");
        Assert.True(ClickraStorage.GetSettingBool(ClickraSettings.Notification), "Unset Notification must default to on.");
        Assert.True(ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressMinifyContent), "Unset MinifyContent must default to on.");
        Assert.False(ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressStripFonts), "Unset StripFonts must default to off.");
        Assert.Equal(1, ClickraStorage.GetSettingInt(ClickraSettings.ImageCompressLevel));
        Assert.Equal(1, ConvertCommandRegistry.GetPdfCompressLevel());
        Assert.Equal(1, ConvertCommandRegistry.GetImageCompressLevel());
        Assert.Equal(0, ConvertCommandRegistry.GetImageCompressMaxDimension());

        // Unknown keys stay empty: the registry is the only place defaults exist.
        Assert.Equal("", ClickraStorage.GetSetting("NoSuchSettingKey"));
        Assert.False(ClickraStorage.GetSettingBool("NoSuchSettingKey"), "Unknown keys must default to false.");
        Assert.Equal(0, ClickraStorage.GetSettingInt("NoSuchSettingKey"));
    }

    private static void TestSettingReadersUseRegistry()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(
            "Could not locate the repository root from the test output directory.");
        string[] files = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var registered = new System.Collections.Generic.HashSet<string>(
            ClickraSettings.All.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            AssertSettingReaderUsesRegistry(file, registered);
        }
    }

    private static void AssertSettingReaderUsesRegistry(
        string file,
        System.Collections.Generic.HashSet<string> registered)
    {
        string source = File.ReadAllText(file);
        bool isRegistry = file.EndsWith("ClickraSettings.cs", StringComparison.Ordinal);
        string shortName = Path.GetFileName(file);

        Assert.True(!Regex.IsMatch(source, LiteralSettingKeyPattern, RegexOptions.CultureInvariant, SettingsRegexTimeout),
            $"{shortName}: GetSetting/SaveSetting must take a ClickraSettings constant, not a literal.");

        if (!isRegistry)
        {
            string? redeclaredKey = Regex
                .Matches(source, SettingKeyDeclarationPattern, RegexOptions.CultureInvariant, SettingsRegexTimeout)
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .FirstOrDefault(registered.Contains);
            Assert.True(redeclaredKey is null,
                $"{shortName}: the setting key \"{redeclaredKey}\" is redeclared outside the registry.");
        }

        Assert.True(!Regex.IsMatch(source, NullCoalescedSettingPattern, RegexOptions.CultureInvariant, SettingsRegexTimeout),
            $"{shortName}: GetSetting results must not be followed by a hand-written ?? default.");
        Assert.True(!Regex.IsMatch(source, LiteralSettingComparisonPattern, RegexOptions.CultureInvariant, SettingsRegexTimeout),
            $"{shortName}: use GetSettingBool for boolean settings instead of comparing to a literal.");
    }

    private static void TestNumericAccessorsUseRegistry()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(
            "Could not locate the repository root from the test output directory.");
        string storage = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "ClickraStorage.ActiveRecord.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));

        AssertNumericAccessorUsesRegistry("GetParkedRetentionDays", storage);
        AssertNumericAccessorUsesRegistry("GetPdfCompressLevel", registry);
        AssertNumericAccessorUsesRegistry("GetImageCompressLevel", registry);
        AssertNumericAccessorUsesRegistry("GetImageCompressMaxDimension", registry);
    }

    private static void AssertNumericAccessorUsesRegistry(string name, string source)
    {
        int start = source.IndexOf($"int {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} must exist.");
        int end = source.Length;
        foreach (string marker in new[] { "\n        public", "\n        private", "\n        internal", "\n        static" })
        {
            int i = source.IndexOf(marker, start + 1, StringComparison.Ordinal);
            if (i > 0 && i < end)
            {
                end = i;
            }
        }
        string body = source[start..end];
        Assert.True(body.Contains("ClickraSettings", StringComparison.Ordinal),
            $"{name} must get its fallback default from ClickraSettings, found: {body}");
    }

    private static void TestRetiredKeysStayDisjoint()
    {
        Assert.True(ClickraSettings.RetiredKeys.Count > 0, "Retired keys list must not be empty.");
        Assert.True(ClickraSettings.IsRetired(RetiredPdfTargetDpi), $"{RetiredPdfTargetDpi} must be retired.");
        Assert.True(ClickraSettings.IsRetired(RetiredPdfJpegQuality), $"{RetiredPdfJpegQuality} must be retired.");
        Assert.True(ClickraSettings.IsRetired(RetiredPdfDpi), $"{RetiredPdfDpi} must be retired.");

        var activeKeys = new System.Collections.Generic.HashSet<string>(
            ClickraSettings.All.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);
        foreach (string retired in ClickraSettings.RetiredKeys)
        {
            Assert.False(activeKeys.Contains(retired),
                $"Retired key '{retired}' cannot simultaneously exist in ClickraSettings.All.");
        }
    }

    private static void TestRetiredSettingsPurgedOnLoad()
    {
        string settingsFile = ClickraStorage.GetSettingsFilePath();
        string? backup = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;

        try
        {
            string content = $"Language=zh-CN\n{RetiredPdfTargetDpi}=150\n{RetiredPdfJpegQuality}=75\nOutputDir=desktop\n";
            File.WriteAllText(settingsFile, content, System.Text.Encoding.UTF8);

            ClickraStorage.ReloadSettings();

            Assert.Equal("zh-CN", ClickraStorage.GetSetting(ClickraSettings.Language));
            Assert.Equal(ClickraSettings.OutputDirDesktop, ClickraStorage.GetSetting(ClickraSettings.OutputDir));
            Assert.Equal(ClickraSettings.DefaultEmpty, ClickraStorage.GetSetting(RetiredPdfTargetDpi));
            Assert.Equal(ClickraSettings.DefaultEmpty, ClickraStorage.GetSetting(RetiredPdfJpegQuality));

            string[] persistedLines = File.ReadAllLines(settingsFile);
            Assert.True(persistedLines.Any(line => line.StartsWith("Language=zh-CN", StringComparison.OrdinalIgnoreCase)),
                "Active setting Language must remain in the persisted file.");
            Assert.True(persistedLines.Any(line => line.StartsWith("OutputDir=desktop", StringComparison.OrdinalIgnoreCase)),
                "Active setting OutputDir must remain in the persisted file.");
            Assert.False(persistedLines.Any(line => line.Contains(RetiredPdfTargetDpi, StringComparison.OrdinalIgnoreCase)),
                $"{RetiredPdfTargetDpi} must be pruned from the persisted file.");
            Assert.False(persistedLines.Any(line => line.Contains(RetiredPdfJpegQuality, StringComparison.OrdinalIgnoreCase)),
                $"{RetiredPdfJpegQuality} must be pruned from the persisted file.");
        }
        finally
        {
            if (backup is not null)
            {
                File.WriteAllText(settingsFile, backup, System.Text.Encoding.UTF8);
            }
            else if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }

            ClickraStorage.ReloadSettings();
        }
    }

    private static void TestCliLocalizationKeysCoverage()
    {
        string[] cliKeys =
        {
            "cli_err_prefix",
            "cli_err_unknown_command",
            "cli_err_no_files_found",
            "cli_err_invalid_format",
            "cli_err_invalid_format_title",
            "cli_err_min_files",
            "cli_err_min_files_title",
            "cli_err_option_requires_dir",
            "cli_progress_compressing_pdf",
            "cli_progress_splitting_pdf",
            "cli_progress_converting_image",
            "cli_progress_converting_image_saving",
            "cli_progress_decrypting_pdf",
            "cli_progress_translating_pdf_start",
            "cli_progress_translating_pdf",
            "cli_progress_translating_pdf_done",
            "cli_warn_file_missing_skip",
            "cli_warn_translate_file_vanished",
            "cli_warn_translate_dir_vanished",
            "cli_err_translate_failed"
        };

        AssertLocalizationKeysCoverage(cliKeys);

        string enErr = Localization.T("cli_err_prefix", LanguageEnUs);
        string twErr = Localization.T("cli_err_prefix", LanguageZhTw);
        Assert.Equal("[Error] ", enErr);
        Assert.Equal("[錯誤] ", twErr);

        string enNoFiles = string.Format(Localization.T("cli_err_no_files_found", LanguageEnUs), "compress-pdf");
        Assert.True(enNoFiles.Contains("No convertible files found"),
            $"Expected English translation, got: {enNoFiles}");
    }

    private static void TestDiagnosticsEmailLocalizationCoverage()
    {
        string[] emailKeys =
        {
            "diag_email_subject",
            "diag_email_thanks",
            "diag_email_attachment_hint",
            "diag_email_system_info",
            "diag_email_os",
            "diag_email_version",
            "diag_email_time",
            "diag_email_problem_desc",
            "diag_email_problem_placeholder"
        };

        AssertLocalizationKeysCoverage(emailKeys);

        var (enSubject, enBody) = Localization.BuildDiagnosticsEmail("1.2.0", "2026-09-15 12:00:00", LanguageEnUs);
        Assert.Equal("Clickra Diagnostics Report", enSubject);
        Assert.True(enBody.Contains("Thank you for submitting a Clickra diagnostics report!"),
            "Expected English thanks text.");
        Assert.True(enBody.Contains("Clickra Version: 1.2.0"), "Expected formatted English version.");
        Assert.True(enBody.Contains("[System Information]"), "Expected English section header.");

        var (twSubject, twBody) = Localization.BuildDiagnosticsEmail("1.2.0", "2026-09-15 12:00:00", LanguageZhTw);
        Assert.Equal("Clickra 診斷回報", twSubject);
        Assert.True(twBody.Contains("感謝您提交 Clickra 診斷回報！"),
            "Expected Traditional Chinese thanks text.");
        Assert.True(twBody.Contains("Clickra 版本: 1.2.0"), "Expected formatted Traditional Chinese version.");
        Assert.True(twBody.Contains("[系統資訊]"), "Expected Traditional Chinese section header.");

        var (jaSubject, jaBody) = Localization.BuildDiagnosticsEmail("1.2.0", "2026-09-15 12:00:00", LanguageJaJp);
        Assert.Equal("Clickra 診断レポート", jaSubject);
        Assert.True(jaBody.Contains("Clickra バージョン: 1.2.0"), "Expected Japanese version label.");
    }

    private static void TestTraySplitterLocalizationCoverage()
    {
        string[] keys =
        {
            "tray_background_running",
            "tray_restore_all",
            "pdf_split_btn_add",
            "pdf_split_btn_delete",
            "pdf_split_btn_clear",
            "pdf_split_btn_split_at",
            "pdf_split_mode_fixed_n",
            "pdf_split_segment_header",
            "pdf_split_segment_item",
            "pdf_split_page_preview_format",
            "pdf_split_badge_format",
            "pdf_split_pages_n"
        };

        AssertLocalizationKeysCoverage(keys);
    }

    private static void TestTranslationDiagnosticsGapReport()
    {
        string[] testKeys = { "sample_key_tw_only", "sample_key_non_existent" };
        var missing = Localization.FindMissingTranslations(testKeys);

        Assert.True(missing.Count > 0, "Missing translations must be detected for unregistered test keys.");
        Assert.True(missing.ContainsKey("en-US"), "en-US must be reported as missing test keys.");
        Assert.True(missing.ContainsKey(LanguageJaJp), "ja-JP must be reported as missing test keys.");
        Assert.True(missing.ContainsKey("ko-KR"), "ko-KR must be reported as missing test keys.");

        string report = Localization.FormatMissingReport(missing);
        Assert.True(report.Contains("[en-US]"), "Report must include language section for en-US.");
        Assert.True(report.Contains("- sample_key_non_existent"), "Report must list the specific missing key.");
    }

    private static void TestLocalizationDictionaryParity()
    {
        var missing = Localization.FindMissingTranslations();
        if (missing.Count > 0)
        {
            Assert.True(false, Localization.FormatMissingReport(missing));
        }
    }

    private static void TestNoHardcodedChineseUiStrings()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(
            "Could not locate the repository root from the test output directory.");
        var filesToScan = new List<string>();

        string dashboardDir = Path.Combine(root, "src", "Clickra.CLI", "Dashboard");
        if (Directory.Exists(dashboardDir))
        {
            filesToScan.AddRange(Directory.GetFiles(dashboardDir, "DashboardWindow.Paint*.cs"));
        }

        string fluentDir = Path.Combine(root, "src", "Clickra.Fluent");
        if (Directory.Exists(fluentDir))
        {
            filesToScan.AddRange(Directory.EnumerateFiles(fluentDir, "*.*", SearchOption.AllDirectories)
                .Where(IsLocalizationGuardTarget));
        }

        Assert.True(filesToScan.Count > 0, "Expected to find target UI files for localization guard scanning.");

        List<string> violations = filesToScan
            .SelectMany(file => FindHardcodedChineseViolations(root, file))
            .ToList();
        Assert.True(violations.Count == 0,
            $"Found {violations.Count} hardcoded Chinese string(s) in UI/Paint files:\n" + string.Join("\n", violations));
    }

    private static bool IsLocalizationGuardTarget(string path)
    {
        bool supportedExtension = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                  path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        bool generatedPath = path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                             path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        return supportedExtension && !generatedPath;
    }

    private static IEnumerable<string> FindHardcodedChineseViolations(string root, string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        string raw = File.ReadAllText(file);
        raw = ext == ".cs"
            ? Regex.Replace(raw, @"/\*.*?\*/", match => PreserveLineBreaks(match.Value), RegexOptions.Singleline, SettingsRegexTimeout)
            : Regex.Replace(raw, @"<!--.*?-->", match => PreserveLineBreaks(match.Value), RegexOptions.Singleline, SettingsRegexTimeout);

        string[] lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        for (int index = 0; index < lines.Length; index++)
        {
            string line = ext == ".cs" ? StripSingleLineComment(lines[index]) : lines[index];
            if (!Regex.IsMatch(line, @"[\u4e00-\u9fff]", RegexOptions.CultureInvariant, SettingsRegexTimeout) ||
                IsAllowedLanguageAutonym(line))
            {
                continue;
            }

            yield return $"{Path.GetRelativePath(root, file)}:{index + 1}: {line.Trim()}";
        }
    }

    private static string PreserveLineBreaks(string value) =>
        new('\n', value.Count(character => character == '\n'));

    private static string StripSingleLineComment(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    private static bool IsAllowedLanguageAutonym(string line) =>
        line.Contains("zh-TW", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("zh-CN", StringComparison.OrdinalIgnoreCase) ||
        line.Contains(LanguageJaJp, StringComparison.OrdinalIgnoreCase) ||
        line.Contains("ja)", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("ko-KR", StringComparison.OrdinalIgnoreCase);

    private static void AssertLocalizationKeysCoverage(string[] keys)
    {
        foreach (string lang in SupportedLocalizationLanguages)
        {
            foreach (string key in keys)
            {
                string translated = Localization.T(key, lang);
                Assert.True(!string.IsNullOrWhiteSpace(translated),
                    $"Key '{key}' must have non-empty translation for language '{lang}'.");
                Assert.False(translated.Equals(key, StringComparison.Ordinal),
                    $"Key '{key}' was not found in dictionary for language '{lang}' (returned raw key).");
            }
        }
    }
}
