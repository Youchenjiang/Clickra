using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Clickra.Core.Modules.Sys.HistoryTasks.Application;
using Clickra.Core.Modules.Sys.HistoryTasks.Presentation;

namespace Clickra.Desktop.ViewModels;

/// <summary>Which of the three list sections a row belongs to (spec 4.2).</summary>
public enum HistoryRowKind
{
    Active,
    Parked,
    Completed,
}

/// <summary>Outcome shown by the status icon at the start of a row; told apart by shape (spec 7).</summary>
public enum HistoryRowState
{
    Success,
    Failed,
    Canceled,
    Parked,
    Running,
}

/// <summary>
/// One row of the history list and of 最近處理 on the convert page (spec 4.1, 4.2). Built from a Core
/// <see cref="HistoryItem"/>; the order on screen is status icon → time → coloured feature label → file
/// name → reason, with two icon buttons at the right end. Buttons call back into the owning view model.
/// </summary>
public sealed class HistoryRowViewModel : ViewModelBase
{
    private bool _isExpanded;
    private string _notice = "";
    private string _noticeActionText = "";
    private Action? _noticeAction;
    private double _progressPercent;

    public HistoryRowViewModel(HistoryItem item, IHistoryRowHost host)
    {
        Item = item;
        Host = host;
        Kind = item.Kind switch
        {
            HistoryItemKind.Active => HistoryRowKind.Active,
            HistoryItemKind.Parked => HistoryRowKind.Parked,
            _ => HistoryRowKind.Completed,
        };
        State = Kind switch
        {
            HistoryRowKind.Active => HistoryRowState.Running,
            HistoryRowKind.Parked => HistoryRowState.Parked,
            _ when item.IsSuccess => HistoryRowState.Success,
            _ when item.IsCanceled => HistoryRowState.Canceled,
            _ => HistoryRowState.Failed,
        };

        InputPaths = SplitPaths(item.Entry.InputPaths);
        string first = InputPaths.Count > 0 ? Path.GetFileName(InputPaths[0]) : item.FirstFileName;
        (NameHead, NameTail) = SplitForMiddleTrim(first);
        FullName = first;
        MoreText = item.FileCount > 1 ? $"等 {item.FileCount} 個" : "";

        (FeatureName, FeatureColor, FeatureGeometry) = HistoryFeatures.Describe(item.Entry.Command ?? "");
        if (FeatureName == (item.Entry.Command ?? "") && !string.IsNullOrEmpty(item.CommandLabelKey))
        {
            // Not in the 4.0 action catalog (an older or right-click-only action): use Core's label.
            string label = Clickra.Core.Localization.T(item.CommandLabelKey);
            if (!string.IsNullOrWhiteSpace(label) && label != item.CommandLabelKey) FeatureName = label;
        }
        Started = ParseTime(item.Entry.Time);
        Finished = ParseTime(string.IsNullOrEmpty(item.Entry.EndTime) ? item.Entry.Time : item.Entry.EndTime);

        TimeText = Kind switch
        {
            HistoryRowKind.Parked => DescribeRemaining(item.Retention),
            HistoryRowKind.Active => "",
            _ => Started?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? item.Time,
        };
        Reason = Kind switch
        {
            HistoryRowKind.Parked => FirstLine(item.ErrorMessage),
            HistoryRowKind.Active => item.FileCount > 1 ? $"第 {Math.Min(item.Entry.CurrentIndex + 1, item.FileCount)}/{item.FileCount} 個" : "",
            _ when State == HistoryRowState.Canceled => "已取消",
            _ when State == HistoryRowState.Failed => string.Equals(item.Entry.ErrorMessage, "Abandoned", StringComparison.OrdinalIgnoreCase)
                ? "沒有做完就中斷了"
                : FirstLine(item.ErrorMessage),
            _ => "",
        };
        if (Kind == HistoryRowKind.Active && item.FileCount > 0)
            _progressPercent = Math.Clamp(item.Entry.CurrentIndex * 100.0 / item.FileCount, 0, 100);
    }

