using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clickra.Core.Modules.HistoryTasks.Application;
using Clickra.Core.Application;

namespace Clickra.Core.Modules.HistoryTasks.Presentation;

/// <summary>皜銝剔??蝔桅?嚗恐??摨憿舐內??嚗脰?銝???敺匱蝥???撌脣?????/summary>
public enum HistoryItemKind
{
    Active,
    Parked,
    Completed
}

public enum HistoryStatusVisualKind
{
    Pending,
    InProgress,
    Success,
    Failed,
    Other
}

/// <summary>
/// 甇瑕皜銝剔?銝???殷???蝝?????拙??ａ閬＊蝷箇?鈭祕??///
/// ??鈭祕嚗???獢?擗???獢?餈啜匱蝥蝚砍嗾??獢??芸?ㄐ蝞?甈～?/// 隞亙? Win32 dashboard ??Fluent ??History ???芸?銝遢?脣??亥岷??典?摰?
/// 蝯????遙??拚??航???牧瘜?銝?牧?隤扎銝?牧?仃??/// 銝?牧??瘨銝?牧?歇????嚗??拚??賢??芸楛????嚗?撘萇?潔?甈⊥?獢閰ｇ???/// ?撅斤?典瘙箏??????脯?/// </summary>
public sealed class HistoryItem
{
    internal HistoryItem(
        HistoryItemKind kind,
        HistoryQueryRecord entry,
        ParkedRetentionQueryState? retention,
        string language)
    {
        Kind = kind;
        Entry = entry;
        Retention = retention;

        CommandLabelKey = ConvertCommandRegistry.GetLabelKey(entry.Command ?? "");
        IsCanceled = kind == HistoryItemKind.Completed && HistoryQueryController.IsUserCanceledReason(entry.ErrorMessage);
        StatusKey = ResolveStatusKey(kind, entry.Status, IsCanceled);
        Time = entry.Time ?? "";
        FileCount = entry.FileCount;
        FirstFileName = FirstFileNameOf(entry);
        FileCountText = DescribeFiles(entry.FileCount, FirstFileName, language);
        StoppedOnText = kind == HistoryItemKind.Parked ? DescribeStoppedOn(entry, language) : "";
        ErrorMessage = entry.ErrorMessage ?? "";
        Subtitle = kind == HistoryItemKind.Parked
            ? JoinParts(FirstFileName, StoppedOnText, ErrorMessage)
            : FileCountText;
        RetentionText = retention.HasValue ? DescribeRetention(retention.Value, language) : "";
        NeedsAttention = retention.HasValue && (retention.Value.IsExpiringSoon || retention.Value.HasExpired);
    }

    /// <summary>???格?芯?蝔柴?/summary>
    public HistoryItemKind Kind { get; }

    /// <summary>??蝝???瑞憯改??閬??亦???嚗匱蝥?瘨?蝝堆??賜摰?/summary>
    public HistoryQueryRecord Entry { get; }

    /// <summary>?誘璅惜??啣??蛛??芰?誘?停?舀?隞斗頨恬???<c>ConvertCommandRegistry</c> 銝?湛???/summary>
    public string CommandLabelKey { get; }

    /// <summary>撌脣???鋡思蝙?刻?瘨???憭望?嚗?/summary>
    public bool IsCanceled { get; }

    /// <summary>??隞嗥????獢嚗?蝜潛?隞餃?瘝????摮????停?臬?????/summary>
    public string StatusKey { get; }

    public string Time { get; }
    public int FileCount { get; }

    /// <summary>蝚砌??撓?交?????瘝?頛詨蝝???箇征摮葡??/summary>
    public string FirstFileName { get; }

    /// <summary>瑼??膩嚗瑼瑼?嚗?瑼????+ N ??獢??∟楝敺??胯 ??獢?/summary>
    public string FileCountText { get; }

    /// <summary>敺匱蝥遙???具洵 i/n 瑼??格?隞餃??箇征摮葡??/summary>
    public string StoppedOnText { get; }

    public string ErrorMessage { get; }

    /// <summary>蝚砌?銵?摮?敺匱蝥遙???獢?繚 蝚?i/n 瑼?繚 ?????園??舀?獢?餈啜?/summary>
    public string Subtitle { get; }

    /// <summary>?拚?靽?????閰梧??芣?敺匱蝥遙????/summary>
    public string RetentionText { get; }

    /// <summary>?喳????歇??嚗?閬??脯?憿舐內敺賜???閮??霅衣內??/summary>
    public bool NeedsAttention { get; }

    /// <summary>?辣隞餃???????芸楛隤輸???銝?典??輻?韏堆?嚗??Ｚ?甇日＊蝷箝敺拙?身摰?/summary>
    public bool HasRetentionOverride => Retention?.IsTaskOverride == true;

    /// <summary>靽?????芣?敺匱蝥遙????/summary>
    public ParkedRetentionQueryState? Retention { get; }

    public bool IsSuccess => Kind == HistoryItemKind.Completed && Entry.Status == HistoryQueryStatus.Success;

    public bool IsPending => Entry.Status == HistoryQueryStatus.Pending;

    public HistoryStatusVisualKind StatusVisualKind => Entry.Status switch
    {
        HistoryQueryStatus.Pending => HistoryStatusVisualKind.Pending,
        HistoryQueryStatus.InProgress => HistoryStatusVisualKind.InProgress,
        HistoryQueryStatus.Success => HistoryStatusVisualKind.Success,
        HistoryQueryStatus.Failed => HistoryStatusVisualKind.Failed,
        _ => HistoryStatusVisualKind.Other
    };

