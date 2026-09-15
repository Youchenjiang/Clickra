using System;
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

    public static void RegisterSettingsRegistryTests(TestRunner runner)
    {
        runner.Run("Settings registry: every setting key is declared exactly once", TestSettingKeysDeclaredExactlyOnce);
        runner.Run("Settings registry: GetSetting applies defaults for unset keys", TestSettingDefaults);
        runner.Run("Settings registry: readers must not invent keys or defaults", TestSettingReadersUseRegistry);
        runner.Run("Settings registry: numeric accessors take their fallback from the registry", TestNumericAccessorsUseRegistry);
        runner.Run("Settings registry: retired keys stay outside the active registry", TestRetiredKeysStayDisjoint);
        runner.Run("Settings storage: retired keys are purged and rewritten on load", TestRetiredSettingsPurgedOnLoad);
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
        Assert.Equal("zh-TW", ClickraStorage.GetSetting(ClickraSettings.TranslateTargetLang));
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
}