    public HistoryItem Item { get; }
    public IHistoryRowHost Host { get; }
    public string Id => Item.Entry.Id;
    public HistoryRowKind Kind { get; }
    public HistoryRowState State { get; }
    public DateTime? Started { get; }
    public DateTime? Finished { get; }

    public bool IsSuccess => State == HistoryRowState.Success;
    public bool IsFailed => State == HistoryRowState.Failed;
    public bool IsCanceled => State == HistoryRowState.Canceled;
    public bool IsParked => State == HistoryRowState.Parked;
    public bool IsRunning => State == HistoryRowState.Running;
    public bool IsCompleted => Kind == HistoryRowKind.Completed;

    public string TimeText { get; }
    public string FeatureName { get; private set; }
    public string FeatureColor { get; }
    public string FeatureGeometry { get; }
    public Avalonia.Media.IImage? FeatureIcon => FeatureIcons.Get((Item.Entry.Command ?? "").Replace('_', '-'));
    /// <summary>Label background: the file-type colour at about 18% (spec 7 檔案類型色).</summary>
    public string FeatureFill => "#2E" + FeatureColor.TrimStart('#');
    /// <summary>Label outline: the file-type colour at about 75%.</summary>
    public string FeatureStroke => "#BF" + FeatureColor.TrimStart('#');

