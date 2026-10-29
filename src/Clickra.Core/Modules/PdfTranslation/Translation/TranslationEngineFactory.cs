using System;
using Clickra.Core.Modules.Settings.Application;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

public static class TranslationEngineFactory
{
    public const string GoogleEngine = "google";
    public const string GoogleFreeEngine = "google-free";
    public const string CustomAiEngine = "custom-ai";

    public static ITranslationEngine Create()
    {
        string? engine = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
        if (string.IsNullOrWhiteSpace(engine))
        {
            engine = SettingsController.TranslateEngine;
        }

        return Create(
            engine,
            SettingsController.TranslateAiEndpoint,
            SettingsController.TranslateAiApiKey,
            SettingsController.TranslateAiModel);
    }

    public static ITranslationEngine Create(string? engine, string? endpoint = null, string? apiKey = null, string? model = null)
    {
        if (string.Equals(engine, "identity", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engine, "passthrough", StringComparison.OrdinalIgnoreCase))
        {
            return new IdentityTranslator();
        }
        if (string.Equals(engine, "synthetic-cjk", StringComparison.OrdinalIgnoreCase))
        {
            return new SyntheticCjkTranslator();
        }
        if (string.Equals(engine, CustomAiEngine, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engine, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenAiTranslator(endpoint, apiKey, model);
        }

        return new GoogleFreeTranslator();
    }
}
