using System;
using System.Collections.Generic;

namespace Clickra.Core.Modules.Office.Processing;

internal readonly record struct OfficeConversionSettings(
    string Engine,
    string Language,
    string LibreOfficePath,
    bool LibreOfficeRemovalPendingRestart);

internal readonly record struct OfficeConversionRequest(
    string AppType,
    string FullPath,
    string OutputPdfPath,
    int FileIndex,
    int TotalFiles);

internal readonly record struct OfficeInteropRequest(
    string Script,
    int FileIndex,
    int TotalFiles,
    string FilePath,
    string AppName,
    string Language);

internal static class OfficeConversionOptions
{
    public const string EngineKey = "office_engine";
    public const string LanguageKey = "office_language";
    public const string LibreOfficePathKey = "libreoffice_path";
    public const string LibreOfficeRemovalPendingRestartKey = "libreoffice_removal_pending_restart";

    public static OfficeConversionSettings Read(IReadOnlyDictionary<string, object>? options)
    {
        string engine = ReadString(options, EngineKey, ClickraSettings.DefaultOfficeEngineAuto);
        string language = ReadString(options, LanguageKey, ClickraSettings.DefaultEmpty);
        string libreOfficePath = ReadString(options, LibreOfficePathKey, ClickraSettings.DefaultEmpty);
        bool removalPending = ReadBool(options, LibreOfficeRemovalPendingRestartKey);
        return new OfficeConversionSettings(engine, language, libreOfficePath, removalPending);
    }

    private static string ReadString(
        IReadOnlyDictionary<string, object>? options,
        string key,
        string fallback) =>
        options is not null && options.TryGetValue(key, out object? value)
            ? Convert.ToString(value) ?? fallback
            : fallback;

    private static bool ReadBool(IReadOnlyDictionary<string, object>? options, string key) =>
        options is not null &&
        options.TryGetValue(key, out object? value) &&
        bool.TryParse(Convert.ToString(value), out bool parsed) &&
        parsed;
}
