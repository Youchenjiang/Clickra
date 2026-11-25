using Clickra.Core.Modules.Pdf.Print.Application;
using Clickra.Core.Modules.Pdf.Print.Ports;
using Clickra.Core.Modules.Pdf.Print.Processing;
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

    private const string ScaleActual = "actual";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "Registers independent PDF print test cases.")]
    public static void RegisterPdfPrintTests(TestRunner runner)
    {
        const string defaultLaserPrinter = "Default Laser";
        RegisterLayoutPlannerTests(runner, defaultLaserPrinter);
        RegisterPreviewAndSettingsTests(runner, defaultLaserPrinter);
        RegisterProcessorAndOrientationTests(runner);
    }

    private static void RegisterLayoutPlannerTests(TestRunner runner, string defaultLaserPrinter)
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

        runner.Run("PDF print: layout planner: A4 portrait page, 1 page per sheet, auto orientation", () =>
        {
            var pages = new[] { new PrintPageSize(1, 595.28f, 841.89f) };
            var sheets = PrintLayoutPlanner.Plan(pages, 595.28f, 841.89f, new PrintPdfOptions(orientation: PrintOrientation.Auto, pagesPerSheet: 1));
            Assert.Equal(1, sheets.Count);
            Assert.False(sheets[0].Landscape, "A4 portrait page should plan as portrait on A4 paper.");
            Assert.False(sheets[0].IsBack, "First sheet must not be back.");
            Assert.Equal(1, sheets[0].Cells.Count);
            var cell = sheets[0].Cells[0];
            Assert.Equal(1, cell.PageNumber);
            Assert.False(cell.Overflows, "Cell must not overflow.");
            float margin = 595.28f * 0.03f;
            Assert.True(Math.Abs(cell.Cell.X - margin) < 0.1f, "Cell X mismatch.");
            Assert.True(Math.Abs(cell.Cell.Y - margin) < 0.1f, "Cell Y mismatch.");
            Assert.True(Math.Abs(cell.Cell.Width - (595.28f - 2 * margin)) < 0.1f, "Cell Width mismatch.");
            Assert.True(Math.Abs(cell.Cell.Height - (841.89f - 2 * margin)) < 0.1f, "Cell Height mismatch.");
        });

        runner.Run("PDF print: layout planner: 2 pages per sheet chooses orientation that maximizes size", () =>
        {
            // Portrait pages -> landscape paper (2x1 grid)
            var portPages = new[] { new PrintPageSize(1, 595.28f, 841.89f), new PrintPageSize(2, 595.28f, 841.89f) };
            var sheetsLand = PrintLayoutPlanner.Plan(portPages, 595.28f, 841.89f, new PrintPdfOptions(orientation: PrintOrientation.Auto, pagesPerSheet: 2));
            Assert.Equal(1, sheetsLand.Count);
            Assert.True(sheetsLand[0].Landscape, "Portrait pages with 2 per sheet should pick landscape paper.");
            Assert.Equal(2, sheetsLand[0].Cells.Count);
            Assert.True(sheetsLand[0].Cells[1].Cell.X > sheetsLand[0].Cells[0].Cell.X, "Cells must be side-by-side.");
            Assert.True(Math.Abs(sheetsLand[0].Cells[0].Cell.Y - sheetsLand[0].Cells[1].Cell.Y) < 0.1f, "Cells must have same Y.");

            // Landscape pages -> portrait paper (1x2 grid)
            var landPages = new[] { new PrintPageSize(1, 841.89f, 595.28f), new PrintPageSize(2, 841.89f, 595.28f) };
            var sheetsPort = PrintLayoutPlanner.Plan(landPages, 595.28f, 841.89f, new PrintPdfOptions(orientation: PrintOrientation.Auto, pagesPerSheet: 2));
            Assert.Equal(1, sheetsPort.Count);
            Assert.False(sheetsPort[0].Landscape, "Landscape pages with 2 per sheet should pick portrait paper.");
            Assert.Equal(2, sheetsPort[0].Cells.Count);
            Assert.True(sheetsPort[0].Cells[1].Cell.Y > sheetsPort[0].Cells[0].Cell.Y, "Cells must be stacked.");
            Assert.True(Math.Abs(sheetsPort[0].Cells[0].Cell.X - sheetsPort[0].Cells[1].Cell.X) < 0.1f, "Cells must have same X.");
        });

        runner.Run("PDF print: layout planner: 4 pages per sheet page order Z and N", () =>
        {
            var pages = new[]
            {
                new PrintPageSize(1, 595f, 842f),
                new PrintPageSize(2, 595f, 842f),
                new PrintPageSize(3, 595f, 842f),
                new PrintPageSize(4, 595f, 842f)
            };

            // Order Z (across first): row 0 has pages 1, 2; row 1 has pages 3, 4
            var sheetsZ = PrintLayoutPlanner.Plan(pages, 595f, 842f, new PrintPdfOptions(orientation: PrintOrientation.Portrait, pagesPerSheet: 4, pageOrder: "Z"));
            var cellsZ = sheetsZ[0].Cells;
            Assert.True(cellsZ[0].Cell.X < cellsZ[1].Cell.X && Math.Abs(cellsZ[0].Cell.Y - cellsZ[1].Cell.Y) < 0.1f, "Order Z row 0 mismatch.");
            Assert.True(cellsZ[2].Cell.X < cellsZ[3].Cell.X && Math.Abs(cellsZ[2].Cell.Y - cellsZ[3].Cell.Y) < 0.1f, "Order Z row 1 mismatch.");
            Assert.True(cellsZ[2].Cell.Y > cellsZ[0].Cell.Y, "Order Z Y mismatch.");

            // Order N (down first): col 0 has pages 1, 2; col 1 has pages 3, 4
            var sheetsN = PrintLayoutPlanner.Plan(pages, 595f, 842f, new PrintPdfOptions(orientation: PrintOrientation.Portrait, pagesPerSheet: 4, pageOrder: "N"));
            var cellsN = sheetsN[0].Cells;
            Assert.True(cellsN[0].Cell.Y < cellsN[1].Cell.Y && Math.Abs(cellsN[0].Cell.X - cellsN[1].Cell.X) < 0.1f, "Order N col 0 mismatch.");
            Assert.True(cellsN[2].Cell.Y < cellsN[3].Cell.Y && Math.Abs(cellsN[2].Cell.X - cellsN[3].Cell.X) < 0.1f, "Order N col 1 mismatch.");
            Assert.True(cellsN[2].Cell.X > cellsN[0].Cell.X, "Order N X mismatch.");
        });

        runner.Run("PDF print: layout planner: 13 pages at 4 per sheet produces 4 sheets and calculates paper count", () =>
        {
            var pages = Enumerable.Range(1, 13).Select(i => new PrintPageSize(i, 595f, 842f)).ToList();
            var sheets = PrintLayoutPlanner.Plan(pages, 595f, 842f, new PrintPdfOptions(pagesPerSheet: 4));
            Assert.Equal(4, sheets.Count);
            Assert.False(sheets[0].IsBack, "Sheet 0 is front.");
            Assert.True(sheets[1].IsBack, "Sheet 1 is back.");
            Assert.False(sheets[2].IsBack, "Sheet 2 is front.");
            Assert.True(sheets[3].IsBack, "Sheet 3 is back.");
            Assert.Equal(4, sheets[0].Cells.Count);
            Assert.Equal(4, sheets[1].Cells.Count);
            Assert.Equal(4, sheets[2].Cells.Count);
            Assert.Equal(1, sheets[3].Cells.Count);

            Assert.Equal(4, PrintLayoutPlanner.PaperCount(sheets, PrintDuplexMode.Simplex));
            Assert.Equal(2, PrintLayoutPlanner.PaperCount(sheets, PrintDuplexMode.DuplexLongEdge));
            Assert.Equal(2, PrintLayoutPlanner.PaperCount(sheets, PrintDuplexMode.DuplexShortEdge));
        });

        runner.Run("PDF print: layout planner: actual size overflows for oversized page and custom scale halves dimensions", () =>
        {
            var a3Pages = new[] { new PrintPageSize(1, 842f, 1191f) };
            var actualSheets = PrintLayoutPlanner.Plan(a3Pages, 595f, 842f, new PrintPdfOptions(orientation: PrintOrientation.Portrait, scale: ScaleActual, pagesPerSheet: 1));
            Assert.True(actualSheets[0].Cells[0].Overflows, "A3 on A4 actual size must overflow.");

            var customSheets = PrintLayoutPlanner.Plan(a3Pages, 595f, 842f, new PrintPdfOptions(orientation: PrintOrientation.Portrait, scale: "custom", scalePercent: 50, pagesPerSheet: 1));
            Assert.False(customSheets[0].Cells[0].Overflows, "50% scaled A3 on A4 must not overflow.");
            Assert.True(Math.Abs(customSheets[0].Cells[0].Page.Width - 421f) < 0.1f, "Width must be 421pt.");
            Assert.True(Math.Abs(customSheets[0].Cells[0].Page.Height - 595.5f) < 0.1f, "Height must be 595.5pt.");
        });

        runner.Run("PDF print: layout planner: 2 or more pages per sheet treats scale mode as fit", () =>
        {
            var pages = new[] { new PrintPageSize(1, 842f, 1191f), new PrintPageSize(2, 842f, 1191f) };
            var sheets = PrintLayoutPlanner.Plan(pages, 595f, 842f, new PrintPdfOptions(scale: ScaleActual, pagesPerSheet: 2));
            Assert.False(sheets[0].Cells[0].Overflows, "Scale mode must be treated as fit when pages per sheet > 1.");
            Assert.False(sheets[0].Cells[1].Overflows, "Cell 1 must not overflow.");
            Assert.True(sheets[0].Cells[0].Page.Width <= sheets[0].Cells[0].Cell.Width + 0.01f, "Width within cell.");
            Assert.True(sheets[0].Cells[0].Page.Height <= sheets[0].Cells[0].Cell.Height + 0.01f, "Height within cell.");
        });

        runner.Run("PDF print: options compatibility and explicit dictionary serialization", () =>
        {
            // fit_to_page=false and no scale -> actual
            var dictLegacy = new Dictionary<string, object> { [PrintPdfOptions.FitToPageKey] = false };
            var optsLegacy = PrintPdfOptions.FromDictionary(dictLegacy);
            Assert.Equal(ScaleActual, optsLegacy.Scale);
            Assert.False(optsLegacy.FitToPage, "Legacy fit to page should be false.");

            // ToExplicitDictionary writes every new field
            var explicitDict = new PrintPdfOptions().ToExplicitDictionary();
            Assert.True(explicitDict.ContainsKey(PrintPdfOptions.PagesPerSheetKey), "PagesPerSheetKey missing.");
            Assert.True(explicitDict.ContainsKey(PrintPdfOptions.PageOrderKey), "PageOrderKey missing.");
            Assert.True(explicitDict.ContainsKey(PrintPdfOptions.PageBorderKey), "PageBorderKey missing.");
            Assert.True(explicitDict.ContainsKey(PrintPdfOptions.ScaleKey), "ScaleKey missing.");
            Assert.True(explicitDict.ContainsKey(PrintPdfOptions.ScalePercentKey), "ScalePercentKey missing.");
            Assert.True(explicitDict.ContainsKey(PrintPdfOptions.GrayscaleKey), "GrayscaleKey missing.");
            Assert.True(explicitDict.ContainsKey(PrintPdfOptions.CollateKey), "CollateKey missing.");

            // NormalizeOptions preserves explicit choices even if they equal defaults
            string origScale = SettingsController.PrintScale;
            int origPps = SettingsController.PrintPagesPerSheet;
            string origOrder = SettingsController.PrintPageOrder;
            try
            {
                SettingsController.PrintScale = "actual";
                SettingsController.PrintPagesPerSheet = 4;
                SettingsController.PrintPageOrder = "N";

                var explicitChoices = new Dictionary<string, object>
                {
                    [PrintPdfOptions.ScaleKey] = "fit",
                    [PrintPdfOptions.PagesPerSheetKey] = 2,
                    [PrintPdfOptions.PageOrderKey] = "Z"
                };
                var mockDiscovery = new MockPrinterDiscoveryService(new[] { defaultLaserPrinter }, defaultLaserPrinter);
                var useCase = new PrintPdfUseCase(mockDiscovery);
                var req = new ConversionRequest(PrintPdfUseCase.CommandName, new[] { "test.pdf" }, explicitChoices);
                var plan = useCase.Plan(req);
                var planned = PrintPdfOptions.FromDictionary(plan.NormalizedOptions);
                Assert.Equal("fit", planned.Scale);
                Assert.Equal(2, planned.PagesPerSheet);
                Assert.Equal("Z", planned.PageOrder);
            }
            finally
            {
                SettingsController.PrintScale = origScale;
                SettingsController.PrintPagesPerSheet = origPps;
                SettingsController.PrintPageOrder = origOrder;
            }
        });

        runner.Run("PDF print: layout planner: page range filter across multiple files", () =>
        {
            string range = "3-5";
            var file1Pages = PrintLayoutPlanner.ResolvePageNumbers(range, 1);
            var file2Pages = PrintLayoutPlanner.ResolvePageNumbers(range, 12);
            var file3Pages = PrintLayoutPlanner.ResolvePageNumbers(range, 2);

            Assert.Equal(0, file1Pages.Count);
            Assert.Equal(3, file2Pages.Count);
            Assert.True(file2Pages.SequenceEqual(new[] { 3, 4, 5 }), "File 2 pages mismatch.");
            Assert.Equal(0, file3Pages.Count);
        });
    }

    private static void RegisterPreviewAndSettingsTests(TestRunner runner, string defaultLaserPrinter)
    {
        runner.Run("PDF print preview: PrintPreviewQuery resolves page sizes and total count", () =>
        {
            string tempPdf = CreateTwoPagePdf((400, 600), (500, 700));
            try
            {
                var (pages, total) = PrintPreviewQuery.ReadPageSizes(tempPdf, "2");
                Assert.Equal(2, total);
                Assert.Equal(1, pages.Count);
                Assert.Equal(2, pages[0].PageNumber);
                Assert.True(Math.Abs(pages[0].WidthPt - 500f) < 1f, "Width mismatch.");
                Assert.True(Math.Abs(pages[0].HeightPt - 700f) < 1f, "Height mismatch.");
            }
            finally
            {
                if (File.Exists(tempPdf)) File.Delete(tempPdf);
            }
        });

        runner.Run("PDF print preview: RenderPage produces bitmap and applies grayscale enhancement", () =>
        {
            string tempPdf = Path.Combine(Path.GetTempPath(), $"clickra_render_query_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharp.Pdf.PdfDocument())
                {
                    var p1 = doc.AddPage();
                    p1.Width = PdfSharp.Drawing.XUnit.FromPoint(300);
                    p1.Height = PdfSharp.Drawing.XUnit.FromPoint(300);
                    doc.Save(tempPdf);
                }

                using var bmp = PrintPreviewQuery.RenderPage(tempPdf, 1, 100, darken: true, grayscale: true)
                    ?? throw new InvalidOperationException("RenderPage returned null bitmap.");
                Assert.True(bmp.Width > 0 && bmp.Height > 0, "Rendered bitmap must have positive dimensions.");
            }
            finally
            {
                if (File.Exists(tempPdf)) File.Delete(tempPdf);
            }
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

        runner.Run("PDF print: choices passed explicitly are not replaced by the saved defaults", () =>
        {
            string origOrientation = SettingsController.PrintOrientation;
            string origDuplex = SettingsController.PrintDuplex;
            bool origDarken = SettingsController.PrintDarkenText;
            try
            {
                // Set saved defaults to conflicting values (Portrait, DuplexVertical, Darken on)
                SettingsController.PrintOrientation = "Portrait";
                SettingsController.PrintDuplex = "DuplexVertical";
                SettingsController.PrintDarkenText = true;

                // The classic print dialog shows Auto / single-sided / no darkening; whatever the saved
                // defaults say, those on-screen choices must be what gets printed.
                var shown = PrintPdfOptions.FromDictionary(new Dictionary<string, object>());
                var dict = shown.ToExplicitDictionary();
                Assert.True(dict.ContainsKey(PrintPdfOptions.OrientationKey), "Orientation must be written even when it is Auto.");
                Assert.True(dict.ContainsKey(PrintPdfOptions.DuplexKey), "Duplex must be written even when it is single-sided.");
                Assert.True(dict.ContainsKey(PrintPdfOptions.DarkenTextKey), "Darken-text must be written even when it is off.");

                var mockDiscovery = new MockPrinterDiscoveryService(new[] { defaultLaserPrinter }, defaultLaserPrinter);
                var plan = new PrintPdfUseCase(mockDiscovery).Plan(new ConversionRequest(PrintPdfUseCase.CommandName, new[] { "doc.pdf" }, dict));
                var planned = PrintPdfOptions.FromDictionary(plan.NormalizedOptions);
                Assert.True(planned.Orientation == PrintOrientation.Auto, $"Expected Auto, got {planned.Orientation}.");
                Assert.True(planned.Duplex == PrintDuplexMode.Simplex, $"Expected Simplex, got {planned.Duplex}.");
                Assert.False(planned.DarkenText, "Expected darken-text to stay off.");
            }
            finally
            {
                SettingsController.PrintOrientation = origOrientation;
                SettingsController.PrintDuplex = origDuplex;
                SettingsController.PrintDarkenText = origDarken;
            }
        });

        runner.Run("PDF print: use case populates default printer when unspecified", () =>
        {
            var mockDiscovery = new MockPrinterDiscoveryService(new[] { defaultLaserPrinter, "Secondary" }, defaultLaserPrinter);
            var useCase = new PrintPdfUseCase(mockDiscovery);

            var plan = useCase.Plan(new ConversionRequest(PrintPdfUseCase.CommandName, new[] { "doc.pdf" }));
            Assert.True(plan.NormalizedOptions.ContainsKey(PrintPdfOptions.PrinterNameKey), "Default printer must be present.");
            Assert.Equal(defaultLaserPrinter, plan.NormalizedOptions[PrintPdfOptions.PrinterNameKey]?.ToString() ?? "");
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
    }

    private static void RegisterProcessorAndOrientationTests(TestRunner runner)
    {
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
            string tempPdf = CreateTwoPagePdf((800, 600), (600, 800));
            try
            {
                var orientations = PdfPrintProcessor.ReadPageOrientations(tempPdf, new[] { 1, 2 });
                Assert.True(orientations[1], "Page 1 should be detected as landscape");
                Assert.False(orientations[2], "Page 2 should be detected as portrait");
            }
            finally
            {
                if (File.Exists(tempPdf)) File.Delete(tempPdf);
            }
        });

        runner.Run("PDF print: layout planner multi-file sheet counts and paper count rules", () =>
        {
            // Plan section 7 interface tests:
            // 3 files (1 landscape, 12 portrait, 2 portrait)
            var file1Pages = new List<PrintPageSize> { new(1, 841.89f, 595.28f) }; // 1 landscape page
            var file2Pages = Enumerable.Range(1, 12).Select(i => new PrintPageSize(i, 595.28f, 841.89f)).ToList(); // 12 portrait pages
            var file3Pages = Enumerable.Range(1, 2).Select(i => new PrintPageSize(i, 595.28f, 841.89f)).ToList(); // 2 portrait pages

            var simplexOpts = new PrintPdfOptions(
                printerName: null,
                pageRange: null,
                copies: 1,
                dpi: 300,
                fitToPage: true,
                language: "zh-TW",
                darkenText: false,
                darkenGamma: 2.2,
                duplex: PrintDuplexMode.Simplex,
                paperSize: "A4",
                orientation: PrintOrientation.Auto,
                pagesPerSheet: 1,
                pageOrder: "Z",
                pageBorder: false,
                scale: "fit",
                scalePercent: 100,
                grayscale: false,
                collate: true);

            float portW = 595.28f;
            float portH = 841.89f;

            var sheets1 = PrintLayoutPlanner.Plan(file1Pages, portW, portH, simplexOpts);
            var sheets2 = PrintLayoutPlanner.Plan(file2Pages, portW, portH, simplexOpts);
            var sheets3 = PrintLayoutPlanner.Plan(file3Pages, portW, portH, simplexOpts);

            Assert.Equal(1, sheets1.Count);
            Assert.Equal(12, sheets2.Count);
            Assert.Equal(2, sheets3.Count);

            int simplexTotalPaper = PrintLayoutPlanner.PaperCount(sheets1, PrintDuplexMode.Simplex)
                + PrintLayoutPlanner.PaperCount(sheets2, PrintDuplexMode.Simplex)
                + PrintLayoutPlanner.PaperCount(sheets3, PrintDuplexMode.Simplex);
            Assert.Equal(15, simplexTotalPaper);

            // Same 3 files with duplex (long edge)
            var duplexOpts = simplexOpts with { Duplex = PrintDuplexMode.DuplexLongEdge };
            var duplexSheets1 = PrintLayoutPlanner.Plan(file1Pages, portW, portH, duplexOpts);
            var duplexSheets2 = PrintLayoutPlanner.Plan(file2Pages, portW, portH, duplexOpts);
            var duplexSheets3 = PrintLayoutPlanner.Plan(file3Pages, portW, portH, duplexOpts);

            int duplexPaper1 = PrintLayoutPlanner.PaperCount(duplexSheets1, PrintDuplexMode.DuplexLongEdge);
            int duplexPaper2 = PrintLayoutPlanner.PaperCount(duplexSheets2, PrintDuplexMode.DuplexLongEdge);
            int duplexPaper3 = PrintLayoutPlanner.PaperCount(duplexSheets3, PrintDuplexMode.DuplexLongEdge);

            Assert.Equal(1, duplexPaper1);
            Assert.Equal(6, duplexPaper2);
            Assert.Equal(1, duplexPaper3);
            Assert.Equal(8, duplexPaper1 + duplexPaper2 + duplexPaper3);

            // Check front/back parity on file 2 sheet 1: s=0 is front, paperNo=1, paperCount=6
            Assert.False(duplexSheets2[0].IsBack, "Sheet 1 front face must not be marked as back");
            Assert.True(duplexSheets2[1].IsBack, "Sheet 1 back face must be marked as back");

            // Page range "3-5"
            var rangeOpts = simplexOpts with { PageRange = "3-5" };
            var rangePages1 = PrintLayoutPlanner.ResolvePageNumbers("3-5", 1);
            var rangePages2 = PrintLayoutPlanner.ResolvePageNumbers("3-5", 12);
            var rangePages3 = PrintLayoutPlanner.ResolvePageNumbers("3-5", 2);

            Assert.Equal(0, rangePages1.Count);
            Assert.Equal(3, rangePages2.Count); // 3, 4, 5
            Assert.Equal(0, rangePages3.Count);

            var rangeSheets2 = PrintLayoutPlanner.Plan(
                rangePages2.Select(p => new PrintPageSize(p, 595.28f, 841.89f)).ToList(),
                portW, portH, rangeOpts);
            Assert.Equal(3, rangeSheets2.Count);
            Assert.Equal(3, PrintLayoutPlanner.PaperCount(rangeSheets2, PrintDuplexMode.Simplex));

            // When printer cannot duplex, effective duplex is Simplex and duplex setting does not alter paper count
            var fallbackDuplex = ResolveEffectiveDuplex(canDuplex: false, PrintDuplexMode.DuplexLongEdge);
            int fallbackTotal = PrintLayoutPlanner.PaperCount(sheets1, fallbackDuplex)
                + PrintLayoutPlanner.PaperCount(sheets2, fallbackDuplex)
                + PrintLayoutPlanner.PaperCount(sheets3, fallbackDuplex);
            Assert.Equal(15, fallbackTotal);
        });

        runner.Run("PDF print: a portrait page rotated 90 degrees prints as landscape at its displayed size", () =>
        {
            // Forms exported by iText often store an A4 portrait MediaBox with /Rotate 90.
            string tempPdf = Path.Combine(Path.GetTempPath(), $"clickra_rotate_test_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharp.Pdf.PdfDocument())
                {
                    var page = doc.AddPage();
                    page.Width = PdfSharp.Drawing.XUnit.FromPoint(595);
                    page.Height = PdfSharp.Drawing.XUnit.FromPoint(842);
                    page.Elements.SetInteger("/Rotate", 90);
                    doc.Save(tempPdf);
                }

                var dims = PdfPrintProcessor.ReadPageDimensions(tempPdf, new[] { 1 });
                Assert.True(Math.Abs(dims[1].widthPt - 842) < 1 && Math.Abs(dims[1].heightPt - 595) < 1,
                    $"Expected the displayed size 842 x 595, got {dims[1].widthPt} x {dims[1].heightPt}.");
                Assert.True(PdfPrintProcessor.ReadPageOrientations(tempPdf, new[] { 1 })[1],
                    "A portrait page with /Rotate 90 is displayed, and must print, as landscape.");
            }
            finally
            {
                if (File.Exists(tempPdf)) File.Delete(tempPdf);
            }
        });
    }

    private static string CreateTwoPagePdf((float w, float h) page1, (float w, float h) page2)
    {
        string tempPdf = Path.Combine(Path.GetTempPath(), $"clickra_test_doc_{Guid.NewGuid():N}.pdf");
        using var doc = new PdfSharp.Pdf.PdfDocument();
        var p1 = doc.AddPage();
        p1.Width = PdfSharp.Drawing.XUnit.FromPoint(page1.w);
        p1.Height = PdfSharp.Drawing.XUnit.FromPoint(page1.h);

        var p2 = doc.AddPage();
        p2.Width = PdfSharp.Drawing.XUnit.FromPoint(page2.w);
        p2.Height = PdfSharp.Drawing.XUnit.FromPoint(page2.h);

        doc.Save(tempPdf);
        return tempPdf;
    }

    private static PrintDuplexMode ResolveEffectiveDuplex(bool canDuplex, PrintDuplexMode requested) =>
        canDuplex ? requested : PrintDuplexMode.Simplex;
}
