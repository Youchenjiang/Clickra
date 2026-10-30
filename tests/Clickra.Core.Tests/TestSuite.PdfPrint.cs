using Clickra.Core.Modules.PdfPrint.Application;
using Clickra.Core.Modules.PdfPrint.Ports;
using Clickra.Core.Modules.PdfPrint.Processing;
using Clickra.Core.Application;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private sealed class MockPrinterDiscoveryService : IPrinterDiscoveryService
    {
        private readonly List<string> _printers = new();
        private string? _defaultPrinter;
        private readonly List<string> _paperSizes = new() { "A4", "Letter" };
        private readonly bool _canDuplex;

        public MockPrinterDiscoveryService(IEnumerable<string> printers, string? defaultPrinter = null, bool canDuplex = true)
        {
            _printers.AddRange(printers);
            _defaultPrinter = defaultPrinter ?? _printers.FirstOrDefault();
            _canDuplex = canDuplex;
        }

        public IReadOnlyList<string> GetInstalledPrinters() => _printers;
        public string? GetDefaultPrinter() => _defaultPrinter;
        public bool IsPrinterInstalled(string printerName) =>
            _printers.Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase));
        public IReadOnlyList<string> GetSupportedPaperSizes(string? printerName = null) => _paperSizes;
        public bool CanDuplex(string? printerName = null) => _canDuplex;
    }

    public static void RegisterPdfPrintTests(TestRunner runner)
    {
        runner.Run("PDF print: resolve page numbers parses ranges and keywords correctly", () =>
        {
            var pagesAll = PdfPrintProcessor.ResolvePageNumbers("all", 10);
            Assert.Equal(10, pagesAll.Count);
            Assert.Equal(1, pagesAll[0]);
            Assert.Equal(10, pagesAll[9]);

            var pagesEmpty = PdfPrintProcessor.ResolvePageNumbers(null, 5);
            Assert.Equal(5, pagesEmpty.Count);

            var pagesRange = PdfPrintProcessor.ResolvePageNumbers("1-3, 5, 8-10", 10);
            Assert.Equal(7, pagesRange.Count);
            Assert.True(pagesRange.SequenceEqual(new[] { 1, 2, 3, 5, 8, 9, 10 }), "Pages range mismatch.");

            var pagesClamped = PdfPrintProcessor.ResolvePageNumbers("0-2, 9-15", 10);
            Assert.True(pagesClamped.SequenceEqual(new[] { 1, 2, 9, 10 }), "Pages clamped mismatch.");
        });

        runner.Run("PDF print: use case validates installed printers and rejects invalid ones", () =>
        {
            var mockDiscovery = new MockPrinterDiscoveryService(new[] { "HP OfficeJet", "PDF Writer" }, "HP OfficeJet");
            var useCase = new PrintPdfUseCase(mockDiscovery);

            var reqValid = new ConversionRequest(
                PrintPdfUseCase.CommandName,
                new[] { "test.pdf" },
                Options: new Dictionary<string, object> { [PrintPdfOptions.PrinterNameKey] = "HP OfficeJet" });
            var resValid = useCase.Validate(reqValid);
            Assert.True(resValid.IsValid, "Expected valid printer to pass validation.");

            var reqInvalid = new ConversionRequest(
                PrintPdfUseCase.CommandName,
                new[] { "test.pdf" },
                Options: new Dictionary<string, object> { [PrintPdfOptions.PrinterNameKey] = "NonExistentPrinter" });
            var resInvalid = useCase.Validate(reqInvalid);
            Assert.False(resInvalid.IsValid, "Expected uninstalled printer to fail validation.");
        });

        runner.Run("PDF print: use case populates default printer when unspecified", () =>
        {
            var mockDiscovery = new MockPrinterDiscoveryService(new[] { "Default Laser", "Secondary" }, "Default Laser");
            var useCase = new PrintPdfUseCase(mockDiscovery);

            var plan = useCase.Plan(new ConversionRequest(PrintPdfUseCase.CommandName, new[] { "doc.pdf" }));
            Assert.True(plan.NormalizedOptions.ContainsKey(PrintPdfOptions.PrinterNameKey), "Default printer must be present.");
            Assert.Equal("Default Laser", plan.NormalizedOptions[PrintPdfOptions.PrinterNameKey]?.ToString() ?? "");
        });

        runner.Run("PDF contrast enhancer: blue pen boost darkens blue ink and bleaches background", () =>
        {
            // Verify blue pen luminance boost
            int normalLum = Clickra.Core.Rendering.PdfContrastEnhancer.CalculateLuminance(150, 150, 150);
            Assert.Equal(150, normalLum);

            // Blue ink (R=40, G=40, B=180): b > r + 10 and b > 100 -> blue boost active
            int blueLum = Clickra.Core.Rendering.PdfContrastEnhancer.CalculateLuminance(40, 40, 180);
            float expectedWithoutBoost = (0.299f * 40) + (0.587f * 40) + (0.114f * 180);
            Assert.True(blueLum < expectedWithoutBoost, "Blue boost must darken perceived luminance for ballpoint pen ink.");

            // Create 50x50 test image: 90% near-white background (240), 10% faint pencil/ink (140)
            using var bmp = new System.Drawing.Bitmap(50, 50, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(240, 240, 240));
            }
            // Draw faint ink on 10% of pixels
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 50; x++)
                {
                    bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(140, 140, 140));
                }
            }

            Clickra.Core.Rendering.PdfContrastEnhancer.Enhance(bmp, gamma: 2.2);

            // Background at bottom (x=25, y=25) must be bleached to white (255)
            var bgPixel = bmp.GetPixel(25, 25);
            Assert.Equal(255, bgPixel.R);
            Assert.Equal(255, bgPixel.G);
            Assert.Equal(255, bgPixel.B);

            // Faint ink at top (x=25, y=2) must be darkened below its original 140
            var textPixel = bmp.GetPixel(25, 2);
            Assert.True(textPixel.R < 100, $"Text ink was not darkened enough: actual {textPixel.R}");
        });

        runner.Run("PDF print: options parse and serialize duplex and paper size correctly", () =>
        {
            var dict = new Dictionary<string, object>
            {
                [PrintPdfOptions.DuplexKey] = "long",
                [PrintPdfOptions.PaperSizeKey] = "A4"
            };
            var opts = PrintPdfOptions.FromDictionary(dict);
            Assert.True(opts.Duplex == PrintDuplexMode.DuplexLongEdge, "Expected DuplexLongEdge");
            Assert.Equal("A4", opts.PaperSize ?? "");

            var dictOut = opts.ToDictionary();
            Assert.Equal("DuplexLongEdge", dictOut[PrintPdfOptions.DuplexKey]?.ToString() ?? "");
            Assert.Equal("A4", dictOut[PrintPdfOptions.PaperSizeKey]?.ToString() ?? "");

            var dictShort = new Dictionary<string, object>
            {
                [PrintPdfOptions.DuplexKey] = "short"
            };
            var optsShort = PrintPdfOptions.FromDictionary(dictShort);
            Assert.True(optsShort.Duplex == PrintDuplexMode.DuplexShortEdge, "Expected DuplexShortEdge");

            var dictSimplex = new Dictionary<string, object>
            {
                [PrintPdfOptions.DuplexKey] = "simplex"
            };
            var optsSimplex = PrintPdfOptions.FromDictionary(dictSimplex);
            Assert.True(optsSimplex.Duplex == PrintDuplexMode.Simplex, "Expected Simplex");
        });
    }
}
