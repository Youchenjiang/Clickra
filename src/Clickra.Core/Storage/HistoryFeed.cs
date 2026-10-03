using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clickra.Core.Processors;

namespace Clickra.Core;

/// <summary>清單中的項目種類；宣告順序即顯示順序（進行中 → 待繼續 → 已完成）。</summary>
public enum HistoryItemKind
{
    Active,
    Parked,
    Completed
}

/// <summary>
/// 歷史清單中的一個項目：原始紀錄，加上兩個介面都要顯示的事實。
///
/// 這些事實（狀態文案鍵、剩餘期限、檔案描述、繼續到第幾個檔案）只在這裡算一次。
/// 以前 Win32 dashboard 與 Fluent 的 History 頁各自從三份儲存查詢重新推導它們，
/// 結果同一個任務在兩邊可能有不同說法（一個說「錯誤」另一個說「失敗」、
/// 一個說「取消」另一個說「已取消」），而且兩邊都得自己重算期限（每張畫格一次檔案查詢）。
/// 呈現層現在只決定排版與顏色。
/// </summary>
public sealed class HistoryItem
{
    internal HistoryItem(HistoryItemKind kind, ClickraStorage.HistoryEntry entry, ClickraStorage.ParkedRetentionInfo? retention)
    {
        Kind = kind;
        Entry = entry;
        Retention = retention;

        CommandLabelKey = ConvertCommandRegistry.GetLabelKey(entry.Command ?? "");
        IsCanceled = kind == HistoryItemKind.Completed && ClickraStorage.IsUserCanceledReason(entry.ErrorMessage);
        StatusKey = ResolveStatusKey(kind, entry.Status, IsCanceled);
        Time = entry.Time ?? "";
        FileCount = entry.FileCount;
        FirstFileName = FirstFileNameOf(entry);
        FileCountText = DescribeFiles(entry.FileCount, FirstFileName);
        StoppedOnText = kind == HistoryItemKind.Parked ? DescribeStoppedOn(entry) : "";
        ErrorMessage = entry.ErrorMessage ?? "";
        Subtitle = kind == HistoryItemKind.Parked
            ? JoinParts(FirstFileName, StoppedOnText, ErrorMessage)
            : FileCountText;
        RetentionText = retention.HasValue ? ClickraStorage.DescribeParkedRetention(retention.Value) : "";
        NeedsAttention = retention.HasValue && (retention.Value.IsExpiringSoon || retention.Value.HasExpired);
    }

    /// <summary>這個項目是哪一種。</summary>
    public HistoryItemKind Kind { get; }

    /// <summary>原始紀錄；具破壞性／需要識別的動作（繼續、取消、明細）都用它。</summary>
    public ClickraStorage.HistoryEntry Entry { get; }

    /// <summary>指令標籤的在地化鍵；未知指令時就是指令本身（與 <c>ConvertCommandRegistry</c> 一致）。</summary>
    public string CommandLabelKey { get; }

    /// <summary>已完成且被使用者取消（而非失敗）。</summary>
    public bool IsCanceled { get; }

    /// <summary>這一件的狀態文案鍵；待繼續任務沒有狀態文字——它的狀態就是它的期限。</summary>
    public string StatusKey { get; }

    public string Time { get; }
    public int FileCount { get; }

    /// <summary>第一個輸入檔的檔名；沒有輸入紀錄時為空字串。</summary>
    public string FirstFileName { get; }

    /// <summary>檔案描述：單檔是檔名，多檔是「檔名 + N 個檔案」，無路徑時是「N 個檔案」。</summary>
    public string FileCountText { get; }

    /// <summary>待繼續任務停在「第 i/n 檔」；單檔任務為空字串。</summary>
    public string StoppedOnText { get; }

    public string ErrorMessage { get; }

    /// <summary>第二行文字：待繼續任務是「檔案 · 第 i/n 檔 · 原因」，其餘是檔案描述。</summary>
    public string Subtitle { get; }

    /// <summary>剩餘保留期限那句話；只有待繼續任務有。</summary>
    public string RetentionText { get; }

    /// <summary>即將過期或已過期：列要變色、要顯示徽章、要計入聚合警示。</summary>
    public bool NeedsAttention { get; }

    /// <summary>這件任務的保留期限是自己調過的（不隨全域政策走）；介面藉此顯示「恢復全域設定」。</summary>
    public bool HasRetentionOverride => Retention?.IsTaskOverride == true;

    /// <summary>保留狀態；只有待繼續任務有。</summary>
    public ClickraStorage.ParkedRetentionInfo? Retention { get; }

    public bool IsSuccess => Kind == HistoryItemKind.Completed && Entry.Status == ConversionStatus.Success;

