using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clickra.Core.Tests;

/// <summary>
/// XAML 的在地化守門：介面標記不得寫入任何 CJK 文案（漢字、假名、諺文、全形標點），
/// 所有使用者可見文字都必須來自 Localization 或 x:Uid。唯一的例外是語言切換器的
/// 「語言自稱」（例如 繁體中文 / 日本語 / 한국어），它們本來就該以該語言原文呈現。
/// </summary>
static partial class TestSuite
{
    private static readonly TimeSpan XamlRegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// CJK 文案字元：CJK 標點、假名、CJK 擴充 A、漢字、諺文、相容漢字、全形字元。
    /// 涵蓋假名與諺文，否則只寫日文或韓文的標記會完全逃過檢查（Hangul 不在
    /// \u4e00-\u9fff 之內）。
    /// </summary>
    private static readonly Regex XamlCjkTextPattern = new(
        @"[\u3000-\u303F\u3040-\u30FF\u3400-\u4DBF\u4E00-\u9FFF\uAC00-\uD7AF\uF900-\uFAFF\uFF00-\uFFEF]",
        RegexOptions.Compiled,
        XamlRegexTimeout);

    /// <summary>
    /// 允許出現在標記裡的非 ASCII 內容：各語言的自稱，以及後面的語言代碼。這兩者從
    /// 命中行移除後若已無 CJK 字元，該行就只是語言選單項目。
    /// </summary>
    private static readonly Regex XamlEndonymPattern = new(
        @"繁體中文|简体中文|日本語|한국어|中文|\((?:zh-TW|zh-CN|en-US|ja-JP|ko-KR|en|ja|ko)\)",
        RegexOptions.Compiled,
        XamlRegexTimeout);

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
        runner.RunGuard("Localization guard: every Clickra.Fluent XAML file is covered by the markup guard", TestEveryFluentXamlFileIsCovered);
        runner.RunGuard("Localization guard: no hardcoded CJK text in XAML markup", TestNoHardcodedCjkTextInXaml);
        runner.RunGuard("Fluent convert workspace: compact surfaces preserve first-screen density", TestCompactConvertWorkspace);
        runner.RunGuard("Fluent convert workspace: command groups preserve complete responsive wiring", TestConvertCommandGroupWiring);
    }

    private static void TestCompactConvertWorkspace()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        XDocument xaml = XDocument.Load(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml"));
        XElement dropZone = FindNamedXamlElement(xaml, "DropZone");
        XElement selectedFilesCard = FindNamedXamlElement(xaml, "SelectedFilesCard");
        XElement runCard = FindNamedXamlElement(xaml, "ConvertRunCard");

        Assert.True(double.TryParse(dropZone.Attribute("MinHeight")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minHeight),
            "DropZone must declare a numeric MinHeight so the compact layout contract is explicit.");
        Assert.True(minHeight <= 140,
            $"DropZone MinHeight must stay compact (<= 140); found {minHeight}.");
        Assert.Equal("2", dropZone.Attribute("Grid.ColumnSpan")?.Value ?? string.Empty);
        Assert.Equal("Collapsed", selectedFilesCard.Attribute("Visibility")?.Value ?? string.Empty);
        Assert.Equal("Collapsed", runCard.Attribute("Visibility")?.Value ?? string.Empty);

        string codeBehind = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml.cs"));
        Assert.True(codeBehind.Contains("SelectedFilesCard.Visibility = _selectedFiles.Count > 0", StringComparison.Ordinal),
            "Selected files must only consume layout space after files are selected.");
        Assert.True(codeBehind.Contains("ConvertRunCard.Visibility = _selectedFiles.Count > 0 || _isRunning", StringComparison.Ordinal),
            "Run controls must stay collapsed until selection or active conversion makes them relevant.");
    }

    private static void TestConvertCommandGroupWiring()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        XDocument xaml = XDocument.Load(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml"));
        XElement commandCard = FindNamedXamlElement(xaml, "ConvertCommandCard");
        string[] actualTags = commandCard.Descendants()
            .Where(e => e.Name.LocalName == "Button" && e.Attribute("Tag") is not null)
            .Select(e => e.Attribute("Tag")!.Value)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
        string[] expectedTags =
        {
            "compress-pdf", "decrypt-pdf", "excel2pdf", "img-merge", "img-stitch", "img-to-gif", "img-to-heic", "img-to-jpg",
            "img-to-png", "img-to-webp", "img2pdf", "merge-pdf", "ppt2pdf", "split-pdf", "translate-pdf", "word2pdf"
        };

        Assert.True(actualTags.SequenceEqual(expectedTags),
            "The Fluent convert workspace must expose exactly the registered 16 command buttons. " +
            $"Expected: [{string.Join(", ", expectedTags)}]; actual: [{string.Join(", ", actualTags)}].");
        Assert.Equal(3, CountGridColumns(FindNamedXamlElement(xaml, "OfficeCommandGrid")));
        Assert.Equal(5, CountGridColumns(FindNamedXamlElement(xaml, "PdfCommandGrid")));
        Assert.Equal(4, CountGridColumns(FindNamedXamlElement(xaml, "ImageCommandGrid")));

        string codeBehind = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml.cs"));
        Assert.True(codeBehind.Contains("SetActiveColumns(page.OfficeCommandGrid, narrow ? 2 : 3);", StringComparison.Ordinal),
            "Office commands must reflow from three wide columns to two narrow columns.");
        Assert.True(codeBehind.Contains("SetActiveColumns(page.PdfCommandGrid, narrow ? 2 : 5);", StringComparison.Ordinal),
            "PDF commands must reflow from five wide columns to two narrow columns.");
        Assert.True(codeBehind.Contains("SetActiveColumns(page.ImageCommandGrid, narrow ? 2 : 4);", StringComparison.Ordinal),
            "Image commands must reflow from four wide columns to two narrow columns.");
    }

    private static XElement FindNamedXamlElement(XDocument xaml, string name)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement? element = xaml.Descendants().SingleOrDefault(e => string.Equals(e.Attribute(x + "Name")?.Value, name, StringComparison.Ordinal));
        Assert.True(element is not null, $"MainPage.xaml must declare x:Name=\"{name}\".");
        return element!;
    }

    private static int CountGridColumns(XElement grid) =>
        grid.Elements().Single(e => e.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Count(e => e.Name.LocalName == "ColumnDefinition");

    private static void TestEveryFluentXamlFileIsCovered()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        var scanned = ScanXamlFiles(root)
            .Select(p => Path.GetRelativePath(Path.Combine(root, "src", FluentProjectDirectory), p).Replace('\\', '/'))
            .ToList();

        Assert.True(scanned.Count > 0, "Expected to find XAML files to scan under src/.");

        foreach (string expected in ExpectedFluentXamlFiles)
        {
            Assert.True(scanned.Contains(expected, StringComparer.Ordinal),
                $"The markup guard must cover src/Clickra.Fluent/{expected} (found: {string.Join(", ", scanned)}).");
        }
    }

    private static void TestNoHardcodedCjkTextInXaml()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        var violations = new List<string>();
        foreach (string file in ScanXamlFiles(root))
        {
            CollectHardcodedCjkViolations(root, file, violations);
        }

        Assert.True(violations.Count == 0,
            "XAML markup must not hardcode CJK text; take it from Localization or x:Uid instead." +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void CollectHardcodedCjkViolations(string root, string file, List<string> violations)
    {
        string relPath = Path.GetRelativePath(root, file).Replace('\\', '/');
        string raw = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty, RegexOptions.Singleline, XamlRegexTimeout);
        string[] lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!XamlCjkTextPattern.IsMatch(line)) continue;
            if (!XamlCjkTextPattern.IsMatch(XamlEndonymPattern.Replace(line, string.Empty))) continue;

            violations.Add($"{relPath}:{i + 1}: {line.Trim()}");
        }
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
