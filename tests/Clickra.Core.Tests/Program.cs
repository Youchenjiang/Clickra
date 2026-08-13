using Clickra.Core.Tests;

var runner = new TestRunner();

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

// Print an explicit summary so CI logs show the actual executed test count
// instead of only a build-success signal.
Console.WriteLine($"SUMMARY: {runner.Passed} passed, {runner.Failures} failed");
return runner.Failures == 0 ? 0 : 1;
