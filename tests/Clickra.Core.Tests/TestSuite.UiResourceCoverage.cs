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
/// 三條「畫面上與上架時會出現的字都必須有一份宣告過的翻譯」的來源端守門：
///
/// 1. Win32 Shell 的右鍵選單直接讀 packaging/msix/Strings/&lt;culture&gt;/Resources.resw，
///    而 ShellUtils.GetString 找不到鍵時把**鍵名本身**印給使用者。因此 resw 的鍵集合與
///    Shell 及 Manifest 實際查詢的鍵集合必須雙向相等：少一條就是選單上出現 Menu_Xxx，
///    多一條就是五種語言都翻好了卻沒有任何人看得到的殘餘鍵。
///
/// 2. ConvertCommandRegistry 是命令中繼資料的單一來源，它的 LabelKey 由 Fluent 與 CLI
///    Dashboard 動態查表（GetLabelKey）之後才送進 Localization。這種間接查詢不會被任何
///    靜態掃描抓到 —— 先前就有 6 個圖片命令的標籤鍵從未被宣告，畫面會直接顯示
///    cmd_img_to_png 這種原始鍵名 —— 所以在這裡用執行期的登錄表逐一驗證。
///
/// 3. Microsoft Store 上架清單文件（docs/StoreListing_*.md）必須與 Localization 支援語言
///    雙向嚴格一致、檔案齊全、格式合規且九大 Partner Center 欄位無遺漏。
/// </summary>
static partial class TestSuite
{
    private static readonly TimeSpan UiResourceRegexTimeout = TimeSpan.FromSeconds(1);
    private const string ShellCommandMessagePrefix = "Shell command '";
    /// <summary>UI 語言的程式碼（zh-TW）與 resw 資料夾名稱（zh-tw）互轉。</summary>
    private static string CultureFolder(string languageCode) => languageCode.ToLowerInvariant();

