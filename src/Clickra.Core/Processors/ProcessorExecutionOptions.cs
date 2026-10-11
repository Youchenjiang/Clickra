using System;
using System.Collections.Generic;

namespace Clickra.Core.Processors;

internal static class ProcessorExecutionOptions
{
    public const string FileStartingCallbackKey = "__file_starting_callback";
    public const string OutputPathsKey = "__output_paths";
    public const string DataDirectoryKey = "__data_directory";

    public static Action<int>? ReadFileStartingCallback(IReadOnlyDictionary<string, object>? options) =>
        options is not null &&
        options.TryGetValue(FileStartingCallbackKey, out object? value)
            ? value as Action<int>
            : null;

    public static IReadOnlyList<string>? ReadOutputPaths(IReadOnlyDictionary<string, object>? options) =>
        options is not null &&
        options.TryGetValue(OutputPathsKey, out object? value)
            ? value as IReadOnlyList<string>
            : null;

    public static string ReadDataDirectory(IReadOnlyDictionary<string, object>? options) =>
        options is not null &&
        options.TryGetValue(DataDirectoryKey, out object? value)
            ? Convert.ToString(value) ?? string.Empty
            : string.Empty;
}
