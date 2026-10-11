using System.Diagnostics.CodeAnalysis;

namespace Clickra.Core.Storage;

public static partial class ClickraStorage
{
    public static void StartActiveRecord(string command, int fileCount, string? inputPaths = null) =>
        LegacyActiveRecordStore.Start(command, fileCount, inputPaths);

    public static void SetActiveRecordInProgress() => LegacyActiveRecordStore.SetInProgress();

    public static void SetActiveRecordIndex(int index) => LegacyActiveRecordStore.SetIndex(index);

    [SuppressMessage(
        "Major Code Smell",
        "S107",
        Justification = "Legacy compatibility facade preserves the historical public signature for existing callers.")]
    public static void CompleteActiveRecord(
        string command,
        string startTime,
        bool isSuccess,
        string errorMsg,
        string? endTime = null,
        long elapsedMs = -1,
        string? inputPaths = null,
        string? outputPath = null) =>
        LegacyActiveRecordStore.Complete(new LegacyActiveRecordCompletion
        {
            Command = command,
            StartTime = startTime,
            IsSuccess = isSuccess,
            ErrorMessage = errorMsg,
            EndTime = endTime,
            ElapsedMs = elapsedMs,
            InputPaths = inputPaths,
            OutputPath = outputPath
        });

    public static void ClearActiveRecord() => LegacyActiveRecordStore.Clear();

    public static HistoryEntry? GetActiveEntry() => LegacyActiveRecordStore.Get();
}
