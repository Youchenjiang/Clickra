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

public sealed class RecentHistoryItemViewModel(HistoryItem item) : ViewModelBase
{
    public string Title { get; } = item.FirstFileName;
    public string Subtitle { get; } = $"{item.Subtitle} · {item.Time}";
    public string CommandKey { get; } = item.Entry.Command ?? "";
}

public sealed class ConvertViewModel : ViewModelBase
{
    private readonly ConvertWorkspaceController _workspace = new();
    private ConvertWorkflowState _state = ConvertWorkflowState.DropZone;
    private ActionItemViewModel? _selectedAction;
    private CancellationTokenSource? _conversionCts;
    private double _progressPercent;
    private string _progressMessage = "";
    private string _resultSummary = "";
    private int _pdfCompressLevel = 1;
    private int _imageCompressLevel = 2;
    private string _imageTargetFormat = "PNG";
    private string _translationLanguage = "繁體中文";
    private string _customOutputPath = "";
    private bool _isCustomOutput;

    public ObservableCollection<FileItemViewModel> SelectedFiles { get; } = [];
    public ObservableCollection<ActionItemViewModel> AvailableActions { get; } = [];
    public ObservableCollection<ExecutionResultItemViewModel> ResultItems { get; } = [];
    public ObservableCollection<RecentHistoryItemViewModel> RecentItems { get; } = [];

    public ConvertViewModel()
    {
        RefreshRecentHistory();
        _pdfCompressLevel = Math.Clamp(SettingsController.PdfCompressionImageLevel, 0, 2);
        _imageCompressLevel = Math.Clamp(SettingsController.ImageCompressionLevel, 0, 3);
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
            OnPropertyChanged(nameof(CanStart));
        }
    }

    public string StartButtonText => SelectedAction is not null ? $"開始{SelectedAction.Name}" : "開始";

    public bool CanStart => !IsExecuting && SelectedAction is not null && _workspace.CanStart;

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
                _ => "個檔案"
            };
            return domain == "image" ? $"{count} {typeName}" : $"{count} 個 {typeName}";
        }
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

    public int PdfCompressLevel
    {
        get => _pdfCompressLevel;
        set { _pdfCompressLevel = value; OnPropertyChanged(); }
    }

    public int ImageCompressLevel
    {
        get => _imageCompressLevel;
        set { _imageCompressLevel = value; OnPropertyChanged(); }
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

    public string ImageTargetFormat
    {
        get => _imageTargetFormat;
        set { _imageTargetFormat = value; OnPropertyChanged(); }
    }

    public string TranslationLanguage
    {
        get => _translationLanguage;
        set { _translationLanguage = value; OnPropertyChanged(); }
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

    public async Task StartConversionAsync()
    {
        if (SelectedAction is null || !CanStart) return;

        State = ConvertWorkflowState.Executing;
        ProgressPercent = 0;
        ProgressMessage = "準備轉換中...";
        ResultItems.Clear();

        _conversionCts = new CancellationTokenSource();
        var controller = new ConversionExecutionController();
        string command = SelectedAction.Command;
        var files = SelectedFiles.Select(f => f.Path).ToArray();

        var options = new Dictionary<string, object>();
        if (command == "compress-pdf")
        {
            options["level"] = PdfCompressLevel;
        }
        else if (command == "img-compress")
        {
            options["level"] = ImageCompressLevel;
        }
        else if (command.StartsWith("img-to-", StringComparison.OrdinalIgnoreCase))
        {
            options["format"] = ImageTargetFormat;
        }

        try
        {
            controller.Prepare(command, files, options);

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
                });
            });

            var callbacks = new ConversionExecutionCallbacks(
                PasswordPrompt: async (index, file, isWrong, ct) =>
                {
                    return await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var resultItem = new ExecutionResultItemViewModel(file, succeeded: false, detail: isWrong ? "密碼不正確" : "需要輸入密碼", needsPassword: true);
                        ResultItems.Add(resultItem);
                        return (string?)null;
                    });
                });

            var result = await Task.Run(() => controller.ExecutePreparedAsync(callbacks, progress, _conversionCts.Token));

            State = ConvertWorkflowState.Completed;
            int total = files.Length;
            int succeeded = result.Outputs.Count;
            int failed = total - succeeded;

            ResultSummary = failed == 0
                ? $"{SelectedAction.Name}好 {total} 個檔案"
                : $"{SelectedAction.Name}好 {succeeded} 個，{failed} 個需要處理";

            foreach (string file in files)
            {
                if (!ResultItems.Any(r => string.Equals(r.FullPath, file, StringComparison.OrdinalIgnoreCase)))
                {
                    bool fileOk = result.Outputs.Any(o => o.Contains(System.IO.Path.GetFileNameWithoutExtension(file)));
                    ResultItems.Add(new ExecutionResultItemViewModel(file, fileOk, fileOk ? "處理完成" : "未產生輸出檔案"));
                }
            }
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
    }

    public void CancelConversion()
    {
        _conversionCts?.Cancel();
    }

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
            if (_workspace.IsCommandCompatible(action.Command))
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
    }

    private void RefreshRecentHistory()
    {
        RecentItems.Clear();
        try
        {
            var feed = HistoryFeed.Load(SettingsController.Language, completedLimit: 3);
            foreach (var item in feed.OfKind(HistoryItemKind.Completed).Take(3))
            {
                RecentItems.Add(new RecentHistoryItemViewModel(item));
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
            // Best effort process launching
        }
    }
}
