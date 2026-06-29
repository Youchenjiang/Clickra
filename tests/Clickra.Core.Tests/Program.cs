var runner = new TestRunner();

TestSuite.RegisterPentestGrayPromptTests(runner);
TestSuite.RegisterFinalProjectTests(runner);
TestSuite.RegisterTogllLayoutTests(runner);
TestSuite.RegisterFigureRegressionTests(runner);
TestSuite.RegisterPdfLayoutRegressionTests(runner);
TestSuite.RegisterTranslationTests(runner);
TestSuite.RegisterLibreOfficeEngineTests(runner);

return runner.Failures == 0 ? 0 : 1;
