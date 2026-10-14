using System;
using System.Collections.Generic;

namespace Clickra.Core.Storage;

public static partial class ClickraStorage
{
    public const string CanceledReason = "Canceled";
    public const string AbandonedReason = "Abandoned";
    public const string LegacyUserAbortedReason = "User Aborted";

    public static bool IsUserCanceledReason(string? errorMessage) =>
        errorMessage is not null &&
        (errorMessage.Equals(CanceledReason, StringComparison.Ordinal) ||
         errorMessage.Equals(LegacyUserAbortedReason, StringComparison.OrdinalIgnoreCase));

    public struct HistoryEntry
    {
        public string Id { get; set; }
        public string Time { get; set; }
        public string Command { get; set; }
        public int FileCount { get; set; }
        public ConversionStatus Status { get; set; }
        public bool IsSuccess => Status == ConversionStatus.Success;
        public string ErrorMessage { get; set; }
        public string EndTime { get; set; }
        public long ElapsedMs { get; set; }
        public string InputPaths { get; set; }
        public string OutputPath { get; set; }
        public int CurrentIndex { get; set; }
        public int Pid { get; set; }
        public int? ParkedRetentionDays { get; set; }
        public string ParkedSince { get; set; }
    }

    public static List<HistoryEntry> GetHistory(int limit = 50) => HistoryStore.Read(limit);

    public static void ClearHistory() => HistoryStore.Clear();
}
