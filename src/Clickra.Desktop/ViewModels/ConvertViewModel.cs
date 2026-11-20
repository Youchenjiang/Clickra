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
using Clickra.Core.Modules.Pdf.Decrypt.Application;
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

    // ----- while running (spec 4.1: 能當場補救的問題不算失敗) -----
    private string _status = "";
    private bool _needsPassword;
    private string _passwordInput = "";
    private string _passwordError = "";
    private string _unlockText = "解鎖並繼續";
    private System.Threading.Tasks.TaskCompletionSource<string?>? _passwordWait;

    public string StatusText { get => _status; set { _status = value; OnPropertyChanged(); } }
    public bool NeedsPassword { get => _needsPassword; private set { _needsPassword = value; OnPropertyChanged(); } }
    public string PasswordInput { get => _passwordInput; set { _passwordInput = value ?? ""; OnPropertyChanged(); } }
    public string PasswordError { get => _passwordError; private set { _passwordError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPasswordError)); } }
    public bool HasPasswordError => _passwordError.Length > 0;
    public string UnlockText { get => _unlockText; private set { _unlockText = value; OnPropertyChanged(); } }

    /// <summary>Shows the password field on this row and waits for 解鎖 or 略過.</summary>
    public System.Threading.Tasks.Task<string?> AskPasswordAsync(bool wrong, string unlockText)
    {
        _passwordWait?.TrySetResult(null);
        _passwordWait = new System.Threading.Tasks.TaskCompletionSource<string?>();
        UnlockText = unlockText;
        PasswordError = wrong ? "密碼不正確，請再輸入一次" : "";
        PasswordInput = "";
        StatusText = "需要密碼";
        NeedsPassword = true;
        return _passwordWait.Task;
    }

    public void SubmitPassword()
    {
        if (_passwordInput.Length == 0) return;
        NeedsPassword = false;
        StatusText = "處理中";
        _passwordWait?.TrySetResult(_passwordInput);
    }

    public void SkipPassword()
    {
        NeedsPassword = false;
        StatusText = "已停止";
        _passwordWait?.TrySetResult(null);
    }

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

    private bool _needsDecryptFirst;
    private bool _isSuggested;
    /// <summary>Core refuses PDFs with a password or print/copy restrictions for this action (spec 4.1): the card
    /// stays in place, disabled, and its tooltip says why.</summary>
    public bool NeedsDecryptFirst { get => _needsDecryptFirst; set { _needsDecryptFirst = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAvailable)); OnPropertyChanged(nameof(CardTip)); } }
    public string CardTip => _needsDecryptFirst ? $"有密碼或使用限制的 PDF 要先解密才能{Name}" : ToolTip;
    public bool IsAvailable => !_needsDecryptFirst;
    /// <summary>解密 when some files are protected: first and outlined.</summary>
    public bool IsSuggested { get => _isSuggested; set { _isSuggested = value; OnPropertyChanged(); } }

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
    public string DetailText { get; set; }
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
    private const string ImageDomain = "image";
    private const string ImageFormatPrefix = "img-to-";

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
            OnPropertyChanged(nameof(IsOptionsAction));
            OnPropertyChanged(nameof(IsOrderAction));
            OnPropertyChanged(nameof(IsSplitAction));
            OnPropertyChanged(nameof(ShowsOutputLocation));
            OnPropertyChanged(nameof(CanStart));
        }
    }

    public string StartButtonText => GetStartButtonText();

    private string GetStartButtonText()
    {
        if (IsRenameAction) return Rename.RenameButtonText;
        if (IsOrderAction && !Order.HasOutputName) return "先填寫檔名";
        if (IsSplitAction)
        {
            return Split.BlockingText.Length > 0 ? Split.BlockingText : Split.StartText;
        }
        if (Options.BlockingText.Length > 0) return Options.BlockingText;
        return SelectedAction is not null ? $"開始{SelectedAction.Name}" : "開始";
    }

    /// <summary>The options card of the open action (spec 4.9).</summary>
    public ActionOptionsViewModel Options { get; } = new();

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
                ImageDomain => "張圖片",
                _ => FileKindName()
            };
            return domain == ImageDomain ? $"{count} {typeName}" : $"{count} 個 {typeName}";
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
            // Mixed types (PDF + 圖片) are just 「N 個檔案」, not named after the first file.
            string? firstDomain = null;
            foreach (var f in SelectedFiles)
            {
                var domain = DomainOf(f.Extension);
                if (firstDomain is null)
                {
                    firstDomain = domain;
                }
                else if (firstDomain != domain)
                {
                    return "file";
                }
            }
            return firstDomain ?? "file";
        }
    }

    private static string DomainOf(string ext) => ext switch
    {
        ".pdf" => "pdf",
        ".doc" or ".docx" => "word",
        ".xls" or ".xlsx" => "excel",
        ".ppt" or ".pptx" => "ppt",
        ".md" or ".markdown" => "markdown",
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".heic" or ".bmp" => ImageDomain,
        _ => "file",
    };

    public string PrimaryDomainColor => PrimaryDomain switch
    {
        "word" => "#0078D4",
        "excel" => "#107C41",
        "ppt" => "#D83B01",
        "markdown" => "#6048A4",
        ImageDomain => "#00828C",
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
        var validPaths = ExpandFolders(paths).ToList();
        if (validPaths.Count == 0) return;

        _workspace.AddFiles(validPaths);
        SyncFilesFromWorkspace();
        UpdateAvailableActions();
        State = ConvertWorkflowState.ActionCatalog;
        RefreshProtection();
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
        // 轉換格式 is one card for the five img-to-* commands: select one that fits these files (a PNG
        // cannot be converted to PNG); the options card picks the real target.
        string selectCommand = action.Command.StartsWith(ImageFormatPrefix, StringComparison.OrdinalIgnoreCase)
            ? Clickra.Shared.ContextMenuVisibility.ImageFormatCommands.FirstOrDefault(_workspace.IsCommandCompatible) ?? action.Command
            : action.Command;
        if (!_workspace.TrySelectCommand(selectCommand)) return;
        if (string.Equals(action.Command, ConvertWorkspaceController.BatchRenameCommand, StringComparison.OrdinalIgnoreCase))
            Rename.SetFiles(SelectedFiles.Select(f => f.Path));
        Options.Reset(action.Command);
        if (action.Command == DecryptCommand)
        {
            Options.DecryptNeedsPassword = ProtectedNeedsPassword;
            Options.DecryptSingleFile = (HasProtectedPdf ? GetProtectedFiles().Count : SelectedFiles.Count) <= 1;
        }
        if (action.Command is "merge-pdf" or "img-merge" or "img-stitch")
            Order.SetFiles(action.Command, SelectedFiles.Select(f => f.Path));
        if (action.Command == "split-pdf")
            Split.SetFiles(SelectedFiles.Select(f => f.Path).ToList());
        Options.SetAllowedImageFormats(_workspace.IsCommandCompatible);
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
        _protectionCts?.Cancel();
        _protection.Clear();
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
        var decryptFiles = SelectedAction.Command == DecryptCommand && HasProtectedPdf
            ? GetProtectedFiles().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        _runningTotal = decryptFiles?.Count ?? SelectedFiles.Count;
        _pauseRequested = false;
        _fileStepsReported = false;
        _lastPassword = null;
        State = ConvertWorkflowState.Executing;
        ProgressPercent = 0;
        ProgressMessage = "準備轉換中...";
        ResultItems.Clear();

        _conversionCts = new CancellationTokenSource();
        var controller = new ConversionExecutionController();
        var files = (IsOrderAction ? Order.GetOrderedFiles() : SelectedFiles.Select(f => f.Path))
            .Where(f => decryptFiles is null || decryptFiles.Contains(f))
            .ToArray();
        MarkFileSteps(files, resumeStartIndex);

        var (command, options, renamed) = ResolveExecutionParameters(SelectedAction.Command);

        try
        {
            IReadOnlyList<string> plannedOutputs = controller.Prepare(command, files, options, existingTaskId, resumeStartIndex).Outputs;
            Changed?.Invoke(this, EventArgs.Empty);

            var progress = CreateProgressReporter();
            var callbacks = CreateExecutionCallbacks(files, resumeStartIndex);
            var result = await Task.Run(() => controller.ExecutePreparedAsync(callbacks, progress, _conversionCts.Token), _conversionCts.Token);

            HandleExecutionResult(result, files, plannedOutputs, renamed, decryptFiles, resumeStartIndex);
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
            _conversionCts?.Dispose();
            _conversionCts = null;
            RefreshRecentHistory();
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    private (string command, Dictionary<string, object> options, Dictionary<string, string>? renamed)
        ResolveExecutionParameters(string initialCommand)
    {
        if (IsRenameAction)
        {
            return (
                initialCommand,
                Rename.BuildExecutionOptions(),
                Rename.Preview.Where(r => r.IsChanged).ToDictionary(r => r.NewName, r => r.OldName, StringComparer.OrdinalIgnoreCase)
            );
        }
        if (IsOrderAction)
        {
            return (initialCommand, Order.BuildOptions(), null);
        }
        if (IsSplitAction)
        {
            return (initialCommand, Split.BuildOptions(), null);
        }
        return (Options.ResolveCommand(initialCommand), Options.Build(), null);
    }

    private IProgress<TaskProgressReport> CreateProgressReporter()
    {
        return new Progress<TaskProgressReport>(report =>
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
    }

    private ConversionExecutionCallbacks CreateExecutionCallbacks(string[] files, int resumeStartIndex)
    {
        return new ConversionExecutionCallbacks(
            PasswordPrompt: async (index, file, isWrong, ct) => await PromptPasswordAsync(index, file, isWrong, ct),
            TaskStarted: taskId => Dispatcher.UIThread.Post(() =>
            {
                _runningTaskId = taskId;
                Changed?.Invoke(this, EventArgs.Empty);
            }),
            FileStarting: index =>
            {
                if (_pauseRequested && index > resumeStartIndex)
                {
                    _parkedAt = index;
                    throw ConversionExecutionController.CreateParkSignal("已暫停", index);
                }
                Dispatcher.UIThread.Post(() =>
                {
                    _runningIndex = index;
                    _fileStepsReported = true;
                    MarkFileSteps(files, index);
                    Changed?.Invoke(this, EventArgs.Empty);
                });
            });
    }

    private async Task<string?> PromptPasswordAsync(int index, string file, bool isWrong, CancellationToken ct)
    {
        if (!isWrong && _lastPassword is { } known) return known;
        string unlock = $"解鎖並{SelectedAction?.Name ?? "繼續"}";
        using (ct.Register(() => Dispatcher.UIThread.Post(() =>
        {
            foreach (FileItemViewModel f in SelectedFiles.Where(f => f.NeedsPassword)) f.SkipPassword();
        })))
        {
            string? password = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FileItemViewModel? row = SelectedFiles.FirstOrDefault(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))
                                         ?? (index >= 0 && index < SelectedFiles.Count ? SelectedFiles[index] : null);
                return row is null ? Task.FromResult<string?>(null) : row.AskPasswordAsync(isWrong, unlock);
            });
            if (password is not null) _lastPassword = password;
            return password;
        }
    }

    private void HandleExecutionResult(
        ConversionExecutionPresentationResult result,
        string[] files,
        IReadOnlyList<string> plannedOutputs,
        Dictionary<string, string>? renamed,
        HashSet<string>? decryptFiles,
        int resumeStartIndex)
    {
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
        if (decryptFiles is not null)
        {
            foreach (FileItemViewModel f in SelectedFiles.Where(f => !decryptFiles.Contains(f.Path)))
                ResultItems.Add(new ExecutionResultItemViewModel(f.Path, true, "沒有密碼或限制，不用解密"));
        }
    }

    /// <summary>Shows each row's step by its place in the run (merge runs in the workspace order, 解密 skips files
    /// without protection), not by its place in the file list.</summary>
    private void MarkFileSteps(IReadOnlyList<string> runFiles, int current)
    {
        foreach (FileItemViewModel row in SelectedFiles)
        {
            if (row.NeedsPassword) continue;
            int at = -1;
            for (int i = 0; i < runFiles.Count; i++)
                if (string.Equals(runFiles[i], row.Path, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
            if (at < 0) row.StatusText = "不用處理";
            else if (at < current) row.StatusText = "完成";
            else if (at == current) row.StatusText = "處理中";
            else row.StatusText = "等待中";
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
        string error = FirstLine(result.Error);
        if (renamed is not null)
        {
            ShowRenameResults(result, renamed, error);
            return;
        }

        bool ok = result.Status == ConversionExecutionPresentationStatus.Succeeded;
        bool perFile = plannedOutputs.Count == files.Count;
        int succeeded = 0;
        for (int i = 0; i < files.Count; i++)
        {
            string file = files[i];
            if (ResultItems.Any(r => string.Equals(r.FullPath, file, StringComparison.OrdinalIgnoreCase))) continue;
            bool fileOk = ok || (perFile && File.Exists(plannedOutputs[i]) && result.Outputs.Contains(plannedOutputs[i], StringComparer.OrdinalIgnoreCase));
            if (fileOk) succeeded++;
            string fallback = error.Length > 0 ? error : "沒有完成";
            string detail = fileOk ? "處理完成" : fallback;
            ResultItems.Add(new ExecutionResultItemViewModel(file, fileOk, detail));
        }
        int failed = files.Count - succeeded;
        string actionName = SelectedAction?.Name ?? "";
        ResultSummary = ok && failed == 0
            ? $"{actionName}好 {files.Count} 個檔案"
            : $"{actionName}好 {succeeded} 個，{failed} 個需要處理";
    }

    private void ShowRenameResults(ConversionExecutionPresentationResult result, Dictionary<string, string> renamed, string error)
    {
        bool ok = result.Status == ConversionExecutionPresentationStatus.Succeeded;
        var done = result.Outputs.Select(Path.GetFileName).Where(n => n is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (newName, oldName) in renamed)
        {
            bool fileOk = done.Contains(newName);
            string fallback = error.Length > 0 ? error : "沒有改名";
            string detail = fileOk ? $"原本是 {oldName}" : fallback;
            ResultItems.Add(new ExecutionResultItemViewModel(newName, fileOk, detail));
        }
        int count = ResultItems.Count(r => r.Succeeded);
        ResultSummary = ok ? $"重新命名好 {count} 個檔案" : $"重新命名好 {count} 個，{renamed.Count - count} 個沒有改";
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
        if (action is null && command.StartsWith(ImageFormatPrefix, StringComparison.OrdinalIgnoreCase))
            action = AvailableActions.FirstOrDefault(a => a.Command.StartsWith(ImageFormatPrefix, StringComparison.OrdinalIgnoreCase));
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
        foreach (var action in catalog.Where(action => IsActionCompatible(action.Command)))
        {
            AvailableActions.Add(action);
        }

        // Section 4.1 rule: 重新命名 (batch-rename) 永遠排在最後一張
        var renameAction = AvailableActions.FirstOrDefault(a => a.Command == ConvertWorkspaceController.BatchRenameCommand);
        if (renameAction is not null)
        {
            AvailableActions.Remove(renameAction);
            AvailableActions.Add(renameAction);
        }

        // 解密 depends on the files themselves (spec 4.1): hidden when none is protected (Core refuses unprotected
        // files), first and outlined when some are; the actions Core refuses for protected files stay, disabled.
        var decrypt = AvailableActions.FirstOrDefault(a => a.Command == DecryptCommand);
        if (decrypt is not null)
        {
            AvailableActions.Remove(decrypt);
            if (HasProtectedPdf)
            {
                decrypt.IsSuggested = true;
                AvailableActions.Insert(0, decrypt);
            }
        }
        foreach (ActionItemViewModel action in AvailableActions)
            action.NeedsDecryptFirst = HasProtectedPdf && NeedsUnprotectedInput(action.Command);

        OnPropertyChanged(nameof(OnlyRename));
        OnPropertyChanged(nameof(OnlyRenameText));
        OnPropertyChanged(nameof(HasProtectedPdf));
        OnPropertyChanged(nameof(ProtectedPdfText));
    }

    // ----- 有密碼或使用限制的 PDF (spec 4.1) -----

    private const string DecryptCommand = "decrypt-pdf";
    private enum PdfProtection { None, Restricted, OpenPassword }
    private readonly Dictionary<string, PdfProtection> _protection = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _protectionCts;

    /// <summary>Core refuses protected PDFs for these (they need the file's content unlocked first); split and
    /// PDF → image ask for the password while running, so they stay available.</summary>
    private static bool NeedsUnprotectedInput(string command) =>
        command is "merge-pdf" or "compress-pdf" or "translate-pdf" or "print-pdf";

    private PdfProtection ProtectionOf(string path) =>
        _protection.TryGetValue(path, out PdfProtection value) ? value : PdfProtection.None;

    private List<FileItemViewModel> GetProtectedFiles() =>
        SelectedFiles.Where(f => ProtectionOf(f.Path) != PdfProtection.None).ToList();

    public bool HasProtectedPdf => SelectedFiles.Any(f => ProtectionOf(f.Path) != PdfProtection.None);

    /// <summary>Some files need their open password typed on the decrypt card; restricted ones need none.</summary>
    public bool ProtectedNeedsPassword => SelectedFiles.Any(f => ProtectionOf(f.Path) == PdfProtection.OpenPassword);

    /// <summary>One sentence above the cards: what is protected and what has to wait for 解密.</summary>
    public string ProtectedPdfText
    {
        get
        {
            List<FileItemViewModel> files = GetProtectedFiles();
            if (files.Count == 0) return "";
            bool open = files.Any(f => ProtectionOf(f.Path) == PdfProtection.OpenPassword);
            bool onlyRestricted = !open;
            const string blocked = "合併、壓縮、翻譯、列印要先解密";
            if (SelectedFiles.Count == 1)
            {
                return onlyRestricted
                    ? $"這個 PDF 打開不用密碼，但設了使用限制（例如不能列印或複製文字）。{blocked}，解密不用密碼。"
                    : $"這個 PDF 要密碼才能開啟。{blocked}；分割、轉成圖片會在執行時問密碼。";
            }
            string names = files.Count <= 2
                ? string.Join("、", files.Select(f => f.Name))
                : string.Join("、", files.Take(2).Select(f => f.Name)) + " 等";
            if (files.Count == SelectedFiles.Count)
                return $"這 {files.Count} 個 PDF 都有密碼或使用限制。{blocked}。";
            return $"{SelectedFiles.Count} 個 PDF 中有 {files.Count} 個有密碼或使用限制（{names}）。解密只會處理{(files.Count == 1 ? "這個檔案" : "這些檔案")}；{blocked}。";
        }
    }

    /// <summary>「先解密」 in the note above the cards.</summary>
    public void DecryptFirst()
    {
        ActionItemViewModel? decrypt = AvailableActions.FirstOrDefault(a => a.Command == DecryptCommand);
        if (decrypt is not null) SelectAction(decrypt);
    }

    /// <summary>
    /// Checks the PDFs in the background (each is opened once); until a file is checked it counts as unprotected,
    /// so the cards show at once and are updated when the answer comes.
    /// </summary>
    private void RefreshProtection()
    {
        _protectionCts?.Cancel();
        List<string> pending = SelectedFiles
            .Where(f => f.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && !_protection.ContainsKey(f.Path))
            .Select(f => f.Path)
            .ToList();
        if (pending.Count == 0) return;
        var cts = new CancellationTokenSource();
        _protectionCts = cts;
        _ = Task.Run(() => ScanPendingProtections(pending, cts), cts.Token);
    }

    private void ScanPendingProtections(List<string> pending, CancellationTokenSource cts)
    {
        var found = new Dictionary<string, PdfProtection>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in pending)
        {
            if (cts.IsCancellationRequested) return;
            found[path] = ClassifyPdfProtection(path);
        }
        Dispatcher.UIThread.Post(() => ApplyScannedProtections(found, cts));
    }

    private void ApplyScannedProtections(Dictionary<string, PdfProtection> found, CancellationTokenSource cts)
    {
        if (cts.IsCancellationRequested) return;
        foreach (var (path, protection) in found) _protection[path] = protection;
        if (found.Values.Any(p => p != PdfProtection.None))
        {
            UpdateAvailableActions();
            if (SelectedAction?.Command == DecryptCommand) Options.DecryptNeedsPassword = ProtectedNeedsPassword;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("DeepSource", "CS-R1008", Justification = "Gracefully handles corrupted or unreadable PDF encryption")]
    private static PdfProtection ClassifyPdfProtection(string path)
    {
        try
        {
            if (!PdfEncryptionQuery.IsEncrypted(path))
            {
                return PdfProtection.None;
            }
            if (PdfEncryptionQuery.NeedsOpenPassword(path))
            {
                return PdfProtection.OpenPassword;
            }
            return PdfProtection.Restricted;
        }
        catch (Exception)
        {
            return PdfProtection.None;
        }
    }

    /// <summary>
    /// A dropped folder adds the files directly inside it (not its subfolders), sorted by name; hidden and
    /// system files are left out (spec 4.1, decision 2026-11-17). Converting a whole folder as one item comes later.
    /// </summary>
    public static IEnumerable<string> ExpandFolders(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                yield return path;
            }
            else if (Directory.Exists(path))
            {
                foreach (string file in SafeEnumerateFiles(path)) yield return file;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("DeepSource", "CS-R1008", Justification = "Gracefully handles directory access/permission errors")]
    private static IEnumerable<string> SafeEnumerateFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .Where(f => (File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    // ----- 檔案沒有想要的功能時 (spec 4.1) -----

    /// <summary>Renaming works on every file, so this is the case where nothing else can be done with them.</summary>
    public bool OnlyRename => AvailableActions.Count == 1 && AvailableActions[0].Command == ConvertWorkspaceController.BatchRenameCommand;

    /// <summary>One plain sentence above the lone 重新命名 card, naming the kind of file rather than its extension.</summary>
    public string OnlyRenameText => GetOnlyRenameText();

    private string GetOnlyRenameText()
    {
        if (!OnlyRename) return "";
        List<string> unknown = SelectedFiles.Select(f => f.Extension).Where(e => DomainOf(e) == "file").Distinct().ToList();
        if (unknown.Count == 0)
            return "這些檔案的種類不同，沒有能一起做的轉換，所以只能一起重新命名。";
        string kind = KindName(unknown);
        bool mixed = unknown.Count < SelectedFiles.Select(f => f.Extension).Distinct().Count();
        return mixed
            ? $"這些檔案裡有{kind}，Clickra 還沒有處理{kind}的功能，所以只能一起重新命名。"
            : $"Clickra 還沒有處理{kind}的功能，目前只能重新命名。";
    }

    /// <summary>影片／音訊／壓縮檔 when all unknown extensions are one such kind; otherwise 「這種檔案（.xyz）」.</summary>
    public static string KindName(IReadOnlyCollection<string> extensions)
    {
        var kinds = extensions.Select(KindOf).Distinct().ToList();
        if (kinds.Count == 1 && kinds[0] is not null) return kinds[0]!;
        return extensions.Count == 1 ? $"這種檔案（{extensions.First()}）" : $"這幾種檔案（{string.Join("、", extensions)}）";
    }

    private static string? KindOf(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".m4v" or ".webm" or ".flv" => "影片",
        ".mp3" or ".wav" or ".m4a" or ".flac" or ".aac" or ".ogg" or ".wma" => "音訊",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "壓縮檔",
        _ => null,
    };

    /// <summary>True when Clickra has conversions for this extension (everything else can only be renamed).</summary>
    public static bool HasConversions(string extension) => DomainOf(extension.ToLowerInvariant()) != "file";

    /// <summary>
    /// 建議新功能: a mail with the file types and the version filled in. Only extensions go along, never file
    /// names, which may be personal and say nothing about the feature.
    /// </summary>
    public void SuggestFeature()
    {
        var groups = SelectedFiles.GroupBy(f => f.Extension.Length > 0 ? f.Extension : "（沒有副檔名）")
            .Select(g => $"{g.Key}（{g.Count()} 個）").ToList();
        string types = groups.Count > 0 ? string.Join("、", groups) : "（還沒放檔案）";
        string subjectTypes = SelectedFiles.Count > 0 ? string.Join("、", SelectedFiles.Select(f => f.Extension).Where(e => e.Length > 0).Distinct()) : "";
        string subject = subjectTypes.Length > 0 ? $"Clickra 功能建議：{subjectTypes}" : "Clickra 功能建議";
        string body = $"我想做的是：\n\n\n──\n檔案類型：{types}\nClickra 版本：{ExternalOpen.AppVersion}\n（不會附上檔名或檔案）";
        ExternalOpen.Mail(subject, body);
    }

    private bool IsActionCompatible(string command) =>
        command.StartsWith("img-to-", StringComparison.OrdinalIgnoreCase)
            ? Clickra.Shared.ContextMenuVisibility.ImageFormatCommands.Any(_workspace.IsCommandCompatible)
            : _workspace.IsCommandCompatible(command);

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
