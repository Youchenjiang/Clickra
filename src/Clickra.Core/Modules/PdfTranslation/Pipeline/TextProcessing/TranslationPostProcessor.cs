using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline.TextProcessing;

    public static class TranslationPostProcessor
    {
        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            PostProcessor.Process(originalText, translatedText, targetLang);
    }
