using System;

namespace Clickra.Core;

internal static class TranslationTimeouts
{
    public static TimeSpan ProviderCallTimeout => ReadTimeout(
        "CLICKRA_TRANSLATION_PROVIDER_TIMEOUT_SECONDS",
        TimeSpan.FromSeconds(30),
        maxSeconds: 120);

    public static TimeSpan ChainCallTimeout => ReadTimeout(
        "CLICKRA_TRANSLATION_CHAIN_TIMEOUT_SECONDS",
        TimeSpan.FromSeconds(Math.Clamp(ProviderCallTimeout.TotalSeconds * 2 + 15, 15, 300)),
        maxSeconds: 300);

    private static TimeSpan ReadTimeout(string variable, TimeSpan defaultValue, double maxSeconds)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable(variable), out int seconds) && seconds > 0)
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, (int)maxSeconds));

        return defaultValue;
    }
}
