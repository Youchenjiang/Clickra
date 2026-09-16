using System;
using System.Collections.Generic;
using System.Globalization;
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
    private static readonly Regex HardcodedCjkTextPattern = new(
        @"[\u3000-\u303F\u3040-\u30FF\u3400-\u4DBF\u4E00-\u9FFF\uAC00-\uD7AF\uF900-\uFAFF\uFF00-\uFFEF]",
        RegexOptions.Compiled,
        SettingsRegexTimeout);
    private static readonly Regex DrawnGlyphLiteralPattern = new(
        "\"[\uFF01-\uFF5E]\"",
        RegexOptions.Compiled,
        SettingsRegexTimeout);
    private const string LiteralSettingKeyPattern = @"(GetSetting|SaveSetting)\(\s*""";
    private const string SettingKeyDeclarationPattern = @"const\s+string\s+\w+\s*=\s*""([^""]+)""";
    private const string NullCoalescedSettingPattern = @"GetSetting\([^)]*\)\s*\?\?";
    private const string LiteralSettingComparisonPattern = "GetSetting\\([^)]*\\)\\.Equals\\(\"";
    private const string RetiredPdfTargetDpi = "PdfCompressTargetDpi";
    private const string RetiredPdfJpegQuality = "PdfCompressJpegQuality";
    private const string RetiredPdfDpi = "PdfCompressDpi";
    private const string LanguageZhTw = "zh-TW";
    private const string LanguageZhCn = "zh-CN";
    private const string LanguageEnUs = "en-US";
    private const string LanguageJaJp = "ja-JP";
    private const string LanguageKoKr = "ko-KR";
    private const string ProgressSubCompleted = "progress_sub_completed";
    private const string SamplePdfFileName = "doc.pdf";
    private const string SamplePdfStage = "page 2/10";
    private static readonly string[] SupportedLocalizationLanguages =
        { LanguageZhTw, LanguageZhCn, LanguageEnUs, LanguageJaJp, LanguageKoKr };

    public static void RegisterSettingsRegistryTests(TestRunner runner)
    {
        runner.Run("Settings registry: every setting key is declared exactly once", TestSettingKeysDeclaredExactlyOnce);
        runner.Run("Settings registry: GetSetting applies defaults for unset keys", TestSettingDefaults);
        runner.Run("Settings registry: readers must not invent keys or defaults", TestSettingReadersUseRegistry);
        runner.Run("Settings registry: numeric accessors take their fallback from the registry", TestNumericAccessorsUseRegistry);
        runner.Run("Settings registry: numeric ranges are declared, valid, and clamp correctly", TestNumericRangesAreDeclaredAndClampCorrectly);
        runner.Run("Settings registry: UI controls derive bounds and guards from centralized ranges", TestNumericUiControlsDeriveBounds);
        runner.Run("Settings registry: retired keys stay outside the active registry", TestRetiredKeysStayDisjoint);
        runner.Run("Settings storage: retired keys are purged and rewritten on load", TestRetiredSettingsPurgedOnLoad);
        runner.Run("Settings storage: real-time file watcher and cache synchronization", TestSettingsFileWatcherAndCacheSynchronization);
        runner.Run("Settings storage: UI components hook SettingsReloaded for real-time sync", TestSettingsReloadedUiHooks);
        runner.Run("Settings registry: CLI localization keys coverage across all 5 languages", TestCliLocalizationKeysCoverage);
        runner.Run("Settings registry: Diagnostics email localization coverage across all 5 languages", TestDiagnosticsEmailLocalizationCoverage);
        runner.Run("Settings registry: Tray, visual splitter, and progress window localization coverage across all 5 languages", TestTraySplitterLocalizationCoverage);
        runner.Run("Settings registry: Fluent add-on settings localization coverage across all 5 languages", TestFluentSettingsLocalizationCoverage);
        runner.Run("Settings registry: Localization.T default language and formatting overloads", TestLocalizationDefaultLanguageAndFormatting);
        runner.Run("Settings registry: Fluent-specific copy stays distinct from shared keys", TestFluentSpecificCopyPreserved);
        runner.Run("Localization guard: no hardcoded CJK text in Clickra.Fluent, Dashboard paint files, and the Win32 progress window", TestNoHardcodedChineseUiStrings);
        runner.Run("Settings registry: Translation diagnostics lists gaps grouped by language when translations are missing", TestTranslationDiagnosticsGapReport);
        runner.Run("Settings registry: All registered keys must have complete translations across all 5 languages", TestLocalizationDictionaryParity);
        runner.Run("Test runner: every compiled test suite is invoked by Program.cs", TestEveryCompiledSuiteIsInvoked);
        runner.Run("Test runner: CleanStaleArtifacts cleans isolated temp directories and test artifacts", TestCleanStaleArtifacts);
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
            "LibreOfficeManagedProductCode", "LibreOfficeManagedSofficePath",
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
            RepoRootNotFoundMessage);
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
            RepoRootNotFoundMessage);
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

    private static void TestNumericRangesAreDeclaredAndClampCorrectly()
    {
        var numericSettings = ClickraSettings.All
            .Where(setting => int.TryParse(setting.Default, out _))
            .ToList();

        Assert.True(numericSettings.Count > 0, "There must be at least one numeric setting.");

        foreach (var setting in numericSettings)
        {
            Assert.True(ClickraSettings.TryGetNumericRange(setting.Key, out var range),
                $"Setting '{setting.Key}' has an integer default ({setting.Default}) but is missing from ClickraSettings.NumericRanges.");
            Assert.True(range.Min <= range.Max,
                $"Setting '{setting.Key}' has Min ({range.Min}) greater than Max ({range.Max}).");
            Assert.True(range.Min <= range.Default && range.Default <= range.Max,
                $"Setting '{setting.Key}' default ({range.Default}) is outside range [{range.Min}, {range.Max}].");
            Assert.Equal(setting.Default, range.Default.ToString());

            Assert.Equal(range.Min, ClickraSettings.ClampNumericSetting(setting.Key, range.Min - 100));
            if (range.Max <= int.MaxValue - 100)
            {
                Assert.Equal(range.Max, ClickraSettings.ClampNumericSetting(setting.Key, range.Max + 100));
            }
            Assert.Equal(range.Default, ClickraSettings.ClampNumericSetting(setting.Key, range.Default));

            Assert.True(ClickraSettings.IsNumericSettingInRange(setting.Key, range.Min), $"{setting.Key} Min must be in range.");
            Assert.True(ClickraSettings.IsNumericSettingInRange(setting.Key, range.Max), $"{setting.Key} Max must be in range.");
            Assert.True(ClickraSettings.IsNumericSettingInRange(setting.Key, range.Default), $"{setting.Key} Default must be in range.");
            Assert.False(ClickraSettings.IsNumericSettingInRange(setting.Key, range.Min - 1), $"{setting.Key} below Min must not be in range.");
            if (range.Max < int.MaxValue)
            {
                Assert.False(ClickraSettings.IsNumericSettingInRange(setting.Key, range.Max + 1), $"{setting.Key} above Max must not be in range.");
            }
        }

        int existingLargeDimension = 20000;
        Assert.Equal(existingLargeDimension,
            ClickraSettings.ClampNumericSetting(ClickraSettings.ImageCompressMaxDimension, existingLargeDimension));
    }

    private static void TestNumericUiControlsDeriveBounds()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepoRootNotFoundMessage);
        string fluentCode = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentXaml = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml"));
        string cliPaint = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, "DashboardWindow.Paint.Settings.cs"));
        string cliEvents = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, "DashboardWindow.Events.cs"));
        string cliClick = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, "DashboardWindow.Events.Click.cs"));

        Assert.True(fluentCode.Contains("CompressionSlider.Minimum = ClickraSettings.MinPdfCompressLevel", StringComparison.Ordinal),
            "Fluent must derive CompressionSlider.Minimum from ClickraSettings.MinPdfCompressLevel.");
        Assert.True(fluentCode.Contains("CompressionSlider.Maximum = ClickraSettings.MaxPdfCompressLevel", StringComparison.Ordinal),
            "Fluent must derive CompressionSlider.Maximum from ClickraSettings.MaxPdfCompressLevel.");
        Assert.True(fluentCode.Contains("ParkedRetentionBox.Minimum = MinParkedRetentionDays", StringComparison.Ordinal),
            "Fluent must derive ParkedRetentionBox.Minimum from ClickraSettings.");
        Assert.True(fluentCode.Contains("ParkedRetentionBox.Maximum = MaxParkedRetentionDays", StringComparison.Ordinal),
            "Fluent must derive ParkedRetentionBox.Maximum from ClickraSettings.");

        Assert.True(fluentXaml.Contains("x:Name=\"CompressionSlider\"", StringComparison.Ordinal) &&
                    !fluentXaml.Contains("x:Name=\"CompressionSlider\" Minimum=", StringComparison.Ordinal) &&
                    !fluentXaml.Contains("x:Name=\"CompressionSlider\" Maximum=", StringComparison.Ordinal),
            "Fluent XAML must not duplicate CompressionSlider bounds; LoadSettings owns the centralized range.");
        Assert.True(fluentXaml.Contains("x:Name=\"ParkedRetentionBox\"", StringComparison.Ordinal) &&
                    !fluentXaml.Contains("x:Name=\"ParkedRetentionBox\" Grid.Column=\"1\" MinWidth=\"160\" Minimum=", StringComparison.Ordinal) &&
                    !fluentXaml.Contains("x:Name=\"ParkedRetentionBox\" Grid.Column=\"1\" MinWidth=\"160\" Maximum=", StringComparison.Ordinal),
            "Fluent XAML must not duplicate ParkedRetentionBox bounds; LoadSettings owns the centralized range.");

        Assert.True(cliPaint.Contains("ClickraSettings.MaxPdfCompressLevel - ClickraSettings.MinPdfCompressLevel + 1", StringComparison.Ordinal),
            "CLI slider stop calculation must derive from ClickraSettings bounds.");
        Assert.True(cliEvents.Contains("ClickraSettings.MaxPdfCompressLevel - ClickraSettings.MinPdfCompressLevel", StringComparison.Ordinal) &&
                    cliEvents.Contains("ClickraSettings.ClampNumericSetting", StringComparison.Ordinal),
            "CLI slider drag event must derive its span and clamping from ClickraSettings.");
        Assert.True(cliClick.Contains("ClickraSettings.MaxPdfCompressLevel - ClickraSettings.MinPdfCompressLevel", StringComparison.Ordinal) &&
                    cliClick.Contains("ClickraSettings.ClampNumericSetting", StringComparison.Ordinal),
            "CLI slider click handler must derive its span and clamping from ClickraSettings.");
        Assert.True(cliClick.Contains("ClickraSettings.ClampNumericSetting(ClickraSettings.PdfCompressImageLevel", StringComparison.Ordinal),
            "CLI ApplyPdfCompressLevel must clamp using ClickraSettings.");
        Assert.True(cliClick.Contains("ClickraSettings.MinParkedRetentionDays", StringComparison.Ordinal) &&
                    cliClick.Contains("ClickraSettings.MaxParkedRetentionDays", StringComparison.Ordinal),
            "CLI SetParkedRetention must clamp against centralized retention bounds.");
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
            string content = $"Language={LanguageZhCn}\n{RetiredPdfTargetDpi}=150\n{RetiredPdfJpegQuality}=75\nOutputDir=desktop\n";
            File.WriteAllText(settingsFile, content, System.Text.Encoding.UTF8);

            ClickraStorage.ReloadSettings();

            Assert.Equal(LanguageZhCn, ClickraStorage.GetSetting(ClickraSettings.Language));
            Assert.Equal(ClickraSettings.OutputDirDesktop, ClickraStorage.GetSetting(ClickraSettings.OutputDir));
            Assert.Equal(ClickraSettings.DefaultEmpty, ClickraStorage.GetSetting(RetiredPdfTargetDpi));
            Assert.Equal(ClickraSettings.DefaultEmpty, ClickraStorage.GetSetting(RetiredPdfJpegQuality));

            string[] persistedLines = File.ReadAllLines(settingsFile);
            Assert.True(persistedLines.Any(line => line.StartsWith($"Language={LanguageZhCn}", StringComparison.OrdinalIgnoreCase)),
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

    private static void TestSettingsFileWatcherAndCacheSynchronization()
    {
        string settingsFile = ClickraStorage.GetSettingsFilePath();
        string? backup = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;

        try
        {
            string? notifiedKey = null;
            string? notifiedValue = null;
            using var reloadedEvent = new System.Threading.ManualResetEventSlim(false);

            Action<string, string> onKeyChanged = (key, value) =>
            {
                if (key == ClickraSettings.QuietMode)
                {
                    notifiedKey = key;
                    notifiedValue = value;
                }
            };
            Action onReloaded = () => reloadedEvent.Set();

            ClickraStorage.SettingChanged += onKeyChanged;
            ClickraStorage.SettingsReloaded += onReloaded;
            try
            {
                ClickraStorage.SaveSetting(ClickraSettings.QuietMode, ClickraSettings.ValueTrue);
                Assert.True(reloadedEvent.Wait(2000), "SettingsReloaded event must be raised on SaveSetting.");
                Assert.Equal(ClickraSettings.QuietMode, notifiedKey ?? string.Empty);
                Assert.Equal(ClickraSettings.ValueTrue, notifiedValue ?? string.Empty);
                Assert.True(ClickraStorage.GetSettingBool(ClickraSettings.QuietMode),
                    "QuietMode must be true after SaveSetting.");
            }
            finally
            {
                ClickraStorage.SettingChanged -= onKeyChanged;
                ClickraStorage.SettingsReloaded -= onReloaded;
            }

            string newConfig = "QuietMode=false\nParkedTaskRetention=42\n";
            File.WriteAllText(settingsFile, newConfig, System.Text.Encoding.UTF8);
            File.SetLastWriteTimeUtc(settingsFile, DateTime.UtcNow.AddSeconds(1));

            Assert.False(ClickraStorage.GetSettingBool(ClickraSettings.QuietMode),
                "GetSetting must immediately bust stale cache when external process changes settings file.");
            Assert.Equal(42, ClickraStorage.GetParkedRetentionDays());
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

    private static void TestSettingsReloadedUiHooks()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepoRootNotFoundMessage);
        string fluentCode = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string cliLifecycle = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, "DashboardWindow.Lifecycle.cs"));
        string cliEvents = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, "DashboardWindow.Events.cs"));

        Assert.True(fluentCode.Contains("ClickraStorage.SettingsReloaded +=", StringComparison.Ordinal),
            "Fluent UI must subscribe to ClickraStorage.SettingsReloaded.");
        Assert.True(fluentCode.Contains("SyncSettingsToUi()", StringComparison.Ordinal),
            "Fluent UI must invoke SyncSettingsToUi() to synchronize controls without saving back.");
        Assert.True(cliLifecycle.Contains("ClickraStorage.SettingsReloaded +=", StringComparison.Ordinal),
            "CLI Dashboard must subscribe to ClickraStorage.SettingsReloaded.");
        Assert.True(cliLifecycle.Contains("ClickraStorage.SettingsReloaded -=", StringComparison.Ordinal),
            "CLI Dashboard must unsubscribe from ClickraStorage.SettingsReloaded on window close.");
        Assert.True(cliEvents.Contains("ClickraStorage.EnsureFreshSettings()", StringComparison.Ordinal),
            "CLI Dashboard timer tick must ensure fresh settings on each refresh.");
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
            "pdf_split_pages_n",
            "progress_sub_failed",
            ProgressSubCompleted,
            "progress_sub_visual_splitter",
            "progress_sub_running",
            "progress_header_failed",
            "progress_header_success",
            "progress_auto_close_hint",
            "progress_tip_processing",
            "cli_progress_preparing",
            "cli_progress_no_files",
            "cli_progress_all_done",
            "cli_progress_pages_input_canceled",
            "cli_progress_compressing_pdf",
            "cli_progress_compressing_pdf_stage",
            "cli_progress_compressing_pdf_done",
            "cli_progress_converting_image",
            "cli_progress_converting_image_saving",
            "cli_progress_translating_pdf",
            "cli_progress_translating_pdf_stage",
            "cli_progress_translating_pdf_saving",
            "cli_progress_splitting_pdf",
            "cli_progress_splitting_pdf_stage",
            "cli_progress_splitting_pdf_done",
            "cli_progress_decrypting_pdf",
            "cli_progress_decrypting_pdf_stage",
            "cli_progress_decrypting_pdf_saving",
            "cli_progress_toast_title",
            "cli_progress_toast_body",
            "cli_tray_converting",
            "cli_tray_restore",
            "cli_tray_cancel",
            "cli_err_no_input",
            "cli_err_no_input_title"
        };

        AssertLocalizationKeysCoverage(keys);

        var placeholderKeys = new (string Key, object[] Args)[]
        {
            ("cli_progress_compressing_pdf", new object[] { SamplePdfFileName, 1, 3 }),
            ("cli_progress_compressing_pdf_stage", new object[] { SamplePdfStage, 1, 3 }),
            ("cli_progress_converting_image", new object[] { "photo.png", 1, 3 }),
            ("cli_progress_translating_pdf", new object[] { SamplePdfFileName, 1, 3 }),
            ("cli_progress_translating_pdf_stage", new object[] { SamplePdfStage, 1, 3 }),
            ("cli_progress_splitting_pdf", new object[] { SamplePdfFileName, 1, 3 }),
            ("cli_progress_splitting_pdf_stage", new object[] { SamplePdfStage, 1, 3 }),
            ("cli_progress_decrypting_pdf", new object[] { SamplePdfFileName, 1, 3 }),
            ("cli_progress_decrypting_pdf_stage", new object[] { SamplePdfStage, 1, 3 }),
            ("cli_progress_toast_body", new object[] { "Compress PDF", 3 }),
            ("cli_tray_converting", new object[] { 42 })
        };

        foreach (string lang in SupportedLocalizationLanguages)
        {
            foreach (var (key, args) in placeholderKeys)
            {
                string rendered = string.Format(CultureInfo.InvariantCulture, Localization.T(key, lang), args);
                Assert.False(Regex.IsMatch(rendered, @"\{\d+\}", RegexOptions.None, SettingsRegexTimeout),
                    $"Key '{key}' for '{lang}' left an unformatted placeholder: {rendered}");
                foreach (object arg in args)
                {
                    string text = Convert.ToString(arg, CultureInfo.InvariantCulture) ?? "";
                    Assert.True(rendered.Contains(text, StringComparison.Ordinal),
                        $"Key '{key}' for '{lang}' dropped the argument '{text}': {rendered}");
                }
            }
        }
    }

    private static void TestFluentSettingsLocalizationCoverage()
    {
        string[] keys =
        {
            "setting_fluent_title",
            "setting_fluent_desc",
            "setting_fluent_ready",
            "setting_fluent_not_installed",
            "setting_fluent_install"
        };

        AssertLocalizationKeysCoverage(keys);
    }

    private static void TestLocalizationDefaultLanguageAndFormatting()
    {
        string origLang = ClickraStorage.GetSetting(ClickraSettings.Language);
        try
        {
            ClickraStorage.SaveSetting(ClickraSettings.Language, LanguageEnUs);
            Assert.Equal("Overview", Localization.T("fluent_nav_overview"));
            Assert.Equal("Operation completed", Localization.T(ProgressSubCompleted));

            ClickraStorage.SaveSetting(ClickraSettings.Language, LanguageJaJp);
            Assert.Equal("概要", Localization.T("fluent_nav_overview"));
            Assert.Equal("処理完了", Localization.T(ProgressSubCompleted));

            ClickraStorage.SaveSetting(ClickraSettings.Language, LanguageZhTw);
            Assert.Equal("總覽", Localization.T("fluent_nav_overview"));
            Assert.Equal("作業完成", Localization.T(ProgressSubCompleted));

            ClickraStorage.SaveSetting(ClickraSettings.Language, LanguageEnUs);
            Assert.Equal("[PDF] doc.pdf (5 pages)", Localization.T("pdf_split_badge_format", SamplePdfFileName, 5));

            ClickraStorage.SaveSetting(ClickraSettings.Language, LanguageZhTw);
            Assert.Equal("[PDF] doc.pdf (5 頁)", Localization.T("pdf_split_badge_format", SamplePdfFileName, 5));
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.Language, origLang);
        }
    }

    private static void TestFluentSpecificCopyPreserved()
    {
        Assert.Equal("OK", Localization.T("fluent_ok", LanguageJaJp));
        Assert.Equal("元と同じ", Localization.T("fluent_output_source", LanguageJaJp));
        Assert.Equal("원본과 같음", Localization.T("fluent_output_source", LanguageKoKr));
        Assert.Equal("사용자 지정...", Localization.T("fluent_custom", LanguageKoKr));
        Assert.Equal("Input paths", Localization.T("fluent_input_paths", LanguageEnUs));
        Assert.Equal("Output paths", Localization.T("fluent_output_paths", LanguageEnUs));
        Assert.Equal("错误消息", Localization.T("fluent_error_message", LanguageZhCn));
        Assert.Equal("Error message", Localization.T("fluent_error_message", LanguageEnUs));

        Assert.False(Localization.T("fluent_ok", LanguageJaJp) == Localization.T("dialog_ok", LanguageJaJp),
            "Fluent OK copy intentionally differs from the shared dialog label in Japanese.");
        Assert.False(Localization.T("fluent_output_paths", LanguageEnUs) == Localization.T("history_detail_outputs", LanguageEnUs),
            "Fluent history output copy intentionally preserves its plural English label.");
    }

    private static void TestTranslationDiagnosticsGapReport()
    {
        string[] testKeys = { "sample_key_tw_only", "sample_key_non_existent" };
        var missing = Localization.FindMissingTranslations(testKeys);

        Assert.True(missing.Count > 0, "Missing translations must be detected for unregistered test keys.");
        Assert.True(missing.ContainsKey("en-US"), "en-US must be reported as missing test keys.");
        Assert.True(missing.ContainsKey(LanguageJaJp), "ja-JP must be reported as missing test keys.");
        Assert.True(missing.ContainsKey(LanguageKoKr), $"{LanguageKoKr} must be reported as missing test keys.");

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

    private static void TestEveryCompiledSuiteIsInvoked()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        var defined = typeof(TestSuite)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Register", StringComparison.Ordinal) &&
                        m.ReturnType == typeof(void) &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(TestRunner))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(defined.Count > 0, "Expected at least one compiled test suite registration method.");
        string program = File.ReadAllText(Path.Combine(root, "tests", "Clickra.Core.Tests", "Program.cs"));
        var missing = defined.Where(n => !program.Contains($"TestSuite.{n}(", StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            "These test suites are compiled but never invoked: " + string.Join(", ", missing));
    }

    private static void TestCleanStaleArtifacts()
    {
        string staleDir = Path.Combine(GetTestDataRoot(), $"clickra-test-data-dummy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staleDir);
        Assert.True(Directory.Exists(staleDir), "Dummy stale directory must exist before cleanup.");

        string? activeDataDir = Environment.GetEnvironmentVariable("CLICKRA_DATA_DIR");
        var (dirs, _) = CleanStaleArtifacts(activeDataDir);

        Assert.True(dirs >= 1, "Must have cleaned at least the dummy stale directory.");
        Assert.False(Directory.Exists(staleDir), "Dummy stale directory must be removed.");
        if (activeDataDir != null && Directory.Exists(activeDataDir))
        {
            Assert.True(Directory.Exists(activeDataDir), "Active test data directory must not be removed.");
        }
    }

    private static void TestNoHardcodedChineseUiStrings()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(
            RepoRootNotFoundMessage);
        var filesToScan = new List<string>();

        string dashboardDir = Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory);
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

        string progressDir = Path.Combine(root, "src", CliProjectDirectory, "Progress");
        if (Directory.Exists(progressDir))
        {
            filesToScan.AddRange(Directory.GetFiles(progressDir, "*.cs"));
        }

        string[] expectedProgressFiles =
        {
            "ProgressWindow.cs",
            "ProgressWindow.Controls.cs",
            "ProgressWindow.Paint.cs",
            "ProgressWindow.PasswordInput.cs",
            "ProgressWindow.Process.cs",
            "ProgressWindow.Tray.cs",
            "ProgressWindow.VisualSplitter.cs"
        };
        foreach (string name in expectedProgressFiles)
        {
            string expected = Path.Combine(progressDir, name);
            Assert.True(filesToScan.Contains(expected),
                $"Progress window file '{name}' must be covered by the localization guard.");
        }

        Assert.True(filesToScan.Count > 0, "Expected to find target UI files for localization guard scanning.");

        List<string> violations = filesToScan
            .SelectMany(file => FindHardcodedChineseViolations(root, file))
            .ToList();
        Assert.True(violations.Count == 0,
            $"Found {violations.Count} hardcoded CJK text(s) in UI/Paint files:\n" + string.Join("\n", violations));
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
            string candidate = DrawnGlyphLiteralPattern.Replace(line, "\"\"");
            candidate = XamlEndonymPattern.Replace(candidate, string.Empty);
            if (!HardcodedCjkTextPattern.IsMatch(candidate))
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
        line.Contains(LanguageZhCn, StringComparison.OrdinalIgnoreCase) ||
        line.Contains(LanguageJaJp, StringComparison.OrdinalIgnoreCase) ||
        line.Contains("ja)", StringComparison.OrdinalIgnoreCase) ||
        line.Contains(LanguageKoKr, StringComparison.OrdinalIgnoreCase);

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
