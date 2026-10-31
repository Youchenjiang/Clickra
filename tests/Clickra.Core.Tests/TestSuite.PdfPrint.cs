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
        private readonly string? _defaultPrinter;
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

        runner.Run("PDF print: paper size registry resolves known and fallback dimensions correctly", () =>
        {
            var a4 = PaperSizeRegistry.GetDimensions("A4");
            Assert.Equal("A4", a4.DisplayName);
            Assert.True(Math.Abs(a4.WidthMm - 210f) < 0.1f, "A4 width mismatch");
            Assert.True(Math.Abs(a4.HeightMm - 297f) < 0.1f, "A4 height mismatch");
            Assert.True(Math.Abs(a4.AspectRatio - (210f / 297f)) < 0.01f, "A4 aspect ratio mismatch");

            var a3 = PaperSizeRegistry.GetDimensions("A3");
            Assert.Equal("A3", a3.DisplayName);
            Assert.True(Math.Abs(a3.WidthMm - 297f) < 0.1f, "A3 width mismatch");
            Assert.True(Math.Abs(a3.HeightMm - 420f) < 0.1f, "A3 height mismatch");
            Assert.True(a3.WidthPt > a4.WidthPt, "A3 width must be larger than A4 width");
            Assert.True(a3.HeightPt > a4.HeightPt, "A3 height must be larger than A4 height");

            var letter = PaperSizeRegistry.GetDimensions("US Letter");
            Assert.Equal("Letter", letter.DisplayName);
            Assert.True(Math.Abs(letter.WidthMm - 215.9f) < 0.1f, "Letter width mismatch");
            Assert.True(Math.Abs(letter.HeightMm - 279.4f) < 0.1f, "Letter height mismatch");

            var fallback = PaperSizeRegistry.GetDimensions(null);
            Assert.Equal("A4", fallback.DisplayName);

            var unknown = PaperSizeRegistry.GetDimensions("Custom Roll Paper");
            Assert.Equal("A4", unknown.DisplayName);
        });

        runner.Run("PDF print: options parse and serialize orientation correctly", () =>
        {
            var dictAuto = new Dictionary<string, object>
            {
                [PrintPdfOptions.OrientationKey] = "auto"
            };
            var optsAuto = PrintPdfOptions.FromDictionary(dictAuto);
            Assert.True(optsAuto.Orientation == PrintOrientation.Auto, "Expected Auto");

            var dictPortrait = new Dictionary<string, object>
            {
                [PrintPdfOptions.OrientationKey] = "portrait"
            };
            var optsPortrait = PrintPdfOptions.FromDictionary(dictPortrait);
            Assert.True(optsPortrait.Orientation == PrintOrientation.Portrait, "Expected Portrait");
            Assert.Equal("Portrait", optsPortrait.ToDictionary()[PrintPdfOptions.OrientationKey]?.ToString() ?? "");

            var dictLandscape = new Dictionary<string, object>
            {
                [PrintPdfOptions.OrientationKey] = "l"
            };
            var optsLandscape = PrintPdfOptions.FromDictionary(dictLandscape);
            Assert.True(optsLandscape.Orientation == PrintOrientation.Landscape, "Expected Landscape");
            Assert.Equal("Landscape", optsLandscape.ToDictionary()[PrintPdfOptions.OrientationKey]?.ToString() ?? "");

            var dictEnum = new Dictionary<string, object>
            {
                [PrintPdfOptions.OrientationKey] = PrintOrientation.Landscape
            };
            var optsEnum = PrintPdfOptions.FromDictionary(dictEnum);
            Assert.True(optsEnum.Orientation == PrintOrientation.Landscape, "Expected Landscape from enum");

            Assert.True(PrintPdfOptions.ParseOrientation(null) == PrintOrientation.Auto, "Null must resolve to Auto");
            Assert.True(PrintPdfOptions.ParseOrientation("unknown") == PrintOrientation.Auto, "Unknown must resolve to Auto");
        });

        runner.Run("PDF print: processor correctly detects per-page landscape orientation", () =>
        {
            string tempPdf = Path.Combine(Path.GetTempPath(), $"clickra_orient_test_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharp.Pdf.PdfDocument())
                {
                    var p1 = doc.AddPage();
                    p1.Width = PdfSharp.Drawing.XUnit.FromPoint(800);
                    p1.Height = PdfSharp.Drawing.XUnit.FromPoint(600); // Landscape

                    var p2 = doc.AddPage();
                    p2.Width = PdfSharp.Drawing.XUnit.FromPoint(600);
                    p2.Height = PdfSharp.Drawing.XUnit.FromPoint(800); // Portrait

                    doc.Save(tempPdf);
                }

                var orientations = PdfPrintProcessor.ReadPageOrientations(tempPdf, new[] { 1, 2 });
                Assert.True(orientations[1], "Page 1 should be detected as landscape");
                Assert.False(orientations[2], "Page 2 should be detected as portrait");
            }
            finally
            {
                if (File.Exists(tempPdf)) File.Delete(tempPdf);
            }
        });
    }
}
