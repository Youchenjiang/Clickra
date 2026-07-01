using Clickra.Core.Processors;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

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
