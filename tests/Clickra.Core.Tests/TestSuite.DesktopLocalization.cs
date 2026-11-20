using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core;

namespace Clickra.Core.Tests;

/// <summary>
/// Guards for the 4.0 window's text (docs/specs/v4_localization_plan.md section 5 item 7 and
/// docs/specs/ui_writing_guide.md section 5): no Chinese written into views or view models, every key
/// the window asks for exists in all five languages, parameters match across languages, and each text is
/// a whole sentence in the agreed wording.
/// </summary>
static partial class TestSuite
{
    private static readonly TimeSpan DesktopTextRegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex DesktopCjk = new(@"[぀-ヿ㐀-鿿가-힯＀-￯　-〿]",
        RegexOptions.Compiled, DesktopTextRegexTimeout);

    private static readonly Regex DesktopKeyLookup = new(
        @"Strings\.(?:T|F|Count|Around)\(\s*""(ui_[a-z0-9_]+)""|\{l:T (ui_[a-z0-9_]+)\}",
        RegexOptions.Compiled, DesktopTextRegexTimeout);

    private static readonly Regex DesktopParameter = new(@"\{([a-z]+)\}", RegexOptions.Compiled, DesktopTextRegexTimeout);

    /// <summary>Words the writing guide (section 2) retired; the zh-TW text must use the agreed word instead.</summary>
    private static readonly string[] RetiredTraditionalChineseWords =
    {
        "你", "您", "...", "轉換中", "已取消", "暫停中", "取消操作", "來源資料夾", "可能已被移動或刪除", "讀到：", "保持原樣", "印表機預設",
    };

    public static void RegisterDesktopLocalizationTests(TestRunner runner)
    {
        runner.RunGuard("Desktop text: views and view models hold no hard-coded Chinese, Japanese or Korean", GuardDesktopHasNoHardCodedText);
        runner.RunGuard("Desktop text: every ui_ key the window looks up exists in all five languages", GuardDesktopKeysAreTranslated);
        runner.RunGuard("Desktop text: ui_ texts keep the same named parameters in every language", GuardDesktopParametersMatch);
        runner.RunGuard("Desktop text: ui_ texts are whole sentences in the agreed wording", GuardDesktopTextsAreWholeSentences);
    }

    private static IEnumerable<string> DesktopSourceFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src", DesktopProjectDir), "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string WithoutComments(string text)
    {
        string stripped = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline, DesktopTextRegexTimeout);
        stripped = Regex.Replace(stripped, @"<!--.*?-->", string.Empty, RegexOptions.Singleline, DesktopTextRegexTimeout);
        return Regex.Replace(stripped, @"(?m)^\s*//[^\n]*|\s//[^\n""]*$", string.Empty, RegexOptions.None, DesktopTextRegexTimeout);
    }

    private static void GuardDesktopHasNoHardCodedText()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        var hits = new List<string>();
        foreach (string file in DesktopSourceFiles(root))
        {
            string[] lines = WithoutComments(File.ReadAllText(file)).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (DesktopCjk.IsMatch(lines[i]))
                    hits.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}: {lines[i].Trim()}");
            }
        }

        Assert.True(hits.Count == 0,
            "Interface text belongs in the locale files as ui_ keys, not in .axaml or .cs " +
            $"(ui_writing_guide.md section 4). {hits.Count} line(s):{Environment.NewLine}" + string.Join(Environment.NewLine, hits.Take(30)));
    }

    private static void GuardDesktopKeysAreTranslated()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in DesktopSourceFiles(root))
        {
            foreach (Match m in DesktopKeyLookup.Matches(WithoutComments(File.ReadAllText(file))))
                keys.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        }
        Assert.True(keys.Count > 100, $"Expected the window to look up its text by ui_ keys, found {keys.Count}.");

        var missing = Localization.FindMissingTranslations(keys);
        Assert.True(missing.Count == 0,
            "Every ui_ key the window uses needs a non-empty text in all five languages:" + Environment.NewLine +
            Localization.FormatMissingReport(missing));
    }

    private static IEnumerable<string> DesktopUiKeys() =>
        Localization.GetAllKeys().Where(k => k.StartsWith("ui_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal);

    private static void GuardDesktopParametersMatch()
    {
        var problems = new List<string>();
        foreach (string key in DesktopUiKeys())
        {
            var reference = Parameters(Localization.T(key, "zh-TW"));
            foreach (string lang in Localization.SupportedLanguages)
            {
                var actual = Parameters(Localization.T(key, lang));
                // English key_one fills in the number itself ("1 file"), so it may drop {count}.
                bool singular = key.EndsWith("_one", StringComparison.Ordinal) && actual.IsSubsetOf(reference) &&
                                reference.Except(actual).All(p => p == "count");
                if (!actual.SetEquals(reference) && !singular)
                    problems.Add($"{key} [{lang}]: {{{string.Join("}, {", actual)}}} vs zh-TW {{{string.Join("}, {", reference)}}}");
            }
        }
        Assert.True(problems.Count == 0,
            "A ui_ text must name the same parameters in every language, or a value goes missing on screen:" +
            Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static HashSet<string> Parameters(string text) =>
        DesktopParameter.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static void GuardDesktopTextsAreWholeSentences()
    {
        var problems = new List<string>();
        foreach (string key in DesktopUiKeys())
        {
            if (key == "ui_list_separator") continue; // the separator itself is a single mark
            foreach (string lang in Localization.SupportedLanguages)
            {
                string text = Localization.T(key, lang);
                if (Regex.IsMatch(text, @"^[，。、；：,.;:）)]", RegexOptions.None, DesktopTextRegexTimeout) ||
                    Regex.IsMatch(text, @"[，、,]$", RegexOptions.None, DesktopTextRegexTimeout))
                    problems.Add($"{key} [{lang}] looks like a piece of a sentence: \"{text}\"");
            }

            string tw = Localization.T(key, "zh-TW");
            foreach (string word in RetiredTraditionalChineseWords.Where(w => tw.Contains(w, StringComparison.Ordinal)))
                problems.Add($"{key} [zh-TW] uses \"{word}\", which ui_writing_guide.md section 2 replaced: \"{tw}\"");
        }
        Assert.True(problems.Count == 0,
            "ui_ texts are stored as whole sentences in the agreed wording (ui_writing_guide.md sections 2 and 4):" +
            Environment.NewLine + string.Join(Environment.NewLine, problems));
    }
}