    public string NameHead { get; }
    public string NameTail { get; }
    public string FullName { get; }
    public string MoreText { get; }
    public bool HasMore => MoreText.Length > 0;
    private string _reason = "";
    public string Reason
    {
        get => _reason;
        private set { _reason = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReason)); }
    }
    public bool HasReason => _reason.Length > 0;
    public bool ReasonIsError => State == HistoryRowState.Failed;

    public IReadOnlyList<string> InputPaths { get; }
    public string InputText => InputPaths.Count == 0 ? "" : string.Join(Environment.NewLine, InputPaths);
    public string OutputPath => Item.Entry.OutputPath ?? "";
    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputPath);
    public string OutputText => string.Join(Environment.NewLine, SplitPaths(OutputPath));
    public string TimeDetail
    {
        get
        {
            string when = Started is { } s ? DescribeDay(s) + " " + s.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : Item.Time;
            if (Finished is { } f && Started is { } st && f > st)
                when += " → " + f.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (Item.ElapsedMs > 0)
                when += $"，花了 {Item.ElapsedMs / 1000.0:0.#} 秒";
            return when;
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }
    public string ProgressText => $"{_progressPercent:0}%";

    /// <summary>Running row: pause only makes sense while other files remain (Core parks between files).</summary>
    public bool CanPause => IsRunning && Host.CanPauseRow(this);
    public bool CanStopRunning => IsRunning && Host.CanStopRow(this);

    /// <summary>Live progress of the task this window is running (進行中 row).</summary>
    public void UpdateRunning(double percent, int fileIndex, int fileCount)
    {
        ProgressPercent = Math.Clamp(percent, 0, 100);
        Reason = fileCount > 1 ? $"第 {Math.Min(fileIndex + 1, fileCount)}/{fileCount} 個" : "";
        RefreshButtons();
    }

    /// <summary>Re-reads which buttons apply (pause/stop depend on the running task).</summary>
    public void RefreshButtons()
    {
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStopRunning));
    }

    public string FolderButtonLabel => IsSuccess ? "開啟所在資料夾" : "開啟來源資料夾";

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>One line under the row after a button found something missing (spec 4.2 找不到檔案).</summary>
    public string Notice
    {
        get => _notice;
        private set { _notice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNotice)); }
    }
    public bool HasNotice => _notice.Length > 0;
    public string NoticeActionText
    {
        get => _noticeActionText;
        private set { _noticeActionText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoticeAction)); }
    }
    public bool HasNoticeAction => _noticeActionText.Length > 0;

    public void ShowNotice(string text, string actionText = "", Action? action = null)
    {
        _noticeAction = action;
        NoticeActionText = actionText;
        Notice = text;
    }

    public void ClearNotice() => ShowNotice("");

    // ----- commands (bound by name from the row template) -----
    public void ToggleExpand() => Host.ToggleExpand(this);
    public void OpenFolder() => Host.OpenFolder(this);
    public void Rerun() => Host.Rerun(this);
    public void Resume() => Host.Resume(this);
    public void Stop() => Host.Stop(this);
    public void Pause() => Host.Pause(this);
    public void RunNoticeAction()
    {
        Action? action = _noticeAction;
        ClearNotice();
        action?.Invoke();
    }

    /// <summary>"今天", "昨天", or the date (spec 4.2 依日期分組).</summary>
    public string DayGroup => Started is { } s ? DescribeDay(s) : "";

    internal static string DescribeDay(DateTime when)
    {
        DateTime today = DateTime.Today;
        if (when.Date == today) return "今天";
        if (when.Date == today.AddDays(-1)) return "昨天";
        return when.Year == today.Year
            ? when.ToString("M/d", CultureInfo.InvariantCulture)
            : when.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
    }

    private static string DescribeRemaining(ParkedRetentionQueryState? retention)
    {
        if (retention is not { } r) return "";
        if (r.IsUnlimited) return "永久";
        if (r.HasExpired) return "已過期";
        if (r.IsExpiringSoon) return "不到 1 天";
        return $"剩 {r.RemainingDays} 天";
    }

    private static DateTime? ParseTime(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime t) ? t : null;

    private static IReadOnlyList<string> SplitPaths(string? joined) =>
        string.IsNullOrWhiteSpace(joined)
            ? Array.Empty<string>()
            : joined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string line = text.Split('\n')[0].Trim();
        return line.Length > 60 ? line[..60] + "…" : line;
    }

    /// <summary>
    /// Middle trimming keeps the end of the name and the extension (spec 4.2): the head may shrink with
    /// "…", the tail (last 8 characters before the extension plus the extension) always shows.
    /// </summary>
    internal static (string Head, string Tail) SplitForMiddleTrim(string name)
    {
        if (string.IsNullOrEmpty(name)) return ("", "");
        string ext = Path.GetExtension(name);
        string stem = name[..^ext.Length];
        const int keep = 6;
        if (stem.Length <= keep + 2) return (name, "");
        return (stem[..^keep], stem[^keep..] + ext);
    }
}

/// <summary>What a row needs from the page that shows it.</summary>
public interface IHistoryRowHost
{
    void ToggleExpand(HistoryRowViewModel row);
    void OpenFolder(HistoryRowViewModel row);
    void Rerun(HistoryRowViewModel row);
    void Resume(HistoryRowViewModel row);
    void Stop(HistoryRowViewModel row);
    void Pause(HistoryRowViewModel row);
    bool CanPauseRow(HistoryRowViewModel row);
    bool CanStopRow(HistoryRowViewModel row);
}

/// <summary>Feature name, file-type colour and glyph for a command, shared with the action catalog.</summary>
public static class HistoryFeatures
{
    public static (string Name, string Color, string Geometry) Describe(string command)
    {
        command = command.Replace('_', '-');
        ActionItemViewModel? action = ConvertActionCatalog.GetActions()
            .FirstOrDefault(a => string.Equals(a.Command, command, StringComparison.OrdinalIgnoreCase));
        if (action is null && command.StartsWith("img-to-", StringComparison.OrdinalIgnoreCase))
            action = ConvertActionCatalog.GetActions().FirstOrDefault(a => a.Command == "img-to-png");
        return action is null
            ? (command, "#5C5C5C", "M6,2 H14 L18,6 V22 H6 Z M14,2 V6 H18")
            : (action.FullFeatureName, action.ColorHex, action.IconGeometry);
    }
}
