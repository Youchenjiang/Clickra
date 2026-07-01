using Clickra.Core.Processors;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using System.Text;

static partial class TestSuite
{
    public static void RegisterPdfCompressionTests(TestRunner runner)
    {
        runner.Run("PDF compression parses user-facing level aliases", () =>
        {
            Assert.True(PdfCompressionOptions.TryParseLevel("", out var defaultLevel), "Expected empty level to use default.");
            Assert.True(defaultLevel == PdfCompressionLevel.Balanced, "Expected empty level to map to balanced.");
            Assert.True(PdfCompressionOptions.TryParseLevel("screen", out var small), "Expected screen alias to parse.");
            Assert.True(small == PdfCompressionLevel.Small, "Expected screen alias to map to small.");
            Assert.True(PdfCompressionOptions.TryParseLevel("printer", out var highQuality), "Expected printer alias to parse.");
            Assert.True(highQuality == PdfCompressionLevel.HighQuality, "Expected printer alias to map to high quality.");
            Assert.True(!PdfCompressionOptions.TryParseLevel("lossless", out _), "Expected unknown level to fail.");
        });

        runner.Run("PDF compression rejects unsupported level options", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}_compressed.pdf");
            try
            {
                CreateSamplePdf(input);
                var processor = new PdfCompressionProcessor();
                var options = new Dictionary<string, object> { { "level", "lossless" } };

                Assert.Throws<ArgumentException>(() =>
                    processor.Process(new List<string> { input }, output, options));
            }
            finally
            {
                TryDelete(input);
                TryDelete(output);
            }
        });

        runner.Run("PDF compression writes optimized PDF without external tools", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}_compressed.pdf");
            try
            {
                CreateSamplePdf(input);
                var processor = new PdfCompressionProcessor();
                var options = new Dictionary<string, object> { { "level", "small" } };

                processor.Process(new List<string> { input }, output, options);

                Assert.True(File.Exists(output), "Expected optimized PDF to be written.");
                Assert.True(new FileInfo(output).Length > 0, "Expected optimized PDF to be non-empty.");
            }
            finally
            {
                TryDelete(input);
                TryDelete(output);
            }
        });

        runner.Run("PDF compression processor delegates selected level to engine", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}_compressed.pdf");
            try
            {
                CreateSamplePdf(input);
                var engine = new RecordingPdfCompressionEngine();
                var processor = new PdfCompressionProcessor(engine);
                var options = new Dictionary<string, object> { { "level", "high" } };

                processor.Process(new List<string> { input }, output, options);

                Assert.True(engine.Level == PdfCompressionLevel.HighQuality, "Expected high level to be delegated to engine.");
                Assert.Equal(input, engine.InputPath);
                Assert.Equal(output, engine.OutputPath);
            }
            finally
            {
                TryDelete(input);
                TryDelete(output);
            }
        });

        runner.Run("PDF compression formats size reduction summary", () =>
        {
            string summary = NativePdfCompressionEngine.FormatCompressionSummary(5_308_416, 5_138_022);

            Assert.True(summary.Contains("5.06 MB -> 4.9 MB"), $"Expected before and after sizes in summary, got: {summary}");
            Assert.True(summary.Contains("減少 3.2%"), $"Expected reduction percentage in summary, got: {summary}");
        });

        runner.Run("PDF compression explains unchanged output size", () =>
        {
            string summary = NativePdfCompressionEngine.FormatCompressionSummary(1024, 1200);

            Assert.True(summary.Contains("檔案大小未明顯下降"), $"Expected unchanged-size explanation, got: {summary}");
        });

        runner.Run("PDF compression minifies verbose page content streams", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}_compressed.pdf");
            try
            {
                CreateVerboseContentPdf(input);
                new PdfCompressionProcessor().Process(
                    new List<string> { input },
                    output,
                    new Dictionary<string, object> { { "level", "balanced" } });

                long inputBytes = new FileInfo(input).Length;
                long outputBytes = new FileInfo(output).Length;
                Assert.True(outputBytes < inputBytes, $"Expected content stream minification to reduce size. Input: {inputBytes}, output: {outputBytes}.");
            }
            finally
            {
                TryDelete(input);
                TryDelete(output);
            }
        });

        runner.Run("PDF compression deduplicates repeated embedded font streams", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-compress-{Guid.NewGuid():N}_compressed.pdf");
            try
            {
                CreatePdfWithDuplicateFontStreams(input);
                new PdfCompressionProcessor().Process(
                    new List<string> { input },
                    output,
                    new Dictionary<string, object> { { "level", "balanced" } });

                long inputBytes = new FileInfo(input).Length;
                long outputBytes = new FileInfo(output).Length;
                Assert.True(outputBytes < inputBytes, $"Expected duplicate font streams to be deduplicated. Input: {inputBytes}, output: {outputBytes}.");
            }
            finally
            {
                TryDelete(input);
                TryDelete(output);
            }
        });
    }

    private static void CreateSamplePdf(string path)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Clickra compression fixture";
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(300);
        page.Height = XUnit.FromPoint(300);
        document.Save(path);
    }

    private static void CreateVerboseContentPdf(string path)
    {
        using var document = new PdfDocument();
        document.Options.NoCompression = true;
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(300);
        page.Height = XUnit.FromPoint(300);

        var builder = new StringBuilder();
        for (int i = 0; i < 800; i++)
        {
            builder.AppendLine($"% generated content comment {i:0000} {i * 7919:X8} {i * 104729:X8} {i * 15485863:X8} should be removed");
            builder.AppendLine("q      1   0   0   1      0      0      cm");
            builder.AppendLine($"{10 + i % 40}      {20 + i % 70}      12      6      re       f");
            builder.AppendLine("Q");
        }

        SetPageContent(page, Encoding.ASCII.GetBytes(builder.ToString()));
        document.Save(path);
    }

    private static void CreatePdfWithDuplicateFontStreams(string path)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(300);
        page.Height = XUnit.FromPoint(300);

        byte[] fakeFontBytes = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Clickra duplicate font payload 0123456789\n", 5000)));
        PdfDictionary fontFile1 = CreateFontFile(document, fakeFontBytes);
        PdfDictionary fontFile2 = CreateFontFile(document, fakeFontBytes);
        PdfDictionary descriptor1 = CreateFontDescriptor(document, "/ClickraFontA", fontFile1);
        PdfDictionary descriptor2 = CreateFontDescriptor(document, "/ClickraFontB", fontFile2);
        PdfDictionary font1 = CreateFont(document, "/ClickraFontA", descriptor1);
        PdfDictionary font2 = CreateFont(document, "/ClickraFontB", descriptor2);

        var fonts = new PdfDictionary(document);
        fonts.Elements.SetReference("/F1", font1);
        fonts.Elements.SetReference("/F2", font2);
        page.Resources.Elements["/Font"] = fonts;
        SetPageContent(page, Encoding.ASCII.GetBytes("BT /F1 12 Tf 20 260 Td (A) Tj /F2 12 Tf 20 240 Td (B) Tj ET"));

        document.Save(path);
    }

    private static void SetPageContent(PdfPage page, byte[] bytes)
    {
        PdfContent content = page.Contents.AppendContent();
        if (content.Stream == null)
            content.CreateStream(bytes);
        else
            content.Stream.Value = bytes;
        content.Elements.Remove("/Filter");
    }

    private static PdfDictionary CreateFontFile(PdfDocument document, byte[] bytes)
    {
        var fontFile = new PdfDictionary(document);
        fontFile.Elements.SetInteger("/Length1", bytes.Length);
        fontFile.CreateStream(bytes);
        document.Internals.AddObject(fontFile);
        return fontFile;
    }

    private static PdfDictionary CreateFontDescriptor(PdfDocument document, string fontName, PdfDictionary fontFile)
    {
        var descriptor = new PdfDictionary(document);
        descriptor.Elements.SetName("/Type", "/FontDescriptor");
        descriptor.Elements.SetName("/FontName", fontName);
        descriptor.Elements.SetInteger("/Flags", 4);
        descriptor.Elements.SetReference("/FontFile2", fontFile);
        document.Internals.AddObject(descriptor);
        return descriptor;
    }

    private static PdfDictionary CreateFont(PdfDocument document, string fontName, PdfDictionary descriptor)
    {
        var font = new PdfDictionary(document);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/TrueType");
        font.Elements.SetName("/BaseFont", fontName);
        font.Elements.SetReference("/FontDescriptor", descriptor);
        document.Internals.AddObject(font);
        return font;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class RecordingPdfCompressionEngine : IPdfCompressionEngine
    {
        public string InputPath { get; private set; } = "";
        public string OutputPath { get; private set; } = "";
        public PdfCompressionLevel Level { get; private set; }

        public void Compress(
            string inputPath,
            string outputPath,
            PdfCompressionLevel level,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            InputPath = inputPath;
            OutputPath = outputPath;
            Level = level;
        }
    }
}
