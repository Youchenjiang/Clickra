using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using Clickra.Desktop.L10n;

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
    private string _unlockText = Strings.T("ui_password_unlock_and_continue");
    private System.Threading.Tasks.TaskCompletionSource<PasswordPromptResult>? _passwordWait;

    public string StatusText { get => _status; set { _status = value; OnPropertyChanged(); } }
    public bool NeedsPassword { get => _needsPassword; private set { _needsPassword = value; OnPropertyChanged(); } }
    public string PasswordInput { get => _passwordInput; set { _passwordInput = value ?? ""; OnPropertyChanged(); } }
    public string PasswordError { get => _passwordError; private set { _passwordError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPasswordError)); } }
    public bool HasPasswordError => _passwordError.Length > 0;
    public string UnlockText { get => _unlockText; private set { _unlockText = value; OnPropertyChanged(); } }

    /// <summary>Shows the password field on this row and waits for 解鎖, 略過, or 停止.</summary>
    public System.Threading.Tasks.Task<PasswordPromptResult> AskPasswordAsync(bool wrong, string unlockText)
    {
        _passwordWait?.TrySetResult(PasswordPromptResult.Skip);
        _passwordWait = new System.Threading.Tasks.TaskCompletionSource<PasswordPromptResult>();
        UnlockText = unlockText;
        PasswordError = wrong ? Strings.T("ui_password_wrong_password_try_again") : "";
        PasswordInput = "";
        StatusText = Strings.T("ui_password_password_needed");
        NeedsPassword = true;
        return _passwordWait.Task;
    }

    public void SubmitPassword()
    {
        if (_passwordInput.Length == 0) return;
        NeedsPassword = false;
        StatusText = Strings.T("ui_run_processing");
        _passwordWait?.TrySetResult(PasswordPromptResult.FromPassword(_passwordInput));
    }

    public void SkipPassword()
    {
        NeedsPassword = false;
        StatusText = Strings.T("ui_result_skipped_needs_password");
        _passwordWait?.TrySetResult(PasswordPromptResult.Skip);
    }

    public void CancelAll()
    {
        NeedsPassword = false;
        StatusText = Strings.T("ui_run_stopped_2");
        _passwordWait?.TrySetResult(PasswordPromptResult.Cancel);
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

    private bool _isSuggested;
    public string CardTip => ToolTip;
    public static bool IsAvailable => true;
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
    public bool IsSkipped { get; set; }
    public string DetailText { get; set; }
    public string PasswordInput { get; set; } = "";

    public bool ShowSuccessIcon => Succeeded && !IsSkipped;
    public bool ShowFailedIcon => !Succeeded && !IsSkipped;

    public ExecutionResultItemViewModel(string path, bool succeeded, string detail = "", bool needsPassword = false, bool isSkipped = false)
    {
        FullPath = path;
        FileName = System.IO.Path.GetFileName(path);
        Succeeded = succeeded;
        DetailText = detail;
        NeedsPassword = needsPassword;
        IsSkipped = isSkipped;
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

    private const string PrintPdfCommand = "print-pdf";

    public ConvertViewModel()
    {
        IsBusy = () => IsExecuting;
        Options.PropertyChanged += OnOptionsPropertyChanged;
        PrintPreview.PropertyChanged += OnPrintPreviewPropertyChanged;
        Split.Zoom.PropertyChanged += OnZoomPropertyChanged;
        PrintPreview.Zoom.PropertyChanged += OnZoomPropertyChanged;
        Split.PropertyChanged += OnSplitPropertyChanged;
        Order.PropertyChanged += OnOrderPropertyChanged;
        Rename.PropertyChanged += OnRenamePropertyChanged;
        RefreshRecentHistory();
    }

    private void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsPrintAction && IsPreviewAffectingProperty(e.PropertyName))
        {
            PrintPreview.Update(SelectedFiles.Select(f => f.Path).ToList(), Options);
        }
        if (e.PropertyName is "" or nameof(ActionOptionsViewModel.IsReady) or nameof(ActionOptionsViewModel.BlockingText))
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(StartButtonText));
        }
    }

    private void OnPrintPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "" or nameof(PrintPreviewViewModel.CanPrint) or nameof(PrintPreviewViewModel.BlockingText))
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(StartButtonText));
        }
    }

    private void OnZoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ZoomPreviewViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(IsZoomPreviewOpen));
            OnPropertyChanged(nameof(ActiveZoomPreview));
        }
    }

    private void OnSplitPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "" or nameof(SplitWorkspaceViewModel.IsReady) or nameof(SplitWorkspaceViewModel.StartText))
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(StartButtonText));
        }
    }

    private void OnOrderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderWorkspaceViewModel.HasOutputName))
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(StartButtonText));
        }
    }

    private void OnRenamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RenameWorkspaceViewModel.CanRename) or nameof(RenameWorkspaceViewModel.RenameButtonText))
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(StartButtonText));
        }
    }

    private static bool IsPreviewAffectingProperty(string? propertyName) =>
        propertyName is ""
            or nameof(ActionOptionsViewModel.IsLoadingPrinters)
            or nameof(ActionOptionsViewModel.PrintPages)
            or nameof(ActionOptionsViewModel.Copies)
            or nameof(ActionOptionsViewModel.PaperIndex)
            or nameof(ActionOptionsViewModel.OrientationIndex)
            or nameof(ActionOptionsViewModel.DuplexIndex)
            or nameof(ActionOptionsViewModel.CanDuplex)
            or nameof(ActionOptionsViewModel.PagesPerSheetIndex)
            or nameof(ActionOptionsViewModel.PagesPerSheet)
            or nameof(ActionOptionsViewModel.PageOrderIndex)
            or nameof(ActionOptionsViewModel.PageOrder)
            or nameof(ActionOptionsViewModel.PageBorder)
            or nameof(ActionOptionsViewModel.ScaleModeIndex)
            or nameof(ActionOptionsViewModel.ScaleMode)
            or nameof(ActionOptionsViewModel.ScalePercent)
            or nameof(ActionOptionsViewModel.ColorModeIndex)
            or nameof(ActionOptionsViewModel.Grayscale)
            or nameof(ActionOptionsViewModel.DarkenText)
            or nameof(ActionOptionsViewModel.Collate);

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
        if (IsOrderAction && !Order.HasOutputName) return Strings.T("ui_start_bar_enter_a_file_name_first");
        if (IsSplitAction)
        {
            return Split.BlockingText.Length > 0 ? Split.BlockingText : Split.StartText;
        }
        if (IsPrintAction)
        {
            if (Options.BlockingText.Length > 0) return Options.BlockingText;
            if (!PrintPreview.CanPrint && !string.IsNullOrEmpty(PrintPreview.BlockingText)) return PrintPreview.BlockingText;
        }
        if (Options.BlockingText.Length > 0) return Options.BlockingText;
        return SelectedAction is not null ? Strings.F("ui_start_bar_start_action", ("action", SelectedAction.Name)) : Strings.T("ui_start_bar_start");
    }

    /// <summary>The options card of the open action (spec 4.9).</summary>
    public ActionOptionsViewModel Options { get; } = new();

    /// <summary>Print preview layout and sheet planning (spec 4.9, print_preview_plan.md).</summary>
    public PrintPreviewViewModel PrintPreview { get; } = new();
    public bool IsPrintAction => string.Equals(SelectedAction?.Command, PrintPdfCommand, StringComparison.OrdinalIgnoreCase);

    public bool IsZoomPreviewOpen => (IsSplitAction && Split.IsPreviewOpen) || (IsPrintAction && PrintPreview.IsPreviewOpen);
    public ZoomPreviewViewModel? ActiveZoomPreview
    {
        get
        {
            if (IsSplitAction && Split.IsPreviewOpen)
            {
                return Split.Zoom;
            }
            if (IsPrintAction && PrintPreview.IsPreviewOpen)
            {
                return PrintPreview.Zoom;
            }
            return null;
        }
    }

    public bool CanStart => !IsExecuting && SelectedAction is not null && _workspace.CanStart &&
                            (!IsRenameAction || Rename.CanRename) && (!IsOrderAction || Order.HasOutputName) && (!IsSplitAction || Split.IsReady) && Options.IsReady &&
                            (!IsPrintAction || PrintPreview.CanPrint);

    /// <summary>批次重新命名 has its own workspace instead of an options card (spec 4.6).</summary>
    public RenameWorkspaceViewModel Rename { get; } = new();
    public bool IsRenameAction => string.Equals(SelectedAction?.Command, ConvertWorkspaceController.BatchRenameCommand, StringComparison.OrdinalIgnoreCase);
    public bool IsOptionsAction => SelectedAction is not null && !IsRenameAction && !IsOrderAction && !IsSplitAction;

    /// <summary>PDF 分割: page thumbnails and cut points (spec 4.6).</summary>
    public SplitWorkspaceViewModel Split { get; } = new();
    public bool IsSplitAction => SelectedAction?.Command == "split-pdf";
    /// <summary>Printing and renaming produce no new file, so the page has no 「存到…」 line.</summary>
    public bool ShowsOutputLocation => SelectedAction is not null && !IsRenameAction && SelectedAction.Command != PrintPdfCommand;

    /// <summary>合併與拼接: the order list workspace (spec 4.6).</summary>
    public OrderWorkspaceViewModel Order { get; } = new();
    public bool IsOrderAction => SelectedAction?.Command is "merge-pdf" or "img-merge" or "img-stitch";

    public string FileSummaryHeader => BuildFileSummaryHeader();

    private string BuildFileSummaryHeader()
    {
        int count = SelectedFiles.Count;
        if (count == 0) return "";
        string? key = PrimaryDomain switch
        {
            "pdf" => "ui_files_count_pdf",
            "word" => "ui_files_count_word",
            "excel" => "ui_files_count_excel",
            "ppt" => "ui_files_count_ppt",
            "markdown" => "ui_files_count_markdown",
            ImageDomain => "ui_files_count_images",
            _ => null
        };
        if (key is not null) return Strings.Count(key, count);
        // 「2 個影片」 when every file is one kind Clickra knows by name, else 「3 個檔案」.
        var kinds = SelectedFiles.Select(f => KindOf(f.Extension)).Distinct().ToList();
        return kinds.Count == 1 && kinds[0] is not null
            ? Strings.Count("ui_files_count_kind", count, ("kind", kinds[0]!))
            : Strings.Count("ui_files_count_files", count);
    }

    public string FileSummarySubtext
    {
        get
        {
            if (SelectedFiles.Count == 0) return "";
            if (SelectedFiles.Count == 1) return SelectedFiles[0].Name;
            return SelectedFiles.Count > 3
                ? Strings.F("ui_list_and_more", ("names", Strings.List(SelectedFiles.Take(3).Select(f => f.Name))))
                : Strings.List(SelectedFiles.Select(f => f.Name));
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
                return Strings.F("ui_start_bar_save_this_time", ("path", TruncatePath(_customOutputPath)));

            var pref = SettingsController.OutputDirectory;
            return pref.Kind switch
            {
                OutputDirectoryKind.Desktop => Strings.T("ui_start_bar_save_to_desktop"),
                OutputDirectoryKind.Downloads => Strings.T("ui_start_bar_save_to_downloads"),
                OutputDirectoryKind.Custom when !string.IsNullOrWhiteSpace(pref.CustomPath) => Strings.F("ui_start_bar_save_to_path", ("path", TruncatePath(pref.CustomPath))),
                _ => Strings.T("ui_start_bar_save_to_the_original_folder")
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
        Clickra.Core.Modules.Pdf.Print.Ports.SystemPrinterDiscoveryService.WarmUp();
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

        // Transition immediately so user experiences zero perceived navigation latency
        SelectedAction = action;
        State = ConvertWorkflowState.ActionPage;

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
        if (action.Command == PrintPdfCommand)
            PrintPreview.Update(SelectedFiles.Select(f => f.Path).ToList(), Options);
        Options.SetAllowedImageFormats(_workspace.IsCommandCompatible);
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
        var files = (IsOrderAction ? Order.GetOrderedFiles() : SelectedFiles.Select(f => f.Path)).ToArray();
        _runningTotal = files.Length;
        _pauseRequested = false;
        _fileStepsReported = false;
        State = ConvertWorkflowState.Executing;
        ProgressPercent = 0;
        ProgressMessage = Strings.T("ui_run_preparing");
        ResultItems.Clear();

        _conversionCts = new CancellationTokenSource();
        var controller = new ConversionExecutionController();
        MarkFileSteps(files, resumeStartIndex);

        var (command, options, renamed) = ResolveExecutionParameters(SelectedAction.Command);

        try
        {
            IReadOnlyList<string> plannedOutputs = controller.Prepare(command, files, options, existingTaskId, resumeStartIndex).Outputs;
            Changed?.Invoke(this, EventArgs.Empty);

            var progress = CreateProgressReporter();
            var callbacks = CreateExecutionCallbacks(files, resumeStartIndex);
            var result = await Task.Run(() => controller.ExecutePreparedAsync(callbacks, progress, _conversionCts.Token), _conversionCts.Token);

            HandleExecutionResult(result, files, plannedOutputs, renamed, resumeStartIndex);
        }
        catch (OperationCanceledException)
        {
            State = ConvertWorkflowState.ActionPage;
            ProgressMessage = Strings.T("ui_run_stopped");
        }
        catch (Exception ex)
        {
            State = ConvertWorkflowState.Completed;
            ResultSummary = Strings.T("ui_result_not_completed_2");
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
                    ? Strings.F("ui_run_processing_n_of_total", ("current", report.Current), ("total", report.Total))
                    : report.Message;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private ConversionExecutionCallbacks CreateExecutionCallbacks(string[] files, int resumeStartIndex)
    {
        return new ConversionExecutionCallbacks(
            PasswordResultPrompt: async (index, file, isWrong, ct) => await PromptPasswordResultAsync(index, file, isWrong, ct),
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
                    throw ConversionExecutionController.CreateParkSignal(Strings.T("ui_run_paused"), index);
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

    private async Task<PasswordPromptResult> PromptPasswordResultAsync(int index, string file, bool isWrong, CancellationToken ct)
    {
        string unlock = SelectedAction is null
            ? Strings.T("ui_password_unlock_and_continue")
            : Strings.F("ui_password_unlock_and_action", ("action", SelectedAction.Name));
        using (ct.Register(() => Dispatcher.UIThread.Post(() =>
        {
            foreach (FileItemViewModel f in SelectedFiles.Where(f => f.NeedsPassword)) f.CancelAll();
        })))
        {
            PasswordPromptResult promptResult = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FileItemViewModel? row = SelectedFiles.FirstOrDefault(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))
                                         ?? (index >= 0 && index < SelectedFiles.Count ? SelectedFiles[index] : null);
                return row is null ? Task.FromResult(PasswordPromptResult.Skip) : row.AskPasswordAsync(isWrong, unlock);
            });
            return promptResult;
        }
    }

    private void HandleExecutionResult(
        ConversionExecutionPresentationResult result,
        string[] files,
        IReadOnlyList<string> plannedOutputs,
        Dictionary<string, string>? renamed,
        int resumeStartIndex)
    {
        if (result.Status == ConversionExecutionPresentationStatus.Canceled)
        {
            State = ConvertWorkflowState.ActionPage;
            ProgressMessage = Strings.T("ui_run_stopped_2");
            return;
        }
        if (result.Status == ConversionExecutionPresentationStatus.Parked)
        {
            State = ConvertWorkflowState.Completed;
            int left = Math.Max(0, files.Length - _parkedAt);
            ResultSummary = Strings.F("ui_run_paused_remaining", ("count", left));
            foreach (string file in files.Take(_parkedAt).Skip(resumeStartIndex))
                ResultItems.Add(new ExecutionResultItemViewModel(file, true, Strings.T("ui_result_done")));
            return;
        }

        State = ConvertWorkflowState.Completed;
        ShowResults(result, files, plannedOutputs, renamed);
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
            if (at < 0) row.StatusText = Strings.T("ui_run_will_be_skipped");
            else if (at < current) row.StatusText = Strings.T("ui_result_done_2");
            else if (at == current) row.StatusText = Strings.T("ui_run_processing");
            else row.StatusText = Strings.T("ui_run_waiting");
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

        var skippedMap = result.SkippedFiles.ToDictionary(s => s.Path, s => s, StringComparer.OrdinalIgnoreCase);
        int succeeded = AppendProcessedResults(result, files, plannedOutputs, skippedMap, error);
        int skipped = AppendSkippedResults(files, skippedMap);
        int failed = files.Count - succeeded - skipped;

        ResultSummary = FormatResultSummary(files.Count, succeeded, skipped, failed);
    }

    private int AppendProcessedResults(
        ConversionExecutionPresentationResult result,
        IReadOnlyList<string> files,
        IReadOnlyList<string> plannedOutputs,
        Dictionary<string, SkippedFileInfo> skippedMap,
        string error)
    {
        bool ok = result.Status == ConversionExecutionPresentationStatus.Succeeded;
        bool perFile = plannedOutputs.Count == files.Count;
        int succeeded = 0;

        for (int i = 0; i < files.Count; i++)
        {
            string file = files[i];
            if (skippedMap.ContainsKey(file) || HasResultItem(file)) continue;

            bool fileOk = ok || (perFile && File.Exists(plannedOutputs[i]) && result.Outputs.Contains(plannedOutputs[i], StringComparer.OrdinalIgnoreCase));
            if (fileOk)
            {
                succeeded++;
                bool hadPassword = ProtectionOf(file) == PdfProtection.OpenPassword;
                string detail = hadPassword ? Strings.T("ui_result_completed_no_password") : Strings.T("ui_result_done");
                ResultItems.Add(new ExecutionResultItemViewModel(file, true, detail));
            }
            else
            {
                string fallback = error.Length > 0 ? error : Strings.T("ui_result_not_completed");
                ResultItems.Add(new ExecutionResultItemViewModel(file, false, fallback));
            }
        }

        return succeeded;
    }

    private int AppendSkippedResults(
        IReadOnlyList<string> files,
        Dictionary<string, SkippedFileInfo> skippedMap)
    {
        int skipped = 0;
        for (int i = 0; i < files.Count; i++)
        {
            string file = files[i];
            if (!skippedMap.TryGetValue(file, out SkippedFileInfo skipInfo) || HasResultItem(file)) continue;

            skipped++;
            string detail = skipInfo.Reason == ConversionSkipReason.NotEncrypted
                ? Strings.T("pdf_not_encrypted")
                : Strings.T("ui_result_skipped_needs_password");
            bool isPasswordSkip = skipInfo.Reason != ConversionSkipReason.NotEncrypted;
            ResultItems.Add(new ExecutionResultItemViewModel(file, false, detail, needsPassword: isPasswordSkip, isSkipped: true));
        }
        return skipped;
    }

    private bool HasResultItem(string file) =>
        ResultItems.Any(r => string.Equals(r.FullPath, file, StringComparison.OrdinalIgnoreCase));

    private static string FormatResultSummary(int totalFiles, int succeeded, int skipped, int failed)
    {
        if (succeeded == 0 && skipped > 0 && failed == 0)
        {
            return Strings.T("core_pdf_all_files_need_password");
        }
        if (skipped > 0)
        {
            return Strings.F("ui_result_summary_done_and_skipped", ("done", succeeded), ("skipped", skipped));
        }
        if (failed > 0)
        {
            return Strings.F("ui_result_done_some_failed", ("done", succeeded), ("failed", failed));
        }
        return Strings.Count("ui_result_done_count", totalFiles);
    }

    private void ShowRenameResults(ConversionExecutionPresentationResult result, Dictionary<string, string> renamed, string error)
    {
        bool ok = result.Status == ConversionExecutionPresentationStatus.Succeeded;
        var done = result.Outputs.Select(Path.GetFileName).Where(n => n is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (newName, oldName) in renamed)
        {
            bool fileOk = done.Contains(newName);
            string fallback = error.Length > 0 ? error : Strings.T("ui_result_not_renamed");
            string detail = fileOk ? Strings.F("ui_result_was_name", ("name", oldName)) : fallback;
            ResultItems.Add(new ExecutionResultItemViewModel(newName, fileOk, detail));
        }
        int count = ResultItems.Count(r => r.Succeeded);
        ResultSummary = ok
            ? Strings.Count("ui_result_renamed_count", count)
            : Strings.F("ui_result_renamed_some", ("done", count), ("unchanged", renamed.Count - count));
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

    public void RerunSkipped(ExecutionResultItemViewModel? item = null)
    {
        if (SelectedAction is null) return;
        List<string> rerunFiles = ResultItems.Where(r => r.IsSkipped).Select(r => r.FullPath).Distinct().ToList();
        if (rerunFiles.Count == 0 && item is not null) rerunFiles.Add(item.FullPath);
        if (rerunFiles.Count > 0)
        {
            PrepareRerun(SelectedAction.Command, rerunFiles);
        }
    }

    /// <summary>▶ 繼續: runs a parked task again from the first file that was not done.</summary>
    public async Task ResumeParkedAsync(ParkedTaskResumePresentation task)
    {
        if (IsExecuting)
        {
            // The task was claimed; return it to parked rather than losing it when another conversion runs.
            try { new HistoryPresentationController().ReturnToParked(task.TaskId, Strings.T("ui_history_parked_busy_with_other_files_resume"), task.StartIndex); }
            catch { /* best effort: the history page shows whatever storage holds */ }
            return;
        }
        ResetToStart();
        _workspace.AddFiles(task.Files);
        SyncFilesFromWorkspace();
        UpdateAvailableActions();
        State = ConvertWorkflowState.ActionCatalog;
        ActionItemViewModel? action = FindAction(task.Command);
        if (action is null)
        {
            // The task is already out of 待繼續; put it back rather than lose it silently.
            ResetToStart();
            try { new HistoryPresentationController().ReturnToParked(task.TaskId, Strings.T("ui_history_parked_cannot_run_here"), task.StartIndex); }
            catch { /* best effort: the history page shows whatever storage holds */ }
            return;
        }
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
        if (IsPrintAction)
        {
            PrintPreview.Update(SelectedFiles.Select(f => f.Path).ToList(), Options);
        }
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

        OnPropertyChanged(nameof(OnlyRename));
        OnPropertyChanged(nameof(OnlyRenameText));
        OnPropertyChanged(nameof(HasProtectedPdf));
        OnPropertyChanged(nameof(HasProtectedPdfNotice));
        OnPropertyChanged(nameof(ProtectedPdfText));
    }

    // ----- 有密碼或使用限制的 PDF (spec 4.1) -----

    private const string DecryptCommand = "decrypt-pdf";
    private enum PdfProtection { None, Restricted, OpenPassword }
    private readonly Dictionary<string, PdfProtection> _protection = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _protectionCts;

    private PdfProtection ProtectionOf(string path) =>
        _protection.TryGetValue(path, out PdfProtection value) ? value : PdfProtection.None;

    private List<FileItemViewModel> GetProtectedFiles() =>
        SelectedFiles.Where(f => ProtectionOf(f.Path) != PdfProtection.None).ToList();

    public bool HasProtectedPdf => SelectedFiles.Any(f => ProtectionOf(f.Path) != PdfProtection.None);

    /// <summary>Some files need their open password typed on the decrypt card; restricted ones need none.</summary>
    public bool ProtectedNeedsPassword => SelectedFiles.Any(f => ProtectionOf(f.Path) == PdfProtection.OpenPassword);

    public bool HasProtectedPdfNotice => ProtectedPdfText.Length > 0;

    /// <summary>One sentence above the cards: what is protected.</summary>
    public string ProtectedPdfText => BuildProtectedPdfText();

    private string BuildProtectedPdfText()
    {
        var openFiles = SelectedFiles.Where(f => ProtectionOf(f.Path) == PdfProtection.OpenPassword).ToList();
        if (openFiles.Count == 0) return "";
        if (openFiles.Count == 1)
        {
            return Strings.T("ui_notice_protected_one_password");
        }
        return Strings.F("ui_notice_protected_some",
            ("count", openFiles.Count),
            ("names", Strings.ShortList(openFiles.Select(f => f.Name).ToList())));
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
        catch (Exception) // skipcq: CS-R1008
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

    private static IEnumerable<string> SafeEnumerateFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .Where(f => (File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception) // skipcq: CS-R1008
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
            return Strings.T("ui_notice_these_files_are_different_types");
        string kind = KindName(unknown);
        bool mixed = unknown.Count < SelectedFiles.Select(f => f.Extension).Distinct().Count();
        return Strings.F(mixed ? "ui_notice_some_unsupported" : "ui_notice_all_unsupported", ("kind", kind));
    }

    /// <summary>影片／音訊／壓縮檔 when all unknown extensions are one such kind; otherwise 「這種檔案（.xyz）」.</summary>
    public static string KindName(IReadOnlyCollection<string> extensions)
    {
        var kinds = extensions.Select(KindOf).Distinct().ToList();
        if (kinds.Count == 1 && kinds[0] is not null) return kinds[0]!;
        return extensions.Count == 1
            ? Strings.F("ui_notice_kind_single", ("ext", extensions.First()))
            : Strings.F("ui_notice_kind_many", ("exts", Strings.List(extensions)));
    }

    private static string? KindOf(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".m4v" or ".webm" or ".flv" => Strings.T("ui_notice_videos"),
        ".mp3" or ".wav" or ".m4a" or ".flac" or ".aac" or ".ogg" or ".wma" => Strings.T("ui_notice_audio"),
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => Strings.T("ui_notice_archives"),
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
        // Only the kinds of file; how many of each says nothing about the feature (2026-11-20).
        var groups = SelectedFiles.Select(f => f.Extension.Length > 0 ? f.Extension : Strings.T("ui_suggest_no_extension"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string types = groups.Count > 0 ? Strings.List(groups) : Strings.T("ui_suggest_no_files");
        var subjectTypes = SelectedFiles.Select(f => f.Extension).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string subject = subjectTypes.Count > 0
            ? Strings.F("ui_suggest_subject_types", ("types", Strings.List(subjectTypes)))
            : Strings.T("ui_suggest_subject");
        string body = Strings.F("ui_suggest_body", ("types", types), ("version", ExternalOpen.AppVersion));
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
            var feed = HistoryFeed.Load(Strings.Language, completedLimit: 3);
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
