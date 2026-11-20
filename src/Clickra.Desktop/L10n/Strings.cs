using System;
using System.Collections.Generic;
using System.Globalization;
using Clickra.Core.Modules.Sys.Settings.Application;

namespace Clickra.Desktop.L10n;

/// <summary>
/// Interface text for the 4.0 window (docs/specs/v4_localization_plan.md, ui_writing_guide.md).
/// The language is read once at start-up: a change in Settings applies after Clickra reopens, so one
/// window never mixes two languages. Every key is a whole sentence; parameters are named
/// (<c>{count}</c>, <c>{name}</c>) so each language can put them where its word order needs them.
/// </summary>
public static class Strings
{
    private static string? _language;

    /// <summary>The language this window was opened in (zh-TW, zh-CN, en-US, ja-JP or ko-KR).</summary>
    public static string Language => _language ??= Clickra.Core.Localization.NormalizeLanguageCode(SettingsController.Language);

    /// <summary>Formatting for dates and numbers follows the interface language, not the system region.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    public static string T(string key) => Clickra.Core.Localization.T(key, Language);

    /// <summary>A sentence with named parameters: <c>F("ui_x", ("count", 3), ("name", file))</c>.</summary>
    public static string F(string key, params (string Name, object? Value)[] args)
    {
        string text = T(key);
        foreach ((string name, object? value) in args)
            text = text.Replace("{" + name + "}", Convert.ToString(value, Culture), StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// A sentence with a count: English has a separate <c>key_one</c> for 1 ("1 file" / "3 files");
    /// languages without plural forms use the one key. <c>{count}</c> is filled in for you.
    /// </summary>
    public static string Count(string key, int count, params (string Name, object? Value)[] args)
    {
        string chosen = count == 1 && Clickra.Core.Localization.HasExactTranslation(key + "_one", Language) ? key + "_one" : key;
        var all = new (string Name, object? Value)[args.Length + 1];
        all[0] = ("count", count);
        args.CopyTo(all, 1);
        return F(chosen, all);
    }

    /// <summary>
    /// The text before and after one parameter, for a sentence with a control in the middle
    /// (「每 [ 3 ] 頁一份」): the sentence stays one key, each language decides where the box goes.
    /// </summary>
    public static (string Before, string After) Around(string key, string name)
    {
        string text = T(key);
        int at = text.IndexOf("{" + name + "}", StringComparison.Ordinal);
        return at < 0 ? (text, "") : (text[..at].TrimEnd(), text[(at + name.Length + 2)..].TrimStart());
    }

    /// <summary>Joins a list the way the language writes one (「、」 in Chinese and Japanese, ", " in English).</summary>
    public static string List(IEnumerable<string> items) => string.Join(T("ui_list_separator"), items);

    /// <summary>The first two names, then 「等」 or its equivalent when there are more.</summary>
    public static string ShortList(IReadOnlyList<string> names) =>
        names.Count <= 2
            ? List(names)
            : F("ui_list_and_more", ("names", List(new[] { names[0], names[1] })));

    /// <summary>
    /// The interface font for the language: the bundled Noto Sans TC for Traditional Chinese, the system's
    /// own UI font elsewhere so Japanese kanji, Simplified hanzi and Hangul use their proper shapes.
    /// </summary>
    public static string FontFamily => Language switch
    {
        "zh-CN" => "Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, avares://Clickra.Desktop/Assets/Fonts#Noto Sans TC, $Default",
        "ja-JP" => "Yu Gothic UI, Meiryo UI, Hiragino Sans, Noto Sans CJK JP, avares://Clickra.Desktop/Assets/Fonts#Noto Sans TC, $Default",
        "ko-KR" => "Malgun Gothic, Apple SD Gothic Neo, Noto Sans CJK KR, avares://Clickra.Desktop/Assets/Fonts#Noto Sans TC, $Default",
        "en-US" => "Segoe UI Variable Text, Segoe UI, avares://Clickra.Desktop/Assets/Fonts#Noto Sans TC, $Default",
        _ => "avares://Clickra.Desktop/Assets/Fonts#Noto Sans TC, Microsoft JhengHei UI, PingFang TC, Noto Sans CJK TC, $Default",
    };
}