    public long ElapsedMs => Entry.ElapsedMs;

    /// <summary>兩個介面共用的狀態文字鍵。待繼續任務回傳空字串，因為它顯示期限而不是狀態。</summary>
    private static string ResolveStatusKey(HistoryItemKind kind, ConversionStatus status, bool isCanceled)
    {
        if (kind == HistoryItemKind.Active)
            return status == ConversionStatus.Pending ? "status_pending" : "status_converting";
        if (kind == HistoryItemKind.Parked)
            return "";
        if (isCanceled)
            return "status_canceled";
        return status == ConversionStatus.Success ? "status_success" : "status_failed";
    }

    private static string FirstFileNameOf(ClickraStorage.HistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.InputPaths)) return "";
        string[] parts = entry.InputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : Path.GetFileName(parts[0]) ?? "";
    }

    private static string DescribeFiles(int fileCount, string firstFileName)
    {
        if (!string.IsNullOrEmpty(firstFileName))
            return fileCount > 1
                ? $"{firstFileName} + {fileCount - 1} {Localization.T("label_files")}"
                : firstFileName;
        return $"{fileCount} {Localization.T("label_files")}";
    }

    private static string DescribeStoppedOn(ClickraStorage.HistoryEntry entry)
        => entry.FileCount > 1
            ? Localization.T("task_file_index", Math.Clamp(entry.CurrentIndex + 1, 1, entry.FileCount), entry.FileCount)
            : "";

    private static string JoinParts(params string[] parts)
        => string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}

/// <summary>
/// 一份歷史清單：進行中、待繼續與已完成三種項目，依顯示順序排好。
/// Fluent 的 History 頁與 Win32 dashboard 的 History 頁都只讀這一份，
/// 所以「有幾件在跑」「下一件什麼時候過期」「那件是失敗還是被取消」在三者之間只有一個答案。
/// </summary>
public sealed class HistoryFeed
{
    private readonly HistoryItem[] _items;
    private readonly Dictionary<HistoryItemKind, HistoryItem[]> _byKind;

    private HistoryFeed(HistoryItem[] items)
    {
        _items = items;
        _byKind = items
            .GroupBy(item => item.Kind)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    /// <summary>顯示順序的完整清單：進行中 → 待繼續 → 已完成。</summary>
    public IReadOnlyList<HistoryItem> Items => _items;

    /// <summary>沒有任何項目（三種都沒有）時為 true。</summary>
    public bool IsEmpty => _items.Length == 0;

    public static HistoryFeed Empty { get; } = new(Array.Empty<HistoryItem>());

    /// <summary>
    /// 讀取一次完整清單。進行中與待繼續任務來自任務檔，已完成項目來自歷史檔；
    /// 待繼續任務的剩餘期限在這裡算一次（每個項目一次），呼叫端不必（也不該）逐列重算。
    /// </summary>
    public static HistoryFeed Load(int completedLimit = 50)
    {
        var items = new List<HistoryItem>();

        foreach (var task in ClickraStorage.GetActiveTasks())
            items.Add(new HistoryItem(HistoryItemKind.Active, task, null));

        foreach (var task in ClickraStorage.GetParkedTasks())
            items.Add(new HistoryItem(HistoryItemKind.Parked, task, ClickraStorage.GetParkedRetentionInfo(task.Id)));

        foreach (var entry in ClickraStorage.GetHistory(completedLimit))
            items.Add(new HistoryItem(HistoryItemKind.Completed, entry, null));

        return new HistoryFeed(items.ToArray());
    }

    /// <summary>指定種類的項目，維持顯示順序。</summary>
    public IReadOnlyList<HistoryItem> OfKind(HistoryItemKind kind)
        => _byKind.TryGetValue(kind, out var items) ? items : Array.Empty<HistoryItem>();

    public int Count(HistoryItemKind kind) => OfKind(kind).Count;

    public int ActiveCount => Count(HistoryItemKind.Active);
    public int ParkedCount => Count(HistoryItemKind.Parked);
    public int CompletedCount => Count(HistoryItemKind.Completed);

    /// <summary>已完成而且成功。</summary>
    public int SuccessCount => OfKind(HistoryItemKind.Completed).Count(item => item.IsSuccess);

    /// <summary>已完成但不算成功（失敗與取消都算在內，與兩個介面原本的統計一致）。</summary>
    public int FailedCount => CompletedCount - SuccessCount;

    /// <summary>即將過期或已過期的待繼續任務數。</summary>
    public int ExpiringSoonCount => OfKind(HistoryItemKind.Parked).Count(item => item.NeedsAttention);
}
