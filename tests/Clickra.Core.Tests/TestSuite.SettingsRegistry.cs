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
    public static void RegisterSettingsRegistryTests(TestRunner runner)
    {
        runner.Run("Settings registry: every setting key is declared exactly once", () =>
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
                Assert.True(ClickraSettings.IsRegistered(key), $"{key} must be registered.");
        });

        runner.Run("Settings registry: GetSetting applies defaults for unset keys", () =>
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
        });

        runner.Run("Settings registry: readers must not invent keys or defaults", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string[] files = Directory
                .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                            !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToArray();

            var registered = new System.Collections.Generic.HashSet<string>(
                ClickraSettings.All.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                bool isRegistry = file.EndsWith("ClickraSettings.cs", StringComparison.Ordinal);
                string shortName = Path.GetFileName(file);

                // (a) Keys must be passed as constants — a literal key at a call site is unregistrable drift.
                Assert.True(!Regex.IsMatch(source, @"(GetSetting|SaveSetting)\(\s*"""),
                    $"{shortName}: GetSetting/SaveSetting must take a ClickraSettings constant, not a literal.");

                // (b) No file may redeclare a key constant (e.g. private const ... = \"Language\").
                if (!isRegistry)
                {
                    foreach (Match m in Regex.Matches(source, @"const\s+string\s+\w+\s*=\s*""([^""]+)"""))
                    {
                        Assert.False(registered.Contains(m.Groups[1].Value),
                            $"{shortName}: the setting key \"{m.Groups[1].Value}\" is redeclared outside the registry.");
                    }
                }

                // (c) No call site may null-coalesce a GetSetting result into a hand-written default.
                Assert.True(!Regex.IsMatch(source, @"GetSetting\([^)]*\)\s*\?\?"),
                    $"{shortName}: GetSetting results must not be followed by a hand-written ?? default.");

                // (d) No call site may compare a GetSetting result against a string literal to recover a
                // default. Comparing to the registry's ValueTrue/ValueFalse constants is the sanctioned form.
                Assert.True(!Regex.IsMatch(source, "GetSetting\\([^)]*\\)\\.Equals\\(\""),
                    $"{shortName}: use GetSettingBool for boolean settings instead of comparing to a literal.");
            }
        });

        runner.Run("Settings registry: numeric accessors take their fallback from the registry", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string storage = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "ClickraStorage.ActiveRecord.cs"));
            string registry2 = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));

            foreach ((string name, string source) in new[]
            {
                ("GetParkedRetentionDays", storage),
                ("GetPdfCompressLevel", registry2),
                ("GetImageCompressLevel", registry2),
                ("GetImageCompressMaxDimension", registry2),
            })
            {
                int start = source.IndexOf($"int {name}(", StringComparison.Ordinal);
                Assert.True(start >= 0, $"{name} must exist.");
                int end = source.Length;
                foreach (string marker in new[] { "\n        public", "\n        private", "\n        internal", "\n        static" })
                {
                    int i = source.IndexOf(marker, start + 1, StringComparison.Ordinal);
                    if (i > 0 && i < end) end = i;
                }
                string body = source[start..end];
                Assert.True(body.Contains("ClickraSettings", StringComparison.Ordinal),
                    $"{name} must get its fallback default from ClickraSettings, found: {body}");
            }
        });
    }
}