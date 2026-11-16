using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Modules.Sys.HistoryTasks.Application;
using Clickra.Core.Modules.Sys.HistoryTasks.Presentation;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Presentation.Convert;
using Clickra.Shared;

namespace Clickra.Desktop.ViewModels;

public enum ConvertWorkflowState
{
    DropZone,
    ActionCatalog,
    ActionPage,
    ManagingFiles,
    Executing,
    Completed
}

public sealed class FileItemViewModel(string path) : ViewModelBase
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public string Extension { get; } = System.IO.Path.GetExtension(path).ToLowerInvariant();

    public string FileSizeText
    {
        get
        {
            try
            {
                var fi = new FileInfo(Path);
                if (!fi.Exists) return "";
                long bytes = fi.Length;
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            }
            catch
            {
                return "";
            }
        }
    }
}

public sealed class ActionItemViewModel : ViewModelBase
{
    public string Command { get; }
    public string Name { get; }
    public string ToolTip { get; }
    public string FullFeatureName { get; }
    public string SourceDomain { get; }
    public string ColorHex { get; }
    public string IconGeometry { get; }
    /// <summary>Official feature icon from assets/features (Assets/FeatureIcons.axaml).</summary>
    public Avalonia.Media.IImage? Icon => FeatureIcons.Get(Command);

    public ActionItemViewModel(
        string command,
        string name,
        string toolTip,
        string fullFeatureName,
        string sourceDomain,
        string colorHex,
        string iconGeometry)
    {
        Command = command;
        Name = name;
        ToolTip = toolTip;
        FullFeatureName = fullFeatureName;
        SourceDomain = sourceDomain;
        ColorHex = colorHex;
        IconGeometry = iconGeometry;
    }
}

public sealed class ExecutionResultItemViewModel : ViewModelBase
{
    public string FileName { get; }
    public string FullPath { get; }
    public bool Succeeded { get; set; }
    public bool NeedsPassword { get; set; }
    public string DetailText { get; set; } = "";
    public string PasswordInput { get; set; } = "";

    public ExecutionResultItemViewModel(string path, bool succeeded, string detail = "", bool needsPassword = false)
    {
        FullPath = path;
        FileName = System.IO.Path.GetFileName(path);
        Succeeded = succeeded;
        DetailText = detail;
        NeedsPassword = needsPassword;
    }
}

/// <summary>A row of 最近處理 was pressed: History opens with that task expanded (spec 4.1).</summary>
public sealed class ShowTaskEventArgs(string taskId) : EventArgs
{
    public string TaskId { get; } = taskId;
}

public sealed class ConvertViewModel : HistoryRowHostBase, IConversionSession
{
    private readonly ConvertWorkspaceController _workspace = new();
    private ConvertWorkflowState _state = ConvertWorkflowState.DropZone;
    private ActionItemViewModel? _selectedAction;
    private CancellationTokenSource? _conversionCts;
    private double _progressPercent;
    private string _progressMessage = "";
    private string _resultSummary = "";
    private string _customOutputPath = "";
    private bool _isCustomOutput;

    public ObservableCollection<FileItemViewModel> SelectedFiles { get; } = [];
    public ObservableCollection<ActionItemViewModel> AvailableActions { get; } = [];
    public ObservableCollection<ExecutionResultItemViewModel> ResultItems { get; } = [];
    public ObservableCollection<HistoryRowViewModel> RecentItems { get; } = [];

    /// <summary>Raised when a 最近處理 row is pressed.</summary>
    public event EventHandler<ShowTaskEventArgs>? ShowTaskRequested;

    // ----- the running conversion (IConversionSession) -----
    private string? _runningTaskId;
    private int _runningIndex;
    private int _runningTotal;
    private bool _pauseRequested;
    private bool _fileStepsReported;
    private int _parkedAt;
    private string? _lastPassword;

    public event EventHandler? Changed;
    public event EventHandler? Finished;

    public bool IsRunning => IsExecuting;
    public string? TaskId => _runningTaskId;
    public int FileIndex => _runningIndex;
    public int FileCount => _runningTotal;
    public double Percent => ProgressPercent;
    public int RemainingCount => Math.Max(0, _runningTotal - _runningIndex);
    /// <summary>
    /// Core parks only between files, and only actions that work file by file report each file start, so
    /// ⏸ shows once a file start was reported and another file follows (spec 4.2).
    /// </summary>
    public bool CanPause => IsExecuting && _fileStepsReported && !_pauseRequested && _runningIndex < _runningTotal - 1;

