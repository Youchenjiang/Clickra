using System;
using Clickra.Core.Modules.Settings.Application;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

public static class TranslationEngineFactory
{
    public static ITranslationEngine Create(string? enginePreference = null)
    {
        string? engine = enginePreference;
        string savedSetting = SettingsController.TranslationEngine;

        if (string.IsNullOrWhiteSpace(engine))
        {
            // Saved offline selection takes precedence over any environment override.
            if (string.Equals(savedSetting, SettingsContract.TranslationEngineOffline, StringComparison.OrdinalIgnoreCase))
            {
                engine = SettingsContract.TranslationEngineOffline;
            }
            else
            {
                engine = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
                if (string.IsNullOrWhiteSpace(engine))
                {
                    engine = savedSetting;
                }
            }
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
            string.Equals(engine, "google-free", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engine, SettingsContract.TranslationEngineGoogle, StringComparison.OrdinalIgnoreCase))
        {
            return new GoogleFreeTranslator();
        }
        if (string.Equals(engine, SettingsContract.TranslationEngineMyMemory, StringComparison.OrdinalIgnoreCase))
        {
            return new MyMemoryTranslator();
        }
        if (string.Equals(engine, SettingsContract.TranslationEngineAuto, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(engine))
        {
            return new FallbackTranslator(new GoogleFreeTranslator(), new MyMemoryTranslator());
        }

        // Unrecognized or invalid engine selections fail closed to offline mode.
        return new OfflineTranslator();
    }
}
