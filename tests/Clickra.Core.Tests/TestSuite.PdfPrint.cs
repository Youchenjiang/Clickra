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

        public MockPrinterDiscoveryService(IEnumerable<string> printers, string? defaultPrinter = null)
        {
            _printers.AddRange(printers);
            _defaultPrinter = defaultPrinter ?? _printers.FirstOrDefault();
        }

        public IReadOnlyList<string> GetInstalledPrinters() => _printers;
        public string? GetDefaultPrinter() => _defaultPrinter;
        public bool IsPrinterInstalled(string printerName) =>
            _printers.Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase));
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
    }
}