    public void RequestPause()
    {
        if (!CanPause) return;
        _pauseRequested = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop() => CancelConversion();

    public ConvertViewModel()
    {
        IsBusy = () => IsExecuting;
        Options.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "" or nameof(ActionOptionsViewModel.IsReady) or nameof(ActionOptionsViewModel.BlockingText))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(StartButtonText));
            }
        };
        Split.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "" or nameof(SplitWorkspaceViewModel.IsReady) or nameof(SplitWorkspaceViewModel.StartText))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(StartButtonText));
            }
        };
        Order.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrderWorkspaceViewModel.HasOutputName))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(StartButtonText));
            }
        };
        Rename.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RenameWorkspaceViewModel.CanRename) or nameof(RenameWorkspaceViewModel.RenameButtonText))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(StartButtonText));
            }
        };
        RefreshRecentHistory();
    }

    public ConvertWorkflowState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDropZone));
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(IsActionCatalog));
            OnPropertyChanged(nameof(IsActionPage));
            OnPropertyChanged(nameof(IsManagingFiles));
            OnPropertyChanged(nameof(IsExecuting));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(CanStart));
        }
    }

    public bool IsDropZone => State == ConvertWorkflowState.DropZone;
    public bool HasFiles => State != ConvertWorkflowState.DropZone;
    public bool IsActionCatalog => State == ConvertWorkflowState.ActionCatalog;
    public bool IsActionPage => State == ConvertWorkflowState.ActionPage;
    public bool IsManagingFiles => State == ConvertWorkflowState.ManagingFiles;
    public bool IsExecuting => State == ConvertWorkflowState.Executing;
    public bool IsCompleted => State == ConvertWorkflowState.Completed;

    public ActionItemViewModel? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (ReferenceEquals(_selectedAction, value)) return;
            _selectedAction = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(IsRenameAction));
            OnPropertyChanged(nameof(IsOrderAction));
            OnPropertyChanged(nameof(IsSplitAction));
            OnPropertyChanged(nameof(ShowsOutputLocation));
            OnPropertyChanged(nameof(CanStart));
        }
    }

    public string StartButtonText => IsRenameAction
        ? Rename.RenameButtonText
        : IsOrderAction && !Order.HasOutputName ? "先填寫檔名"
        : IsSplitAction ? (Split.BlockingText.Length > 0 ? Split.BlockingText : Split.StartText)
        : Options.BlockingText.Length > 0 ? Options.BlockingText
        : SelectedAction is not null ? $"開始{SelectedAction.Name}" : "開始";

    /// <summary>The options card of the open action (spec 4.9).</summary>

    public bool CanStart => !IsExecuting && SelectedAction is not null && _workspace.CanStart &&
                            (!IsRenameAction || Rename.CanRename) && (!IsOrderAction || Order.HasOutputName) && (!IsSplitAction || Split.IsReady) && Options.IsReady;

    /// <summary>批次重新命名 has its own workspace instead of an options card (spec 4.6).</summary>
    public RenameWorkspaceViewModel Rename { get; } = new();
    public bool IsRenameAction => string.Equals(SelectedAction?.Command, ConvertWorkspaceController.BatchRenameCommand, StringComparison.OrdinalIgnoreCase);
    public bool IsOptionsAction => SelectedAction is not null && !IsRenameAction && !IsOrderAction && !IsSplitAction;

    /// <summary>PDF 分割: page thumbnails and cut points (spec 4.6).</summary>
    public SplitWorkspaceViewModel Split { get; } = new();
    public bool IsSplitAction => SelectedAction?.Command == "split-pdf";
    /// <summary>Printing and renaming produce no new file, so the page has no 「存到…」 line.</summary>
    public bool ShowsOutputLocation => SelectedAction is not null && !IsRenameAction && SelectedAction.Command != "print-pdf";

    /// <summary>合併與拼接: the order list workspace (spec 4.6).</summary>
    public OrderWorkspaceViewModel Order { get; } = new();
    public bool IsOrderAction => SelectedAction?.Command is "merge-pdf" or "img-merge" or "img-stitch";

    public string FileSummaryHeader
    {
        get
        {
            int count = SelectedFiles.Count;
            if (count == 0) return "";
            string domain = PrimaryDomain;
            string typeName = domain switch
            {
                "pdf" => "PDF 檔案",
                "word" => "Word 文件",
                "excel" => "Excel 試算表",
                "ppt" => "PPT 簡報",
                "markdown" => "Markdown 文件",
                "image" => "張圖片",
                _ => FileKindName()
            };
            return domain == "image" ? $"{count} {typeName}" : $"{count} 個 {typeName}";
        }
    }

    /// <summary>「影片」 when every file is one kind Clickra knows by name, else 「檔案」 (2 個影片, 3 個檔案).</summary>
    private string FileKindName()
    {
        var kinds = SelectedFiles.Select(f => KindOf(f.Extension)).Distinct().ToList();
        return kinds.Count == 1 && kinds[0] is not null ? kinds[0]! : "檔案";
    }

    public string FileSummarySubtext
    {
        get
        {
            if (SelectedFiles.Count == 0) return "";
            if (SelectedFiles.Count == 1) return SelectedFiles[0].Name;
            return string.Join("、", SelectedFiles.Take(3).Select(f => f.Name)) + (SelectedFiles.Count > 3 ? " 等" : "");
        }
    }

    public string PrimaryDomain
    {
        get
        {
            if (SelectedFiles.Count == 0) return "file";
            string firstExt = SelectedFiles[0].Extension;
            if (firstExt == ".pdf") return "pdf";
            if (firstExt is ".doc" or ".docx") return "word";
            if (firstExt is ".xls" or ".xlsx") return "excel";
            if (firstExt is ".ppt" or ".pptx") return "ppt";
            if (firstExt is ".md" or ".markdown") return "markdown";
            if (firstExt is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".heic" or ".bmp") return "image";
            return "file";
        }
    }

    public string PrimaryDomainColor => PrimaryDomain switch
    {
        "word" => "#0078D4",
        "excel" => "#107C41",
        "ppt" => "#D83B01",
        "markdown" => "#6048A4",
        "image" => "#00828C",
        "pdf" => "#475569",
        _ => "#5C5C5C"
    };

    public double ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        set { _progressMessage = value; OnPropertyChanged(); }
    }

    public string ResultSummary
    {
        get => _resultSummary;
        set { _resultSummary = value; OnPropertyChanged(); }
    }



    public string CustomOutputPath
    {
        get => _customOutputPath;
        set
        {
            _customOutputPath = value;
            _isCustomOutput = !string.IsNullOrWhiteSpace(value);
            OnPropertyChanged();
        }
    }



    public string OutputSummaryText
    {
        get
        {
            if (_isCustomOutput && !string.IsNullOrWhiteSpace(_customOutputPath))
                return $"這次存到 {TruncatePath(_customOutputPath)}";

            var pref = SettingsController.OutputDirectory;
            return pref.Kind switch
            {
                OutputDirectoryKind.Desktop => "存到桌面",
                OutputDirectoryKind.Downloads => "存到下載",
                OutputDirectoryKind.Custom when !string.IsNullOrWhiteSpace(pref.CustomPath) => $"存到 {TruncatePath(pref.CustomPath)}",
                _ => "存到原本的資料夾"
            };
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var validPaths = paths.Where(File.Exists).ToList();
        if (validPaths.Count == 0) return;

        _workspace.AddFiles(validPaths);
        SyncFilesFromWorkspace();
        UpdateAvailableActions();
        State = ConvertWorkflowState.ActionCatalog;
    }

    public void RemoveFile(string path)
    {
        var remaining = SelectedFiles.Where(f => !string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)).Select(f => f.Path).ToList();
        _workspace.ReplaceFiles(remaining);
        SyncFilesFromWorkspace();
        if (SelectedFiles.Count == 0)
        {
            ResetToStart();
        }
        else
        {
            UpdateAvailableActions();
        }
    }

    public void ToggleManageFiles()
    {
        if (State == ConvertWorkflowState.ManagingFiles)
        {
            State = SelectedAction is not null ? ConvertWorkflowState.ActionPage : ConvertWorkflowState.ActionCatalog;
        }
        else if (HasFiles && !IsExecuting && !IsCompleted)
        {
            State = ConvertWorkflowState.ManagingFiles;
        }
    }

    public void SelectAction(ActionItemViewModel action)
    {
        if (!_workspace.TrySelectCommand(action.Command)) return;
        if (string.Equals(action.Command, ConvertWorkspaceController.BatchRenameCommand, StringComparison.OrdinalIgnoreCase))
            Rename.SetFiles(SelectedFiles.Select(f => f.Path));
        if (action.Command is "merge-pdf" or "img-merge" or "img-stitch")
            Order.SetFiles(action.Command, SelectedFiles.Select(f => f.Path));
        if (action.Command == "split-pdf")
            Split.SetFiles(SelectedFiles.Select(f => f.Path).ToList());
        SelectedAction = action;
        State = ConvertWorkflowState.ActionPage;
    }

    public void BackToCatalog()
    {
        SelectedAction = null;
        State = ConvertWorkflowState.ActionCatalog;
    }

    public void ResetToStart()
    {
        _workspace.ClearFiles();
        SelectedFiles.Clear();
        AvailableActions.Clear();
        ResultItems.Clear();
        SelectedAction = null;
        ProgressPercent = 0;
        ProgressMessage = "";
        ResultSummary = "";
        State = ConvertWorkflowState.DropZone;
        RefreshRecentHistory();
    }

    /// <summary>
    /// Runs the selected action. <paramref name="existingTaskId"/> and <paramref name="resumeStartIndex"/>
    /// continue a parked task (▶ 繼續) from the first file that was not done.
    /// </summary>
    public async Task StartConversionAsync(string? existingTaskId = null, int resumeStartIndex = 0)
    {
        if (SelectedAction is null || !CanStart) return;

        _runningTaskId = existingTaskId;
        _runningIndex = resumeStartIndex;
        _runningTotal = SelectedFiles.Count;
        _pauseRequested = false;
        _fileStepsReported = false;
        _lastPassword = null;
        for (int i = 0; i < SelectedFiles.Count; i++)
            SelectedFiles[i].StatusText = i < resumeStartIndex ? "完成" : i == resumeStartIndex ? "處理中" : "等待中";
        State = ConvertWorkflowState.Executing;
        ProgressPercent = 0;
        ProgressMessage = "準備轉換中...";
        ResultItems.Clear();

        _conversionCts = new CancellationTokenSource();
        var controller = new ConversionExecutionController();
        string command = SelectedAction.Command;
        // Merge and stitch run in the order shown in the workspace.
        var files = (IsOrderAction ? Order.OrderedFiles : SelectedFiles.Select(f => f.Path)).ToArray();

        var options = new Dictionary<string, object>();
        Dictionary<string, string>? renamed = null;
        if (IsRenameAction)
        {
            // Run exactly the plan shown in the preview.
            options = Rename.BuildExecutionOptions();
            renamed = Rename.Preview.Where(r => r.IsChanged).ToDictionary(r => r.NewName, r => r.OldName, StringComparer.OrdinalIgnoreCase);
        }
        else if (IsOrderAction)
        {
            options = Order.BuildOptions();
        }
        else if (IsSplitAction)
        {
            options = Split.BuildOptions();
        }
        else
        {
            options = new ConversionExecutionOptions();
        }

        try
        {
            IReadOnlyList<string> plannedOutputs = controller.Prepare(command, files, options, existingTaskId, resumeStartIndex).Outputs;
            Changed?.Invoke(this, EventArgs.Empty);

            var progress = new Progress<TaskProgressReport>(report =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (report.Total > 0)
                    {
                        ProgressPercent = Math.Clamp((double)report.Current / report.Total * 100.0, 0.0, 100.0);
                    }
                    ProgressMessage = string.IsNullOrWhiteSpace(report.Message)
                        ? $"正在處理 ({report.Current}/{report.Total})..."
                        : report.Message;
                    Changed?.Invoke(this, EventArgs.Empty);
                });
            });

            var callbacks = new ConversionExecutionCallbacks(
                TaskStarted: taskId => Dispatcher.UIThread.Post(() =>
                {
                    _runningTaskId = taskId;
                    Changed?.Invoke(this, EventArgs.Empty);
                }),
                FileStarting: index =>
                {
                    // ⏸: stop cleanly before the next file; Core records the task as parked (待繼續).
                    if (_pauseRequested && index > resumeStartIndex)
                    {
                        _parkedAt = index;
                        throw ConversionExecutionController.CreateParkSignal("已暫停", index);
                    }
                    Dispatcher.UIThread.Post(() =>
                    {
                        _runningIndex = index;
                        _fileStepsReported = true;
                        for (int i = 0; i < SelectedFiles.Count; i++)
                        {
                            if (SelectedFiles[i].NeedsPassword) continue;
                            SelectedFiles[i].StatusText = i < index ? "完成" : i == index ? "處理中" : "等待中";
                        }
                        Changed?.Invoke(this, EventArgs.Empty);
                    });
                });

            var result = await Task.Run(() => controller.ExecutePreparedAsync(callbacks, progress, _conversionCts.Token));

            if (result.Status == ConversionExecutionPresentationStatus.Canceled)
            {
                State = ConvertWorkflowState.ActionPage;
                ProgressMessage = "已停止";
                return;
            }
            if (result.Status == ConversionExecutionPresentationStatus.Parked)
            {
                State = ConvertWorkflowState.Completed;
                int left = Math.Max(0, files.Length - _parkedAt);
                ResultSummary = $"已暫停，剩下 {left} 個可在「歷史」繼續";
                foreach (string file in files.Take(_parkedAt).Skip(resumeStartIndex))
                    ResultItems.Add(new ExecutionResultItemViewModel(file, true, "處理完成"));
                return;
            }

            State = ConvertWorkflowState.Completed;
            ShowResults(result, files, plannedOutputs, renamed);
        }
        catch (OperationCanceledException)
        {
            State = ConvertWorkflowState.ActionPage;
            ProgressMessage = "已取消操作";
        }
        catch (Exception ex)
        {
            State = ConvertWorkflowState.Completed;
            ResultSummary = "轉檔未完成";
            ResultItems.Add(new ExecutionResultItemViewModel(files.FirstOrDefault() ?? "", false, ex.Message));
        }
        finally
        {
            _pauseRequested = false;
            RefreshRecentHistory();
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Fills the result page from what Core reported, not from guesses: the overall status decides
    /// success, and each file is checked against its planned output.
    /// </summary>
    private void ShowResults(
        ConversionExecutionPresentationResult result,
        IReadOnlyList<string> files,
        IReadOnlyList<string> plannedOutputs,
        Dictionary<string, string>? renamed)
    {
        bool ok = result.Status == ConversionExecutionPresentationStatus.Succeeded;
        string error = FirstLine(result.Error);

        if (renamed is not null)
        {
            // Rename: list the files that got a new name.
            var done = result.Outputs.Select(Path.GetFileName).Where(n => n is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (newName, oldName) in renamed)
            {
                bool fileOk = done.Contains(newName);
                ResultItems.Add(new ExecutionResultItemViewModel(newName, fileOk, fileOk ? $"原本是 {oldName}" : (error.Length > 0 ? error : "沒有改名")));
            }
            int count = ResultItems.Count(r => r.Succeeded);
            ResultSummary = ok ? $"重新命名好 {count} 個檔案" : $"重新命名好 {count} 個，{renamed.Count - count} 個沒有改";
            return;
        }

        bool perFile = plannedOutputs.Count == files.Count;
        int succeeded = 0;
        for (int i = 0; i < files.Count; i++)
        {
            string file = files[i];
            if (ResultItems.Any(r => string.Equals(r.FullPath, file, StringComparison.OrdinalIgnoreCase))) continue;
            bool fileOk = ok || (perFile && File.Exists(plannedOutputs[i]) && result.Outputs.Contains(plannedOutputs[i], StringComparer.OrdinalIgnoreCase));
            if (fileOk) succeeded++;
            ResultItems.Add(new ExecutionResultItemViewModel(file, fileOk, fileOk ? "處理完成" : (error.Length > 0 ? error : "沒有完成")));
        }
        int failed = files.Count - succeeded;
        ResultSummary = ok && failed == 0
            ? $"{SelectedAction?.Name}好 {files.Count} 個檔案"
            : $"{SelectedAction?.Name}好 {succeeded} 個，{failed} 個需要處理";
    }

    private static string FirstLine(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Split('\n')[0].Trim();

    /// <summary>再轉一次: the same action on these files, opened on its action page; the user presses 開始.</summary>
    public bool PrepareRerun(string command, IReadOnlyList<string> files)
    {
        if (IsExecuting) return false;
        ResetToStart();
        AddFiles(files);
        if (SelectedFiles.Count == 0) return false;
        ActionItemViewModel? action = FindAction(command);
        if (action is null) return true; // the catalog is shown instead
        SelectAction(action);
        Options.SetImageFormatFromCommand(command); // after SelectAction, which loads the defaults
        return true;
    }

    /// <summary>▶ 繼續: runs a parked task again from the first file that was not done.</summary>
    public async Task ResumeParkedAsync(ParkedTaskResumePresentation task)
    {
        if (IsExecuting) return;
        ResetToStart();
        _workspace.AddFiles(task.Files);
        SyncFilesFromWorkspace();
        UpdateAvailableActions();
        State = ConvertWorkflowState.ActionCatalog;
        ActionItemViewModel? action = FindAction(task.Command);
        if (action is null) return;
        SelectAction(action);
        Options.SetImageFormatFromCommand(task.Command); // after SelectAction, which loads the defaults
        await StartConversionAsync(task.TaskId, task.StartIndex);
    }

    private ActionItemViewModel? FindAction(string command)
    {
        ActionItemViewModel? action = AvailableActions.FirstOrDefault(a => string.Equals(a.Command, command, StringComparison.OrdinalIgnoreCase));
        if (action is null && command.StartsWith("img-to-", StringComparison.OrdinalIgnoreCase))
            action = AvailableActions.FirstOrDefault(a => a.Command.StartsWith("img-to-", StringComparison.OrdinalIgnoreCase));
        return action;
    }

    /// <summary>A 最近處理 row was pressed: show it expanded on the History page.</summary>
    public override void ToggleExpand(HistoryRowViewModel row)
    {
        DismissNoticeExcept(row);
        ShowTaskRequested?.Invoke(this, new ShowTaskEventArgs(row.Id));
    }

    public void CancelConversion()
    {
        _conversionCts?.Cancel();
    }

    public void RefreshRecent() => RefreshRecentHistory();

    public void OpenOutputDirectory()
    {
        string dir = ResolveOutputDirectory();
        if (Directory.Exists(dir))
        {
            OpenPath(dir);
        }
    }

    private void SyncFilesFromWorkspace()
    {
        SelectedFiles.Clear();
        foreach (string file in _workspace.SelectedFiles)
        {
            SelectedFiles.Add(new FileItemViewModel(file));
        }
        OnPropertyChanged(nameof(FileSummaryHeader));
        OnPropertyChanged(nameof(FileSummarySubtext));
        OnPropertyChanged(nameof(PrimaryDomain));
        OnPropertyChanged(nameof(PrimaryDomainColor));
    }

    private void UpdateAvailableActions()
    {
        AvailableActions.Clear();
        if (SelectedFiles.Count == 0) return;

        var catalog = ConvertActionCatalog.GetActions();
        foreach (var action in catalog)
        {
            if (IsActionCompatible(action.Command))
            {
                AvailableActions.Add(action);
            }
        }

        // Section 4.1 rule: 重新命名 (batch-rename) 永遠排在最後一張
        var renameAction = AvailableActions.FirstOrDefault(a => a.Command == ConvertWorkspaceController.BatchRenameCommand);
        if (renameAction is not null)
        {
            AvailableActions.Remove(renameAction);
            AvailableActions.Add(renameAction);
        }
        OnPropertyChanged(nameof(OnlyRename));
        OnPropertyChanged(nameof(OnlyRenameText));
    }

    private bool IsActionCompatible(string command) => _workspace.IsCommandCompatible(command);

    private void RefreshRecentHistory()
    {
        RecentItems.Clear();
        try
        {
            var feed = HistoryFeed.Load(SettingsController.Language, completedLimit: 3);
            foreach (var item in feed.OfKind(HistoryItemKind.Completed).Take(3))
            {
                RecentItems.Add(new HistoryRowViewModel(item, this));
            }
        }
        catch
        {
            // History storage gracefully ignored if unavailable
        }
        OnPropertyChanged(nameof(HasRecentHistory));
    }

    public bool HasRecentHistory => RecentItems.Count > 0;

    private string ResolveOutputDirectory()
    {
        if (_isCustomOutput && Directory.Exists(_customOutputPath))
            return _customOutputPath;

        var pref = SettingsController.OutputDirectory;
        return pref.Kind switch
        {
            OutputDirectoryKind.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            OutputDirectoryKind.Downloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            OutputDirectoryKind.Custom when Directory.Exists(pref.CustomPath) => pref.CustomPath,
            _ => SelectedFiles.Count > 0 ? (System.IO.Path.GetDirectoryName(SelectedFiles[0].Path) ?? ".") : "."
        };
    }

    private static string TruncatePath(string path)
    {
        if (path.Length <= 24) return path;
        string dir = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(dir) ? path : $"…\\{dir}";
    }
}
