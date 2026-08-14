using System.Globalization;
using Clickra.Core.Models;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private static TranslationPageDiagnostics Diagnostics(string sourceFile, int page)
    {
        // test_pdfs/ is git-ignored (large PDFs). On a fresh CI checkout the
        // repo root cannot even be located, so these layout assertions cannot
        // run: skip instead of failing so the gated job reports the
        // deterministic suite's true result.
        string root;
        try
        {
            root = RepoRoot().Value;
        }
        catch (InvalidOperationException)
        {
            throw new TestSkippedException("Could not locate Clickra repo root (test_pdfs/ fixtures are git-ignored).");
        }

        var path = Path.Combine(root, "test_pdfs", "source", sourceFile);
        if (!File.Exists(path))
        {
            throw new TestSkippedException($"Missing test PDF fixture: {path}");
        }
        return PdfTranslateProcessor.AnalyzePageParagraphDiagnostics(path, page);
    }

    private static PdfParagraph UninitializedParagraph(string text, double width, double height)
    {
        var paragraph = (PdfParagraph)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(PdfParagraph));
        paragraph.TextWithPlaceholders = text;
        paragraph.X0 = 0;
        paragraph.Y0 = 0;
        paragraph.X1 = width;
        paragraph.Y1 = height;
        return paragraph;
    }

    private static PdfParagraph LayoutParagraph(
        string source,
        string translated,
        double x0,
        double y0,
        double x1,
        double y1,
        double fontSize = 10)
    {
        var paragraph = new PdfParagraph(Array.Empty<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>())
        {
            TextWithPlaceholders = source,
            TranslatedText = translated,
            X0 = x0,
            Y0 = y0,
            X1 = x1,
            Y1 = y1,
            AverageFontSize = fontSize,
            SourceVisualFontSize = fontSize,
            SourceLineHeight = fontSize,
            SemanticRole = PdfParagraphSemanticRole.Body
        };
        foreach (var (name, value) in new[]
        {
            (nameof(PdfParagraph.OriginalX0), x0),
            (nameof(PdfParagraph.OriginalY0), y0),
            (nameof(PdfParagraph.OriginalX1), x1),
            (nameof(PdfParagraph.OriginalY1), y1)
        })
        {
            typeof(PdfParagraph).GetProperty(name)?.SetValue(paragraph, value);
        }
        return paragraph;
    }

    private static void AssertParagraph(
        TranslationPageDiagnostics page,
        string text,
        Func<TranslationParagraphDiagnostics, bool> predicate)
    {
        var matches = page.Paragraphs
            .Where(p => p.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(matches.Count > 0, $"Could not find paragraph containing '{text}' on page {page.PageNumber}.");
        Assert.True(matches.Any(predicate),
            $"Paragraph containing '{text}' did not satisfy predicate. Matches:\n" +
            string.Join("\n", matches.Select(Describe)));
    }

    /// <summary>Shared predicates for layout assertions; keep them here so the
    /// per-suite test files do not repeat the same lambda bodies.</summary>
    private static bool IsTableBypassed(TranslationParagraphDiagnostics p) =>
        p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent;

    private static bool IsPlainTranslatable(TranslationParagraphDiagnostics p) =>
        !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent;

    private static bool IsBodyProse(TranslationParagraphDiagnostics p) =>
        !p.IsBypassed && !p.IsCode && !p.IsDiagram && p.IsBodyProse;

    private static void AssertAllParagraphs(
        TranslationPageDiagnostics page,
        string text,
        Func<TranslationParagraphDiagnostics, bool> predicate)
    {
        var matches = page.Paragraphs
            .Where(p => p.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(matches.Count > 0, $"Could not find paragraph containing '{text}' on page {page.PageNumber}.");
        Assert.True(matches.All(predicate),
            $"Not all paragraphs containing '{text}' satisfied predicate. Matches:\n" +
            string.Join("\n", matches.Select(Describe)));
    }

    private static PathInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "test_pdfs", "source")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src", "Clickra.Core")))
            {
                return new PathInfo(dir.FullName);
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Clickra repo root.");
    }

    private static string Describe(TranslationParagraphDiagnostics p) =>
        $"  [{p.Index}] bypass={p.IsBypassed} table={p.IsTable} code={p.IsCode} " +
        $"diagram={p.IsDiagram} gray={p.IsGrayPromptContent} body={p.IsBodyProse} " +
        $"bbox=[{p.X0:F1},{p.Y0:F1},{p.X1:F1},{p.Y1:F1}] " +
        $"text='{Short(p.Text)}'";

    private static string Short(string value)
    {
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= 120 ? value : value[..120] + "...";
    }
}

sealed class PathInfo(string value)
{
    public static PathInfo operator /(PathInfo left, string right) =>
        new(Path.Combine(left.Value, right));

    public string Value { get; } = value;
    public override string ToString() => Value;
}

sealed class TestRunner
{
    public int Passed { get; private set; }
    public int Failures { get; private set; }
    public int Skipped { get; private set; }

    public void Run(string name, Action test)
    {
        // Reset ambient culture before each test and restore it afterwards so
        // one test that changes CurrentCulture/CurrentUICulture cannot leak
        // into the next (PR-B stability, 6.3). Tests that need a specific
        // culture set it inside their own body.
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            test();
            Passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (TestSkippedException ex)
        {
            Skipped++;
            Console.WriteLine($"SKIP {name}");
            Console.WriteLine(ex.Message);
        }
        catch (Exception ex)
        {
            Failures++;
            Console.WriteLine($"FAIL {name}");
            Console.WriteLine(ex.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
        }
    }
}

/// <summary>Thrown when a test cannot run because its git-ignored PDF fixture
/// is absent (e.g. a fresh CI checkout). Counted as skipped, not failed.</summary>
public sealed class TestSkippedException(string message) : Exception(message);

static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void Equal(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
