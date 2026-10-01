using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

/// <summary>
/// 兩條「畫面上會出現的字都必須有一份宣告過的翻譯」的來源端守門。
///
/// 1. Win32 Shell 的右鍵選單直接讀 packaging/msix/Strings/&lt;culture&gt;/Resources.resw，
///    而 ShellUtils.GetString 找不到鍵時把**鍵名本身**印給使用者。因此 resw 的鍵集合與
///    Shell 實際查詢的鍵集合必須雙向相等：少一條就是選單上出現 Menu_Xxx，多一條就是
///    五種語言都翻好了卻沒有任何人看得到的殘餘鍵。
///
/// 2. ConvertCommandRegistry 是命令中繼資料的單一來源，它的 LabelKey 由 Fluent 與 CLI
///    Dashboard 動態查表（GetLabelKey）之後才送進 Localization。這種間接查詢不會被任何
///    靜態掃描抓到 —— 先前就有 6 個圖片命令的標籤鍵從未被宣告，畫面會直接顯示
///    cmd_img_to_png 這種原始鍵名 —— 所以在這裡用執行期的登錄表逐一驗證。
/// </summary>
static partial class TestSuite
{
    private static readonly TimeSpan UiResourceRegexTimeout = TimeSpan.FromSeconds(1);
    /// <summary>UI 語言的程式碼（zh-TW）與 resw 資料夾名稱（zh-tw）互轉。</summary>
    private static string CultureFolder(string languageCode) => languageCode.ToLowerInvariant();

