using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Pdf.Translation.Pipeline.TextProcessing;

    public static class TranslationPostProcessor
    {
        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            PostProcessor.Process(originalText, translatedText, targetLang);
    }
