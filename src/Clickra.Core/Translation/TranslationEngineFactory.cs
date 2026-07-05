using System;

namespace Clickra.Core
{
    public static class TranslationEngineFactory
    {
        public static ITranslationEngine Create()
        {
            string? engine = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
            if (string.Equals(engine, "identity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(engine, "passthrough", StringComparison.OrdinalIgnoreCase))
            {
                return new IdentityTranslator();
            }
            if (string.Equals(engine, "google", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(engine, "google-free", StringComparison.OrdinalIgnoreCase))
            {
                return new GoogleFreeTranslator();
            }
            if (string.Equals(engine, "mymemory", StringComparison.OrdinalIgnoreCase))
            {
                return new MyMemoryTranslator();
            }

            return new FallbackTranslator(new GoogleFreeTranslator(), new MyMemoryTranslator());
        }
    }
}
