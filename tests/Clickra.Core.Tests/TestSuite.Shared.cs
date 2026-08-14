using System.Globalization;
using Clickra.Core.Models;
using Clickra.Core.Processors;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

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

    /// <summary>Runs the real translation pipeline over a synthetic PDF built
    /// in-memory, so layout-classification tests no longer depend on the
    /// git-ignored test_pdfs/ fixtures. Returns the page-1 diagnostics.
    /// The temp file is deleted afterwards.</summary>
    private static TranslationPageDiagnostics DiagnosticsFromSynthetic(SyntheticGrayPage page)
    {
        EnsureFontResolver();
        string path = page.WriteTempPdf();
        try
        {
            return PdfTranslateProcessor.AnalyzePageParagraphDiagnostics(path, 1);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    private static bool _fontResolverInstalled;

    private static void EnsureFontResolver()
    {
        // PdfSharp 6.x needs an IFontResolver to locate TrueType fonts; the
        // default resolver is unreliable across machines/CI. Use the same
        // resolver Core production code uses (Arial, DFKai-SB and friends),
        // so synthetic pages and layout tests agree on font availability.
        if (_fontResolverInstalled) return;
        GlobalFontSettings.FontResolver = new ClickraFontResolver();
        _fontResolverInstalled = true;
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
    private readonly bool _requireFixtures;

    public TestRunner(bool requireFixtures = false)
    {
        _requireFixtures = requireFixtures;
    }

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
            // --require-fixtures turns missing fixtures into failures so a
            // fixture-expecting gate fails loudly (see Program.cs).
            if (_requireFixtures)
            {
                Failures++;
                Console.WriteLine($"FAIL {name}");
                Console.WriteLine(ex.Message);
            }
            else
            {
                Skipped++;
                Console.WriteLine($"SKIP {name}");
                Console.WriteLine(ex.Message);
            }
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

/// <summary>Builds a one-page PDF whose layout reproduces the vector
/// features the classifier relies on: gray-filled prompt boxes (with a
/// heading line plus instruction lines inside), optional standalone
/// heading/instruction text outside any box, and optional body prose.
/// The geometry is synthetic, so the tests run without the git-ignored
/// test_pdfs/ fixtures and reproduce on any developer machine.</summary>
sealed class SyntheticGrayPage
{
    private readonly List<GrayBox> _boxes = new();
    private readonly List<(double Y, string Text)> _outside = new();

    public SyntheticGrayPage AddBox(double x, double y, double width, double height, string heading, params string[] lines)
    {
        _boxes.Add(new GrayBox(x, y, width, height, heading, lines));
        return this;
    }

    /// <summary>Adds a text line outside any gray box (e.g. body prose that
    /// must stay translatable, or a standalone heading).</summary>
    public SyntheticGrayPage AddOutsideText(double y, string text)
    {
        _outside.Add((y, text));
        return this;
    }

    /// <summary>Writes the page to a unique temp file and returns its path.</summary>
    public string WriteTempPdf()
    {
        string path = Path.Combine(Path.GetTempPath(), $"clickra-synth-{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);
        using var gfx = XGraphics.FromPdfPage(page);

        foreach (var box in _boxes)
        {
            gfx.DrawRectangle(
                new XSolidBrush(XColor.FromArgb(230, 230, 230)),
                new XRect(box.X, box.Y, box.Width, box.Height));

            double cursor = box.Y + 10;
            DrawText(gfx, box.X + 8, cursor, box.Heading, bold: true);
            cursor += 26;
            foreach (string line in box.Lines)
            {
                DrawText(gfx, box.X + 8, cursor, line, bold: false);
                cursor += 26;
            }
        }

        foreach (var (y, text) in _outside)
        {
            DrawText(gfx, 72, y, text, bold: false);
        }

        doc.Save(path);
        return path;
    }

    private static void DrawText(XGraphics gfx, double x, double y, string text, bool bold)
    {
        var font = new XFont("Arial", 10, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        gfx.DrawString(text, font, XBrushes.Black, new XRect(x, y, 400, 20), XStringFormats.TopLeft);
    }

    private sealed record GrayBox(double X, double Y, double Width, double Height, string Heading, string[] Lines);
}