    public long ElapsedMs => Entry.ElapsedMs;

    /// <summary>?拙??Ｗ?函????摮??蝜潛?隞餃??蝛箏?銝莎??摰＊蝷箸????舐???/summary>
    private static string ResolveStatusKey(HistoryItemKind kind, HistoryQueryStatus status, bool isCanceled)
    {
        if (kind == HistoryItemKind.Active)
            return status == HistoryQueryStatus.Pending ? "status_pending" : "status_converting";
        if (kind == HistoryItemKind.Parked)
            return "";
        if (isCanceled)
            return "status_canceled";
        return status == HistoryQueryStatus.Success ? "status_success" : "status_failed";
    }

    private static string FirstFileNameOf(HistoryQueryRecord entry)
    {
        if (string.IsNullOrEmpty(entry.InputPaths)) return "";
        string[] parts = entry.InputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : Path.GetFileName(parts[0]) ?? "";
    }

    private static string DescribeFiles(int fileCount, string firstFileName, string language)
    {
        if (!string.IsNullOrEmpty(firstFileName))
            return fileCount > 1
                ? $"{firstFileName} + {fileCount - 1} {Localization.T("label_files", language)}"
                : firstFileName;
        return $"{fileCount} {Localization.T("label_files", language)}";
    }

    private static string DescribeStoppedOn(HistoryQueryRecord entry, string language)
        => entry.FileCount > 1
            ? string.Format(
                Localization.T("task_file_index", language),
                Math.Clamp(entry.CurrentIndex + 1, 1, entry.FileCount),
                entry.FileCount)
            : "";

    internal static string DescribeRetention(ParkedRetentionQueryState info, string language)
    {
        string phrase;
        if (info.IsUnlimited)
            phrase = Localization.T("task_parked_ttl_unlimited", language);
        else if (info.HasExpired)
            phrase = Localization.T("task_parked_ttl_expired", language);
        else if (info.IsExpiringSoon)
            phrase = Localization.T("task_parked_ttl_expiring_soon", language);
        else if (info.RemainingDays == 1)
            phrase = string.Format(Localization.T("task_parked_ttl_days_one", language), 1);
        else
            phrase = string.Format(Localization.T("task_parked_ttl_days", language), info.RemainingDays);

        return info.IsTaskOverride
            ? phrase + Localization.T("task_parked_ttl_override", language)
            : phrase;
    }

    private static string JoinParts(params string[] parts)
        => string.Join(" 繚 ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

}

/// <summary>
/// 銝隞賣風?脫??殷??脰?銝准?蝜潛??歇摰?銝車?嚗?憿舐內???末??/// Fluent ??History ?? Win32 dashboard ??History ??芾???隞踝?
/// ?隞乓?撟曆辣?刻???銝隞嗡?暻潭????隞嗆憭望??鋡怠?瘨銝??????獢?/// </summary>
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

    /// <summary>憿舐內?????湔??殷??脰?銝???敺匱蝥???撌脣???/summary>
    public IReadOnlyList<HistoryItem> Items => _items;

    /// <summary>瘝?隞颱??嚗?蝔桅瘝?嚗???true??/summary>
    public bool IsEmpty => _items.Length == 0;

    public static HistoryFeed Empty { get; } = new(Array.Empty<HistoryItem>());

    /// <summary>
    /// 霈??甈∪??湔??柴脰?銝剛?敺匱蝥遙???芯遙??嚗歇摰??靘甇瑕瑼?
    /// 敺匱蝥遙???拚????券ㄐ蝞?甈∴?瘥??桐?甈∴?嚗?怎垢銝?嚗?銝府嚗?????    /// </summary>
    public static HistoryFeed Load(string language, int completedLimit = 50)
    {
        var items = new List<HistoryItem>();

        foreach (var task in HistoryQueryController.GetActive())
            items.Add(new HistoryItem(HistoryItemKind.Active, task, null, language));

        foreach (var task in HistoryQueryController.GetParked())
            items.Add(new HistoryItem(HistoryItemKind.Parked, task, HistoryQueryController.GetParkedRetention(task.Id), language));

        foreach (var entry in HistoryQueryController.GetCompleted(completedLimit))
            items.Add(new HistoryItem(HistoryItemKind.Completed, entry, null, language));

        return new HistoryFeed(items.ToArray());
    }

    /// <summary>??蝔桅????殷?蝬剜?憿舐內????/summary>
    public IReadOnlyList<HistoryItem> OfKind(HistoryItemKind kind)
        => _byKind.TryGetValue(kind, out var items) ? items : Array.Empty<HistoryItem>();

    public int Count(HistoryItemKind kind) => OfKind(kind).Count;

    public int ActiveCount => Count(HistoryItemKind.Active);
    public int ParkedCount => Count(HistoryItemKind.Parked);
    public int CompletedCount => Count(HistoryItemKind.Completed);

    /// <summary>撌脣???????/summary>
    public int SuccessCount => OfKind(HistoryItemKind.Completed).Count(item => item.IsSuccess);

    /// <summary>撌脣???銝???嚗仃?????賜??典嚗??拙??Ｗ??祉?蝯梯?銝?湛???/summary>
    public int FailedCount => CompletedCount - SuccessCount;

    /// <summary>?喳????歇????蝜潛?隞餃??詻?/summary>
    public int ExpiringSoonCount => OfKind(HistoryItemKind.Parked).Count(item => item.NeedsAttention);
}
