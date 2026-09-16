using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clickra.Core.Tests;

/// <summary>
/// XAML 的在地化守門：介面標記不得寫入任何 CJK 文案（漢字、假名、諺文、全形標點），
/// 所有使用者可見文字都必須來自 Localization 或 x:Uid。唯一的例外是語言切換器的
/// 「語言自稱」（例如 繁體中文 / 日本語 / 한국어），它們本來就該以該語言原文呈現。
/// </summary>
static partial class TestSuite
{
    /// <summary>
    /// CJK 文案字元：CJK 標點、假名、CJK 擴充 A、漢字、諺文、相容漢字、全形字元。
    /// 涵蓋假名與諺文，否則只寫日文或韓文的標記會完全逃過檢查（Hangul 不在
    /// \u4e00-\u9fff 之內）。
    /// </summary>
    private static readonly Regex XamlCjkTextPattern = new(
        @"[\u3000-\u303F\u3040-\u30FF\u3400-\u4DBF\u4E00-\u9FFF\uAC00-\uD7AF\uF900-\uFAFF\uFF00-\uFFEF]",
        RegexOptions.Compiled);

    /// <summary>
    /// 允許出現在標記裡的非 ASCII 內容：各語言的自稱，以及後面的語言代碼。這兩者從
    /// 命中行移除後若已無 CJK 字元，該行就只是語言選單項目。
    /// </summary>
    private static readonly Regex XamlEndonymPattern = new(
        @"繁體中文|简体中文|日本語|한국어|中文|\((?:zh-TW|zh-CN|en-US|ja-JP|ko-KR|en|ja|ko)\)",
        RegexOptions.Compiled);

    /// <summary>Clickra.Fluent 的每一個介面標記檔都必須被涵蓋。</summary>
    private static readonly string[] ExpectedFluentXamlFiles =
    {
        "App.xaml",
        "MainPage.xaml",
        "MainWindow.xaml",
        "TaskProgressPage.xaml",
        "Controls/SplitPagesOverlay.xaml",
        "Controls/VisualSplitterControl.xaml"
    };

    public static void RegisterXamlLocalizationTests(TestRunner runner)
    {
        runner.Run("Localization guard: every Clickra.Fluent XAML file is covered by the markup guard", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            var scanned = ScanXamlFiles(root)
                .Select(p => Path.GetRelativePath(Path.Combine(root, "src", "Clickra.Fluent"), p).Replace('\\', '/'))
                .ToList();

            Assert.True(scanned.Count > 0, "Expected to find XAML files to scan under src/.");

            foreach (string expected in ExpectedFluentXamlFiles)
            {
                Assert.True(scanned.Contains(expected, StringComparer.Ordinal),
                    $"The markup guard must cover src/Clickra.Fluent/{expected} (found: {string.Join(", ", scanned)}).");
            }
        });

        runner.Run("Localization guard: no hardcoded CJK text in XAML markup", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            var violations = new List<string>();

            foreach (string file in ScanXamlFiles(root))
            {
                string relPath = Path.GetRelativePath(root, file).Replace('\\', '/');
                string raw = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

                string[] lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!XamlCjkTextPattern.IsMatch(line)) continue;

                    // Language endonyms are the one intentional exception: strip the autonym
                    // and its language code, and only an otherwise-CJK-free line passes. A
                    // literal next to an autonym is still reported.
                    if (!XamlCjkTextPattern.IsMatch(XamlEndonymPattern.Replace(line, string.Empty))) continue;

                    violations.Add($"{relPath}:{i + 1}: {line.Trim()}");
                }
            }

            Assert.True(violations.Count == 0,
                "XAML markup must not hardcode CJK text; take it from Localization or x:Uid instead." +
                Environment.NewLine + string.Join(Environment.NewLine, violations));
        });
    }

    /// <summary>Every .xaml file under src/, excluding build output.</summary>
    private static IEnumerable<string> ScanXamlFiles(string root)
    {
        string srcDir = Path.Combine(root, "src");
        Assert.True(Directory.Exists(srcDir), $"Expected the source directory to exist: {srcDir}");

        return Directory.EnumerateFiles(srcDir, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }
}
