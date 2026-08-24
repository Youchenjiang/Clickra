using Clickra.Core.Tests;

// 隔離 ClickraStorage 資料目錄：整個測試套件使用暫存目錄，不讀寫真實使用者
// 資料（%LOCALAPPDATA%\Clickra）。必須在任何 ClickraStorage 靜態初始化之前設定。
string testDataDir = Path.Combine(Path.GetTempPath(), $"clickra-test-data-{Guid.NewGuid():N}");
Directory.CreateDirectory(testDataDir);
Environment.SetEnvironmentVariable("CLICKRA_DATA_DIR", testDataDir);

// Mirrors tests/PdfRegression/run_translation_tests.py --require-fixtures:
// when set, fixture-dependent tests that cannot run are reported as failures
// instead of skips, so a regression gate that expects fixtures fails loudly
// when they are missing rather than quietly passing.
bool requireFixtures = args.Contains("--require-fixtures", StringComparer.Ordinal);
var runner = new TestRunner(requireFixtures);

TestSuite.RegisterPentestGrayPromptTests(runner);
TestSuite.RegisterFinalProjectTests(runner);
TestSuite.RegisterTogllLayoutTests(runner);
TestSuite.RegisterFigureRegressionTests(runner);
TestSuite.RegisterPdfLayoutRegressionTests(runner);
TestSuite.RegisterTranslationTests(runner);
TestSuite.RegisterLibreOfficeEngineTests(runner);
TestSuite.RegisterPdfCompressionTests(runner);
TestSuite.RegisterPdfSplitTests(runner);
TestSuite.RegisterPdfDecryptTests(runner);
TestSuite.RegisterTaskQueueTests(runner);

// Print an explicit summary so CI logs show the actual executed test count
// instead of only a build-success signal. Skipped counts fixture-dependent
// tests whose git-ignored test_pdfs/ fixtures are absent (fresh CI checkout).
Console.WriteLine($"SUMMARY: {runner.Passed} passed, {runner.Failures} failed, {runner.Skipped} skipped");

try
{
    if (Directory.Exists(testDataDir))
    {
        Directory.Delete(testDataDir, recursive: true);
    }
}
catch
{
    // Best-effort cleanup of the isolated data dir.
}

return runner.Failures == 0 ? 0 : 1;