    /// <summary>
    /// 應用程式支援語言與 docs/StoreListing_*.md 檔案名稱的對應關係。
    /// 任何新語言的加入都必須在此處宣告對應的商店說明文件。
    /// </summary>
    private static readonly Dictionary<string, string> StoreListingFileByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-TW"] = "StoreListing_ZH.md",
        ["en-US"] = "StoreListing_EN.md",
        ["ja-JP"] = "StoreListing_JA.md",
        ["ko-KR"] = "StoreListing_KO.md",
        ["zh-CN"] = "StoreListing_ZH-CN.md",
    };

    /// <summary>
    /// Microsoft Partner Center 商店清單文件必須具備的 9 個標準 H2 欄位。
    /// </summary>
    private static readonly string[] RequiredStoreListingSections =
    {
        "Product Name",
        "Description",
        "What's new in this version",
        "Product Features",
        "Short title",
        "Voice title",
        "Short description",
        "Keywords",
        "Copyright and trademark info"
    };

    private static Dictionary<string, string> ParseStoreListingSections(string markdown)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string currentSection = "";
        var lines = new List<string>();

        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(currentSection))
                {
                    sections[currentSection] = string.Join(Environment.NewLine, lines).Trim();
                    lines.Clear();
                }
                currentSection = line.Substring(3).Trim();
            }
            else if (!string.IsNullOrEmpty(currentSection))
            {
                lines.Add(line);
            }
        }
        if (!string.IsNullOrEmpty(currentSection))
        {
            sections[currentSection] = string.Join(Environment.NewLine, lines).Trim();
        }
        return sections;
    }

    private static string[] ExtractBulletItems(string sectionContent)
    {
        using var reader = new StringReader(sectionContent);
        var items = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                string item = trimmed.Substring(2).Trim();
                if (!string.IsNullOrEmpty(item))
                {
                    items.Add(item);
                }
            }
        }
        return items.ToArray();
    }

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

    /// <summary>AppxManifest 透過 ms-resource: 語法引用的 resw 鍵（如 AppName、AppDescription）。</summary>
    private static string[] GetManifestConsumedResourceKeys(string repoRoot)
    {
        string manifestPath = Path.Combine(repoRoot, "packaging", "msix", "AppxManifest.xml");
        string manifestContent = File.ReadAllText(manifestPath);
        var matches = Regex.Matches(manifestContent, @"ms-resource:(?<key>[A-Za-z0-9_]+)", RegexOptions.None, UiResourceRegexTimeout);
        var keys = matches.Select(m => m.Groups["key"].Value).Distinct(StringComparer.Ordinal).ToList();

        Assert.True(keys.Contains("AppName", StringComparer.Ordinal),
            "AppxManifest.xml must reference ms-resource:AppName.");
        Assert.True(keys.Contains("AppDescription", StringComparer.Ordinal),
            "AppxManifest.xml must reference ms-resource:AppDescription.");

        return keys.ToArray();
    }

    private static (string[] SubArgs, string[] MenuKeys, string[] IconFiles, int[] MultiFileIndices) GetShellCommandDefinitions(string repoRoot)
    {
        string comMethods = File.ReadAllText(Path.Combine(repoRoot, "src", "ClickraShell", "ComMethods.cs"));

        string[] ParseArray(string name)
        {
            Match match = Regex.Match(
                comMethods,
                name + @"\s*=\s*\{(?<body>[^}]*)\}",
                RegexOptions.None,
                UiResourceRegexTimeout);
            Assert.True(match.Success, "ComMethods must declare the " + name + " array.");
            return Regex.Matches(
                    match.Groups["body"].Value,
                    "\"(?<value>[^\"]+)\"",
                    RegexOptions.None,
                    UiResourceRegexTimeout)
                .Select(m => m.Groups["value"].Value)
                .ToArray();
        }

        Match multiFile = Regex.Match(
            comMethods,
            @"(?<indices>\d+(?:\s+or\s+\d+)*)\s*=>\s*files\.Count\s*>\s*1",
            RegexOptions.None,
            UiResourceRegexTimeout);
        Assert.True(multiFile.Success, "ComMethods must declare the multi-file command gate.");
        int[] multiFileIndices = Regex.Matches(
                multiFile.Groups["indices"].Value,
                @"\b\d+\b",
                RegexOptions.None,
                UiResourceRegexTimeout)
            .Select(m => int.Parse(m.Value))
            .ToArray();

        return (ParseArray("SubArgs"), ParseArray("MenuKeys"), ParseArray("IconFiles"), multiFileIndices);
    }

    public static void RegisterUiResourceCoverageTests(TestRunner runner)
    {
        runner.RunGuard("Package resources: resw keys and the keys consumed by shell menu and manifest are bidirectionally equal", TestShellResourceCoverage);
        runner.RunGuard("Convert registry: every command label key is declared and translated in all 5 languages", TestConvertRegistryLabelCoverage);
        runner.RunGuard("Shell menu: commands reconcile with ConvertCommandRegistry metadata", TestShellCommandRegistryCoverage);
        runner.RunGuard("Store listing: docs/StoreListing_*.md covers all supported languages with complete fields", TestStoreListingCoverage);
    }

    private static void TestShellResourceCoverage()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string[] shellConsumed = GetShellConsumedResourceKeys(root);
        string[] manifestConsumed = GetManifestConsumedResourceKeys(root);
        string[] consumed = shellConsumed.Concat(manifestConsumed).Distinct(StringComparer.Ordinal).ToArray();
        string stringsDir = Path.Combine(root, "packaging", "msix", "Strings");

        string[] expected = Localization.SupportedLanguages.Select(CultureFolder).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        string[] actual = Directory.GetDirectories(stringsDir).Select(Path.GetFileName).Where(d => !string.IsNullOrEmpty(d)).Cast<string>().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.True(expected.SequenceEqual(actual),
            "The packaged language folders must be exactly the app's supported languages " +
            "(expected " + string.Join(", ", expected) + "; found " + string.Join(", ", actual) + ").");

        foreach (string culture in actual)
        {
            var doc = XDocument.Load(Path.Combine(stringsDir, culture, "Resources.resw"));
            var entries = doc.Root?.Elements("data").ToList() ?? new List<XElement>();
            string[] names = entries.Select(e => e.Attribute("name")?.Value ?? "").ToArray();

            Assert.True(names.Length == names.Distinct(StringComparer.Ordinal).Count(),
                culture + "/Resources.resw declares a <data> name twice; the shell reads the first match.");

            string[] missing = consumed.Where(k => !names.Contains(k, StringComparer.Ordinal)).ToArray();
            Assert.True(missing.Length == 0,
                culture + "/Resources.resw is missing " + missing.Length + " key(s) the shell renders: " +
                string.Join(", ", missing) + ". GetString prints the key name verbatim when it is absent.");

            string[] residual = names.Where(n => !consumed.Contains(n, StringComparer.Ordinal)).ToArray();
            Assert.True(residual.Length == 0,
                culture + "/Resources.resw carries " + residual.Length + " key(s) nothing reads: " +
                string.Join(", ", residual) + ". Wire them to a consumer or delete them from all 5 languages.");

            foreach (var entry in entries)
            {
                string name = entry.Attribute("name")?.Value ?? "";
                string value = entry.Element("value")?.Value ?? "";
                Assert.False(string.IsNullOrWhiteSpace(value),
                    culture + "/Resources.resw has an empty value for " + name + "; the menu entry would render blank.");
                Assert.False(string.Equals(value, name, StringComparison.Ordinal),
                    culture + "/Resources.resw uses " + name + " as its own translation.");
            }
        }
    }

    private static void TestConvertRegistryLabelCoverage()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string registrySource = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string[] declared = Regex.Matches(registrySource, "\"(?<key>cmd_[a-z_]+)\"", RegexOptions.None, UiResourceRegexTimeout)
            .Select(m => m.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.True(declared.Length > 0, "The convert registry must declare at least one label key.");

        string[] reached = new[] { "pdf", "word", "excel", "ppt", "image" }
            .SelectMany(ConvertCommandRegistry.GetCommandsForType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(reached.Length > 0, "The convert registry must expose its commands per file type.");

        foreach (string command in reached)
        {
            string key = ConvertCommandRegistry.GetLabelKey(command);
            Assert.False(string.Equals(key, command, StringComparison.Ordinal),
                "'" + command + "' has no label key in ConvertCommandRegistry; the raw command id would be shown.");
            Assert.True(ConvertCommandRegistry.IsKnownCommand(command),
                "'" + command + "' is listed under a file type but is not in the command table.");
        }

        string[] reachedKeys = reached.Select(ConvertCommandRegistry.GetLabelKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.True(declared.SequenceEqual(reachedKeys),
            "Every label key in the command table must be reachable from a file type. " +
            "Table only: " + string.Join(", ", declared.Except(reachedKeys)) + "; " +
            "reachable only: " + string.Join(", ", reachedKeys.Except(declared)) + ".");

        foreach (string key in declared)
        {
            foreach (string lang in Localization.SupportedLanguages)
            {
                Assert.True(Localization.HasExactTranslation(key, lang),
                    key + " is a command label but has no " + lang + " translation; the UI would show the key name.");
            }
        }
    }

    private static void TestShellCommandRegistryCoverage()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        var (subArgs, menuKeys, iconFiles, multiFileIndices) = GetShellCommandDefinitions(root);
        string shellSource = File.ReadAllText(Path.Combine(root, "src", "ClickraShell", "ComMethods.cs"));
        string cliSource = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));

        Assert.True(subArgs.Length > 0, "Shell menu must declare at least one command.");
        Assert.True(menuKeys.Length == subArgs.Length,
            "MenuKeys and SubArgs must have the same number of entries.");
        Assert.True(iconFiles.Length == subArgs.Length,
            "IconFiles and SubArgs must have the same number of entries.");
        Assert.True(subArgs.Length == subArgs.Distinct(StringComparer.Ordinal).Count(),
            "Shell SubArgs must not contain duplicate command ids.");

        string[] registryCommands = new[] { "pdf", "word", "excel", "ppt", "image" }
            .SelectMany(ConvertCommandRegistry.GetCommandsForType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(command => command, StringComparer.Ordinal)
            .ToArray();
        string[] shellCommands = subArgs.OrderBy(command => command, StringComparer.Ordinal).ToArray();
        Assert.True(shellCommands.SequenceEqual(registryCommands, StringComparer.Ordinal),
            "Shell menu must expose every production ConvertCommandRegistry command exactly once. " +
            "Shell: [" + string.Join(", ", shellCommands) + "] Registry: [" + string.Join(", ", registryCommands) + "]");
        Assert.True(shellSource.Contains("files.All(f => IsSupported(f, idx))", StringComparison.Ordinal),
            "Explorer commands must stay hidden unless every selected file is valid for that command.");
        Assert.True(cliSource.Contains("string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(command);", StringComparison.Ordinal),
            "CLI image dispatch must derive accepted extensions from ConvertCommandRegistry instead of a private list.");

        for (int i = 0; i < subArgs.Length; i++)
        {
            string command = subArgs[i];
            Assert.True(ConvertCommandRegistry.IsKnownCommand(command),
                ShellCommandMessagePrefix + command + "' is not registered in ConvertCommandRegistry.");

            string labelKey = ConvertCommandRegistry.GetLabelKey(command);
            Assert.False(string.Equals(labelKey, command, StringComparison.Ordinal),
                ShellCommandMessagePrefix + command + "' has no registered localization key.");
            Assert.True(ConvertCommandRegistry.GetAllowedExtensions(command).Length > 0,
                ShellCommandMessagePrefix + command + "' has no allowed input extensions.");

            int minFiles = ConvertCommandRegistry.GetMinFiles(command);
            bool shellRequiresMultiple = multiFileIndices.Contains(i);
            Assert.True((minFiles > 1) == shellRequiresMultiple,
                ShellCommandMessagePrefix + command + "' has MinFiles=" + minFiles +
                " but its ComMethods multi-file gate does not match.");
        }
    }

    private static void TestStoreListingCoverage()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string[] mappedLangs = StoreListingFileByLanguage.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string[] supportedLangs = Localization.SupportedLanguages.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(mappedLangs.SequenceEqual(supportedLangs),
            "StoreListingFileByLanguage must exactly match Localization.SupportedLanguages. " +
            "Mapped: [" + string.Join(", ", mappedLangs) + "], Supported: [" + string.Join(", ", supportedLangs) + "]");

        string docsDir = Path.Combine(root, "docs");
        string[] actualListingFiles = Directory.GetFiles(docsDir, "StoreListing_*.md")
            .Select(Path.GetFileName)
            .Where(f => !string.IsNullOrEmpty(f))
            .Cast<string>()
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] expectedListingFiles = StoreListingFileByLanguage.Values
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(expectedListingFiles.SequenceEqual(actualListingFiles, StringComparer.OrdinalIgnoreCase),
            "The StoreListing markdown files in docs/ must exactly match the application's supported languages. " +
            "Expected: [" + string.Join(", ", expectedListingFiles) + "], Found: [" + string.Join(", ", actualListingFiles) + "]");

        foreach (string lang in Localization.SupportedLanguages)
        {
            string fileName = StoreListingFileByLanguage[lang];
            string filePath = Path.Combine(docsDir, fileName);
            Assert.True(File.Exists(filePath), "Store listing file for " + lang + " (" + fileName + ") must exist.");

            string content = File.ReadAllText(filePath);
            Assert.False(string.IsNullOrWhiteSpace(content), fileName + " must not be empty.");
            Assert.True(content.StartsWith("# Microsoft Store", StringComparison.OrdinalIgnoreCase),
                fileName + " must begin with '# Microsoft Store' header.");

            var sections = ParseStoreListingSections(content);
            foreach (string requiredSection in RequiredStoreListingSections)
            {
                Assert.True(sections.ContainsKey(requiredSection),
                    fileName + " is missing required section '## " + requiredSection + "'.");
                Assert.False(string.IsNullOrWhiteSpace(sections[requiredSection]),
                    fileName + " has empty content for required section '## " + requiredSection + "'.");
            }

            Assert.True(string.Equals(sections["Product Name"].Trim(), "Clickra", StringComparison.Ordinal),
                fileName + " Product Name must be 'Clickra', found '" + sections["Product Name"] + "'.");
            Assert.True(string.Equals(sections["Short title"].Trim(), "Clickra", StringComparison.Ordinal),
                fileName + " Short title must be 'Clickra'.");
            Assert.True(string.Equals(sections["Voice title"].Trim(), "Clickra", StringComparison.Ordinal),
                fileName + " Voice title must be 'Clickra'.");

            string[] features = ExtractBulletItems(sections["Product Features"]);
            Assert.True(features.Length > 0, fileName + " must have at least one feature bullet in Product Features.");
            Assert.True(features.Length <= 20,
                fileName + " has " + features.Length + " features, exceeding Partner Center maximum of 20.");

            string[] keywords = ExtractBulletItems(sections["Keywords"]);
            Assert.True(keywords.Length > 0, fileName + " must have at least one keyword bullet in Keywords.");
            Assert.True(keywords.Length <= 7,
                fileName + " has " + keywords.Length + " keywords, exceeding Partner Center maximum of 7.");

            Assert.True(sections["Description"].Length >= 50,
                fileName + " Description is too short (" + sections["Description"].Length + " chars).");
            Assert.True(sections["Short description"].Length >= 20,
                fileName + " Short description is too short (" + sections["Short description"].Length + " chars).");
            Assert.True(sections["Copyright and trademark info"].Contains("Youchen Jiang", StringComparison.OrdinalIgnoreCase),
                fileName + " Copyright info must credit the author.");
        }
    }
}
