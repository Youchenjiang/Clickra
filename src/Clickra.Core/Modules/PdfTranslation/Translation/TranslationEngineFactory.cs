using System;
using Clickra.Core.Modules.Settings.Application;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

public static class TranslationEngineFactory
{
    public static ITranslationEngine Create(string? enginePreference = null)
    {
        string? engine = enginePreference;
        if (string.IsNullOrWhiteSpace(engine))
        {
            engine = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
        }
        if (string.IsNullOrWhiteSpace(engine))
        {
            engine = SettingsController.TranslationEngine;
        }

        if (string.Equals(engine, "identity", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engine, "passthrough", StringComparison.OrdinalIgnoreCase))
        {
            return new IdentityTranslator();
        }
        if (string.Equals(engine, "synthetic-cjk", StringComparison.OrdinalIgnoreCase))
        {
            return new SyntheticCjkTranslator();
        }
        if (string.Equals(engine, SettingsContract.TranslationEngineOffline, StringComparison.OrdinalIgnoreCase))
        {
            return new OfflineTranslator();
        }
        if (string.Equals(engine, "google", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engine, "google-free", StringComparison.OrdinalIgnoreCase))
        {
            return new GoogleFreeTranslator();
        }
        if (string.Equals(engine, SettingsContract.TranslationEngineMyMemory, StringComparison.OrdinalIgnoreCase))
        {
            return new MyMemoryTranslator();
        }

        return new FallbackTranslator(new GoogleFreeTranslator(), new MyMemoryTranslator());
    }
}
