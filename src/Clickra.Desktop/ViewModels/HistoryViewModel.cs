using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Clickra.Core.Modules.Sys.HistoryTasks.Application;
using Clickra.Core.Modules.Sys.HistoryTasks.Presentation;
using Clickra.Core.Modules.Sys.Settings.Application;

namespace Clickra.Desktop.ViewModels;

public enum HistoryFilter
{
    All,
    Success,
    Failed
}

public sealed class HistoryTaskItemViewModel : ViewModelBase
{
    private bool _isExpanded;

    public HistoryItem Item { get; }
    public string FirstFileName => Item.FirstFileName;
    public string CommandLabelKey => Item.CommandLabelKey;
    public string Time => Item.Time;
    public string FileCountText => Item.FileCountText;
    public string Subtitle => Item.Subtitle;
    public bool IsSuccess => Item.Entry.Status == HistoryQueryStatus.Success;
    public bool IsFailed => Item.Entry.Status == HistoryQueryStatus.Failed;
    public bool IsCanceled => Item.IsCanceled;
    public string OutputPath => Item.Entry.OutputPath ?? "";
    public string InputPaths => Item.Entry.InputPaths ?? "";
    public string ErrorMessage => Item.ErrorMessage;

    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputPath);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public HistoryTaskItemViewModel(HistoryItem item)
    {
        Item = item;
    }
}

public sealed class HistoryViewModel : ViewModelBase
{
    private HistoryFilter _filter = HistoryFilter.All;
    private HistoryTaskItemViewModel? _expandedItem;
    private readonly List<HistoryTaskItemViewModel> _allCompleted = [];

    public ObservableCollection<HistoryTaskItemViewModel> DisplayedItems { get; } = [];
    public ObservableCollection<HistoryTaskItemViewModel> ActiveItems { get; } = [];

    public HistoryViewModel()
    {
        Refresh();
    }

    public HistoryFilter Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public int AllCount => _allCompleted.Count;
    public int SuccessCount => _allCompleted.Count(i => i.IsSuccess);
    public int FailedCount => _allCompleted.Count(i => i.IsFailed);

    public bool HasActiveTasks => ActiveItems.Count > 0;
    public bool HasCompletedTasks => DisplayedItems.Count > 0;
    public bool IsEmpty => _allCompleted.Count == 0 && ActiveItems.Count == 0;

    public void Refresh()
    {
        _allCompleted.Clear();
        ActiveItems.Clear();

        try
        {
            var feed = HistoryFeed.Load(SettingsController.Language, completedLimit: 100);

            foreach (var item in feed.OfKind(HistoryItemKind.Active))
            {
                ActiveItems.Add(new HistoryTaskItemViewModel(item));
            }

            foreach (var item in feed.OfKind(HistoryItemKind.Completed))
            {
                _allCompleted.Add(new HistoryTaskItemViewModel(item));
            }
        }
        catch
        {
            // Graceful fallback
        }

        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(SuccessCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(HasActiveTasks));
        OnPropertyChanged(nameof(IsEmpty));

        ApplyFilter();
    }

    public void ToggleExpand(HistoryTaskItemViewModel item)
    {
        if (_expandedItem != null && !ReferenceEquals(_expandedItem, item))
        {
            _expandedItem.IsExpanded = false;
        }

        item.IsExpanded = !item.IsExpanded;
        _expandedItem = item.IsExpanded ? item : null;
    }

    public void ClearHistory()
    {
        try
        {
            HistoryController.Clear();
        }
        catch
        {
            // Best effort clear
        }
        Refresh();
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            OpenPath(path);
        }
    }

    public void OpenFolder(string path)
    {
        string dir = Directory.Exists(path) ? path : (System.IO.Path.GetDirectoryName(path) ?? "");
        if (Directory.Exists(dir))
        {
            OpenPath(dir);
        }
    }

    private void ApplyFilter()
    {
        DisplayedItems.Clear();
        var items = _filter switch
        {
            HistoryFilter.Success => _allCompleted.Where(i => i.IsSuccess),
            HistoryFilter.Failed => _allCompleted.Where(i => i.IsFailed),
            _ => _allCompleted
        };

        foreach (var item in items)
        {
            DisplayedItems.Add(item);
        }

        OnPropertyChanged(nameof(HasCompletedTasks));
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose();
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", $"\"{path}\"")?.Dispose();
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", $"\"{path}\"")?.Dispose();
            }
        }
        catch
        {
            // Best effort
        }
    }
}