    /// <summary>Shell 選單會查詢的 resw 鍵：MenuKeys 陣列，加上根項目標題的鍵。</summary>
    private static string[] GetShellConsumedResourceKeys(string repoRoot)
    {
        string comMethods = File.ReadAllText(Path.Combine(repoRoot, "src", "ClickraShell", "ComMethods.cs"));

        Match menuKeys = Regex.Match(comMethods, @"MenuKeys\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.None, UiResourceRegexTimeout);
        Assert.True(menuKeys.Success, "ComMethods must declare the MenuKeys array the shell menu renders.");
        var keys = Regex.Matches(menuKeys.Groups["body"].Value, "\"(?<key>[^\"]+)\"", RegexOptions.None, UiResourceRegexTimeout)
            .Select(m => m.Groups["key"].Value)
            .ToList();

        Match rootKey = Regex.Match(comMethods, @"RootTitleKey\s*=\s*""(?<key>[^""]+)""", RegexOptions.None, UiResourceRegexTimeout);
        Assert.True(rootKey.Success, "ComMethods must declare the root menu label's resource key.");
        keys.Add(rootKey.Groups["key"].Value);

        // Declaring a key is not consuming it: the shell has to hand each one to GetString,
        // otherwise the resw entry is read by nobody and the equality above proves nothing.
        Assert.True(comMethods.Contains("ShellUtils.GetString(MenuKeys[idx])", StringComparison.Ordinal),
            "Every menu entry must resolve its label through ShellUtils.GetString(MenuKeys[idx]).");
        Assert.True(comMethods.Contains("ShellUtils.GetString(RootTitleKey)", StringComparison.Ordinal),
            "The root menu entry must resolve its label through ShellUtils.GetString(RootTitleKey).");

        Assert.True(keys.Count == keys.Distinct(StringComparer.Ordinal).Count(),
            "The shell must not list the same resource key twice.");
        return keys.ToArray();
    }

    public static void RegisterUiResourceCoverageTests(TestRunner runner)
    {
        runner.Run("Shell resources: resw keys and the keys the shell menu consumes are bidirectionally equal", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string[] consumed = GetShellConsumedResourceKeys(root);
            string stringsDir = Path.Combine(root, "packaging", "msix", "Strings");

            // A new UI language must be added on both sides at once, not only to Localization.cs.
            string[] expected = Localization.SupportedLanguages.Select(CultureFolder).OrderBy(c => c, StringComparer.Ordinal).ToArray();
            string[] actual = Directory.GetDirectories(stringsDir).Select(Path.GetFileName).Where(d => !string.IsNullOrEmpty(d)).Cast<string>().OrderBy(c => c, StringComparer.Ordinal).ToArray();
            Assert.True(expected.SequenceEqual(actual),
                $"The packaged language folders must be exactly the app's supported languages " +
                $"(expected {string.Join(", ", expected)}; found {string.Join(", ", actual)}).");

            foreach (string culture in actual)
            {
                var doc = XDocument.Load(Path.Combine(stringsDir, culture, "Resources.resw"));
                var entries = doc.Root?.Elements("data").ToList() ?? new List<XElement>();
                string[] names = entries.Select(e => e.Attribute("name")?.Value ?? "").ToArray();

                Assert.True(names.Length == names.Distinct(StringComparer.Ordinal).Count(),
                    $"{culture}/Resources.resw declares a <data> name twice; the shell reads the first match.");

                string[] missing = consumed.Where(k => !names.Contains(k, StringComparer.Ordinal)).ToArray();
                Assert.True(missing.Length == 0,
                    $"{culture}/Resources.resw is missing {missing.Length} key(s) the shell renders: " +
                    $"{string.Join(", ", missing)}. GetString prints the key name verbatim when it is absent.");

                string[] residual = names.Where(n => !consumed.Contains(n, StringComparer.Ordinal)).ToArray();
                Assert.True(residual.Length == 0,
                    $"{culture}/Resources.resw carries {residual.Length} key(s) nothing reads: " +
                    $"{string.Join(", ", residual)}. Wire them to a consumer or delete them from all 5 languages.");

                foreach (var entry in entries)
                {
                    string name = entry.Attribute("name")?.Value ?? "";
                    string value = entry.Element("value")?.Value ?? "";
                    Assert.False(string.IsNullOrWhiteSpace(value),
                        $"{culture}/Resources.resw has an empty value for {name}; the menu entry would render blank.");
                    Assert.False(string.Equals(value, name, StringComparison.Ordinal),
                        $"{culture}/Resources.resw uses {name} as its own translation.");
                }
            }
        });

        runner.Run("Convert registry: every command label key is declared and translated in all 5 languages", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            // Every label key the table declares, straight from the registry source.
            string registrySource = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
            string[] declared = Regex.Matches(registrySource, "\"(?<key>cmd_[a-z_]+)\"", RegexOptions.None, UiResourceRegexTimeout)
                .Select(m => m.Groups["key"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            Assert.True(declared.Length > 0, "The convert registry must declare at least one label key.");

            // ... must be exactly the keys reachable through the per-type command lists, so a command
            // added to the table without a file type cannot slip past this guard.
            string[] reached = new[] { "pdf", "word", "excel", "ppt", "image" }
                .SelectMany(ConvertCommandRegistry.GetCommandsForType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Assert.True(reached.Length > 0, "The convert registry must expose its commands per file type.");

            foreach (string command in reached)
            {
                string key = ConvertCommandRegistry.GetLabelKey(command);
                // GetLabelKey falls back to the command id for an unknown command, which the UIs then
                // render verbatim (e.g. "img-to-png" in the status bar), so that counts as undeclared.
                Assert.False(string.Equals(key, command, StringComparison.Ordinal),
                    $"'{command}' has no label key in ConvertCommandRegistry; the raw command id would be shown.");
                Assert.True(ConvertCommandRegistry.IsKnownCommand(command),
                    $"'{command}' is listed under a file type but is not in the command table.");
            }

            string[] reachedKeys = reached.Select(ConvertCommandRegistry.GetLabelKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            Assert.True(declared.SequenceEqual(reachedKeys),
                $"Every label key in the command table must be reachable from a file type. " +
                $"Table only: {string.Join(", ", declared.Except(reachedKeys))}; " +
                $"reachable only: {string.Join(", ", reachedKeys.Except(declared))}.");

            foreach (string key in declared)
            {
                foreach (string lang in Localization.SupportedLanguages)
                {
                    Assert.True(Localization.HasExactTranslation(key, lang),
                        $"{key} is a command label but has no {lang} translation; the UI would show the key name.");
                }
            }
        });
    }
}
