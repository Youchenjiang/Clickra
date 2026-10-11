using System;
using System.Collections.Generic;

namespace Clickra.Core.Processors;

internal static class ExecutionLocalizationOptions
{
    public const string LanguageKey = "ui_language";

    public static string ReadLanguage(IReadOnlyDictionary<string, object>? options) =>
        options is not null && options.TryGetValue(LanguageKey, out object? value)
            ? Convert.ToString(value) ?? ClickraSettings.DefaultEmpty
            : ClickraSettings.DefaultEmpty;
}
