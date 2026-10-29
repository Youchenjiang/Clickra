using System;
using System.Collections.Generic;
using Clickra.Core.Application;

namespace Clickra.Core.Processors;

internal static class ExecutionLocalizationOptions
{
    public const string LanguageKey = ConversionRequest.LanguageOptionKey;

    public static string ReadLanguage(IReadOnlyDictionary<string, object>? options) =>
        options is not null && options.TryGetValue(LanguageKey, out object? value)
            ? Convert.ToString(value) ?? ClickraSettings.DefaultEmpty
            : ClickraSettings.DefaultEmpty;

    /// <summary>
    /// Localizes <paramref name="key"/> for an explicit execution language and formats it.
    /// Processors use this instead of ambient-language localization overloads, whose
    /// one-argument form would otherwise bind a format argument as a language code.
    /// </summary>
    public static string Text(string language, string key, params object[] args)
    {
        string template = Localization.T(key, language);
        return args is { Length: > 0 } ? string.Format(template, args) : template;
    }
}
