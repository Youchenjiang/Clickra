using System;
using System.Collections.Generic;

namespace Clickra.Core.Storage;

public static partial class ClickraStorage
{
    public const string CanceledReason = StorageTaskReasons.Canceled;
    public const string AbandonedReason = StorageTaskReasons.Abandoned;
    public const string LegacyUserAbortedReason = StorageTaskReasons.LegacyUserAborted;

    public static bool IsUserCanceledReason(string? errorMessage) =>
        StorageTaskReasons.IsUserCanceled(errorMessage);


    public static List<HistoryEntry> GetHistory(int limit = 50) => HistoryStore.Read(limit);

    public static void ClearHistory() => HistoryStore.Clear();
}
