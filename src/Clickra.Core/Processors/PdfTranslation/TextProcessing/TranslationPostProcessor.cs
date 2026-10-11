namespace Clickra.Core.Processors.PdfTranslation.TextProcessing;

    public static class TranslationPostProcessor
    {
        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            PostProcessor.Process(originalText, translatedText, targetLang);
    }
