using Clickra.Core;
using Clickra.Core.Processors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;
using System.Threading;

namespace Clickra_Fluent;

/// <summary>
/// 統一轉換視窗（IDM 模型）：所有轉換——無論從右鍵、dashboard 或暫存恢復發起——
/// 都在這個視窗執行。關窗行為集中在 App.TrackWindow 決定：轉換中關窗 → 縮到系統匣
/// 繼續；卡在輸入 prompt 關窗 → 暫存（可從 dashboard 繼續或取消）；閒置關窗 → 退出。
/// </summary>
public sealed partial class TaskProgressPage : Page
{
    /// <summary>一般轉換的視窗尺寸：緊湊單一區塊排版（~460px 寬內容），高度剛好包住
    /// 狀態列+進度條+按鈕，不會有大窗漂小卡的空間感。分割介面需要整片空間時才暫時放大。</summary>
    internal static readonly Windows.Graphics.SizeInt32 CompactWindowSize = new(480, 300);

    /// <summary>分割預覽需要的視窗尺寸（整頁 PDF 預覽 + 縮放工具列）。</summary>
    private static readonly Windows.Graphics.SizeInt32 SplitWindowSize = new(1150, 760);

    /// <summary>宿主視窗（獨立任務視窗時由建立者設定；主視窗流程時為 null）。</summary>
    public Window? HostWindow { get; set; }

    private CancellationTokenSource? _cts;
    private string _arguments = "";
    private string _outputFolder = "";
    private string _taskId = "";
    private string _taskLabel = "Clickra";
    private List<string> _files = new();
    private readonly Stopwatch _stopwatch = new();
    private bool _finished;
    private bool _isBackgrounded;
    private bool _parkRequested;
    private string _parkReason = "";
    private bool _promptActive;
    private ContentDialog? _activeDialog;

    /// <summary>分割介面顯示前的視窗尺寸（關閉後還原，尊重使用者手動調整）。</summary>
    private Windows.Graphics.SizeInt32 _preSplitSize;

    /// <summary>調整宿主視窗尺寸（分割介面放大 / 關閉還原）。</summary>
    private void ResizeHost(Windows.Graphics.SizeInt32 size)
    {
        var window = Window;
        if (window == null) return;
        try { window.AppWindow.Resize(size); } catch { /* 某些環境忽略 Resize，不影響功能。 */ }
    }

    /// <summary>轉換正在執行（未完成、未暫存）。</summary>
    internal bool IsConversionActive => _cts != null && !_finished;

    /// <summary>目前正卡在密碼/分割輸入（關窗時應暫存而非縮匣，避免隱形輸入卡死）。</summary>
    internal bool IsWaitingOnPrompt => _promptActive;

    /// <summary>匣選單 / tooltip 顯示的任務名稱。</summary>
    internal string TaskLabel => _taskLabel;

    /// <summary>目前任務的 task 檔 ID（dashboard 取消/繼續用它定位）。</summary>
    internal string TaskId => _taskId;

    private Window? Window => HostWindow ?? App.MainWindow;

    /// <summary>關閉承載本頁的視窗（獨立任務視窗關自己，主視窗流程關主視窗）。</summary>
    private void CloseHostWindow() => Window?.Close();

    public TaskProgressPage()
    {
        InitializeComponent();
        ApplyLanguage(TitleText, FileText, StateText, OpenFolderButton, CancelButton);
        CancelButton.Click += (_, _) => _cts?.Cancel();
        OpenFolderButton.Click += (_, _) => OpenOutputFolder();
        Unloaded += (_, _) => TrayService.Instance.RemoveBackgroundWindow(Window);
        Loaded += async (_, _) => await RunAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _arguments = e.Parameter as string ?? "";
    }

    private static string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));

    private static void ApplyLanguage(TextBlock titleText, TextBlock fileText, TextBlock stateText, Button openFolderButton, Button cancelButton)
    {
        titleText.Text = "";
        fileText.Text = "";
        stateText.Text = L("fluent_progress_preparing");
        openFolderButton.Content = L("fluent_progress_open_folder");
        cancelButton.Content = L("fluent_cancel");
    }

    private record ParseResult(string Command, List<string> Files, int StartIndex, string? ExistingTaskId);

    private ParseResult? TryParseArguments()
    {
        var args = ConvertCommandRegistry.SplitCommandLine(_arguments);
        bool isResume = args.Count > 0 && args[0].Equals("resume", StringComparison.OrdinalIgnoreCase);

        if (isResume) return TryParseResume(args);
        return TryParseFresh(args);
    }

    private ParseResult? TryParseResume(List<string> args)
    {
        if (args.Count < 2) return null;
        var parked = ClickraStorage.GetTask(args[1]);
        if (parked == null || parked.Value.Status != ConversionStatus.Parked) return null;
        var files = parked.Value.InputPaths.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (files.Count == 0) return null;
        int startIndex = Math.Clamp(parked.Value.CurrentIndex, 0, files.Count);
        return new ParseResult(parked.Value.Command, files, startIndex, args[1]);
    }

    private ParseResult? TryParseFresh(List<string> args)
    {
        if (args.Count < 2 || !ConvertCommandRegistry.IsKnownCommand(args[0])) return null;
        var files = ConvertCommandRegistry.ExpandDirectoryArguments(args[0], args.Skip(1)).Where(File.Exists).ToList();
        if (files.Count == 0) return null;
        return new ParseResult(args[0], files, 0, null);
    }

    private async Task RunAsync()
    {
        var parsed = TryParseArguments();
        if (parsed == null)
        {
            Complete(L("fluent_progress_invalid_command"), false);
            CancelButton.Click += (_, _) => CloseHostWindow();
            return;
        }
        string command = parsed.Command;
        List<string> files = parsed.Files;
        int startIndex = parsed.StartIndex;
        string? existingTaskId = parsed.ExistingTaskId;

        if (!OfficeEnginePreflight.TryValidate(command, L, out string preflightError))
        {
            Complete(preflightError, false);
            return;
        }

        var outputs = ConvertCommandRegistry.EstimateOutputs(command, files);
        _outputFolder = Path.GetDirectoryName(outputs[0]) ?? "";
        _files = files;
        _cts = new CancellationTokenSource();
        _taskLabel = $"{L(ConvertCommandRegistry.GetLabelKey(command))} - {Path.GetFileName(files[0])}";

        TitleText.Text = L(ConvertCommandRegistry.GetLabelKey(command));
        FileText.Text = files.Count > 1
            ? string.Format(L("fluent_progress_multiple_files"), Path.GetFileName(files[0]), files.Count)
            : Path.GetFileName(files[0]);
        StateText.Text = L("fluent_progress_preparing");
        PercentText.Text = "0%";
        ProgressBar.Value = 0;
        _stopwatch.Restart();

        // 建立（或沿用暫存的）任務檔並登記實例，供 dashboard 取消/查看定位。
        _taskId = existingTaskId ?? ClickraStorage.StartTask(command, files.Count, string.Join(";", files));
        App.RegisterTaskPage(_taskId, this);

        try
        {
            var result = await ConvertCommandRunner.RunTrackedAsync(command, files, outputs,
                (percent, message) =>
                {
                    if (_isBackgrounded && Window is { } window)
                    {
                        TrayService.Instance.UpdateProgress(window, _taskLabel, percent);
                    }
                    DispatcherQueue.TryEnqueue(() => SetProgress(percent, message));
                },
                PromptPasswordAsync,
                PromptSplitAsync,
                _cts.Token,
                startIndex: startIndex,
                // 一律沿用頁面建立的 task 檔（fresh 或 resume），避免 Core 重複建立產生孤兒任務。
                existingTaskId: _taskId);

            string statusMessage;
            bool success;
            string toastTitle;
            string toastBody;
            switch (result.Status)
            {
                case ConvertCommandRunner.ConvertRunStatus.Succeeded:
                    statusMessage = L("fluent_progress_completed");
                    success = true;
                    toastTitle = L("fluent_toast_done_title");
                    toastBody = string.Format(L("fluent_toast_done_body"), L(ConvertCommandRegistry.GetLabelKey(command)), files.Count);
                    break;
                case ConvertCommandRunner.ConvertRunStatus.Canceled:
                    // 使用者取消不是失敗：不秀錯誤畫面，直接關窗（歷史已記錄 Canceled）。
                    // 縮在匣內時先通知再關；前景時關窗本身就是回饋。
                    _finished = true;
                    if (_isBackgrounded)
                    {
                        TrayService.Instance.RemoveBackgroundWindow(Window);
                        ShowToast(L("fluent_toast_canceled_title"), string.Format(L("fluent_toast_canceled_body"), Path.GetFileName(files[0])));
                        await Task.Delay(1200);
                    }
                    CloseHostWindow();
                    return;
                case ConvertCommandRunner.ConvertRunStatus.Parked:
                    // 已暫存：不寫歷史，留待 dashboard「繼續 / 取消」。通知後自動關窗。
                    _finished = true;
                    TrayService.Instance.RemoveBackgroundWindow(Window);
                    ShowToast(L("fluent_park_toast_title"), L("fluent_park_toast_body"));
                    await Task.Delay(800);
                    CloseHostWindow();
                    return;
                default:
                    statusMessage = result.Error ?? "";
                    success = false;
                    toastTitle = L("fluent_toast_failed_title");
                    toastBody = statusMessage;
                    break;
            }

            Complete(statusMessage, success);
            _finished = true;

            if (_isBackgrounded)
            {
                // 縮在系統匣內時任務結束：移除匣圖示、跳出通知，稍候自動關窗——
                // 最後一個視窗關閉時程序隨之結束（TrackWindow）。
                TrayService.Instance.RemoveBackgroundWindow(Window);
                ShowToast(toastTitle, toastBody);
                await Task.Delay(1500);
                CloseHostWindow();
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            App.UnregisterTaskPage(_taskId);
        }
    }

    /// <summary>密碼 prompt：若已要求暫存則拋出 ParkedException，讓轉換乾淨停下來。</summary>
    private async Task<string?> PromptPasswordAsync(int fileIndex)
    {
        if (_parkRequested) throw new ConvertCommandRunner.ParkedException(_parkReason, fileIndex);
        _promptActive = true;
        try
        {
            var result = await DispatcherQueue.EnqueueAsync(() => FluentDialogs.PromptPasswordAsync(XamlRoot, L, d => _activeDialog = d));
            if (_parkRequested) throw new ConvertCommandRunner.ParkedException(_parkReason, fileIndex);
            return result;
        }
        finally
        {
            _promptActive = false;
            _activeDialog = null;
        }
    }

    /// <summary>分割頁數 prompt：同上，暫存時喚醒並拋出 ParkedException。</summary>
    private async Task<string?> PromptSplitAsync(int fileIndex, string pdfPath)
    {
        if (_parkRequested) throw new ConvertCommandRunner.ParkedException(_parkReason, fileIndex);
        _promptActive = true;
        try
        {
            // 分割介面是全窗覆蓋層且背景透明（讓 Mica 透出）：先把底下的進度卡收合，
            // 否則進度卡會從覆蓋層的空隙透出來，與分割介面疊字。
            // 分割預覽需要整片空間：暫時放大視窗，關閉後還原成緊湊尺寸。
            // 注意：本方法在背景執行緒被呼叫（RunSplit 在 Task.Run 內），
            // 所有 UI 存取（收合/還原/放大）都必須經由 DispatcherQueue。
            var result = await DispatcherQueue.EnqueueAsync(() =>
            {
                ProgressCardHost.Visibility = Visibility.Collapsed;
                _preSplitSize = Window?.AppWindow.Size ?? CompactWindowSize;
                ResizeHost(SplitWindowSize);
                return SplitOverlay.ShowForAsync(pdfPath);
            });
            if (_parkRequested) throw new ConvertCommandRunner.ParkedException(_parkReason, fileIndex);
            return result;
        }
        finally
        {
            _promptActive = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                ProgressCardHost.Visibility = Visibility.Visible;
                if (_preSplitSize.Width > 0) ResizeHost(_preSplitSize);
            });
        }
    }

    /// <summary>暫存目前的轉換：關閉卡住的 prompt，讓背景執行緒拋出 ParkedException（由 TrackWindow 在卡 prompt 關窗時呼叫）。</summary>
    internal void ParkAndClose()
    {
        if (_parkRequested) return;
        _parkRequested = true;
        _parkReason = L("fluent_task_parked_waiting");
        if (_activeDialog != null)
        {
            try { _activeDialog.Hide(); } catch { }
            _activeDialog = null;
        }
        SplitOverlay.Cancel();
    }

    /// <summary>取消目前的轉換（dashboard 任務紀錄的取消按鈕）。</summary>
    internal void RequestCancel() => _cts?.Cancel();

    /// <summary>標記為縮到匣背景執行（由 TrackWindow 在關窗縮匣時呼叫）。</summary>
    internal void MarkBackgrounded() => _isBackgrounded = true;

    /// <summary>標記已還原（由 TrayService 雙擊/選單還原或 dashboard 查看時呼叫）。</summary>
    internal void MarkRestored()
    {
        _isBackgrounded = false;
        TrayService.Instance.RemoveBackgroundWindow(Window);
    }

    /// <summary>還原視窗（dashboard 任務紀錄的查看按鈕）。</summary>
    internal void ShowWindow()
    {
        var window = Window;
        if (window == null) return;
        MarkRestored();
        window.AppWindow.Show();
        window.Activate();
    }

    /// <summary>更新進行中狀態（UI 執行緒）：大百分比、進度條、目前檔案與狀態行。</summary>
    private void SetProgress(int percent, string message)
    {
        ProgressBar.Value = percent;
        PercentText.Text = $"{percent}%";
        if (_files.Count > 1)
        {
            int fileIndex = Math.Clamp((int)((long)percent * _files.Count / 100), 0, _files.Count - 1);
            FileText.Text = Path.GetFileName(_files[fileIndex]);
            StateText.Text = string.Format(L("fluent_task_file_index"), fileIndex + 1, _files.Count)
                + (string.IsNullOrWhiteSpace(message) ? "" : $" · {message}");
        }
        else
        {
            StateText.Text = string.IsNullOrWhiteSpace(message) ? L("fluent_progress_processing") : message;
        }
    }

    /// <summary>結束狀態（UI 執行緒）：成功 = 綠色勾 + 100% + 摘要；失敗 = 紅色叉 + 錯誤訊息。</summary>
    private void Complete(string message, bool success)
    {
        _stopwatch.Stop();
        CancelButton.Content = L("fluent_progress_close");
        CancelButton.Click += (_, _) => CloseHostWindow();

        if (success)
        {
            PercentText.Visibility = Visibility.Visible;
            ProgressBar.Visibility = Visibility.Visible;
            StateText.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;

            ProgressBar.Value = 100;
            PercentText.Text = "100%";
            PercentText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            TitleText.Text = L("fluent_progress_done_title");
            StatusIcon.Glyph = "\uE73E";
            StatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            StateText.Text = string.Format(L("fluent_progress_done_summary"), _files.Count, _stopwatch.Elapsed.TotalSeconds.ToString("0.0"));
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        else
        {
            PercentText.Visibility = Visibility.Collapsed;
            ProgressBar.Visibility = Visibility.Collapsed;
            StateText.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Visible;

            TitleText.Text = L("fluent_progress_failed_title");
            StatusIcon.Glyph = "\uE783";
            StatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            ErrorText.Text = string.IsNullOrWhiteSpace(message) ? L("fluent_progress_failed_generic") : message;
            OpenFolderButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(_outputFolder) || !Directory.Exists(_outputFolder)) return;
        Process.Start(new ProcessStartInfo(Clickra.Core.SystemPaths.Explorer, $"\"{_outputFolder}\"") { UseShellExecute = true })?.Dispose();
    }

    /// <summary>任務結束的 Windows Toast 通知（與 MainPage 相同實作；Notification 關閉時略過）。</summary>
    private static void ShowToast(string title, string body)
    {
        if (ClickraStorage.GetSetting("Notification").Equals("false", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            static string Escape(string value) => value.Replace("'", "''").Replace("`", "``").Replace("\"", "`\"");
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode('{Escape(title)}')) | Out-Null
$textNodes.Item(1).AppendChild($template.CreateTextNode('{Escape(body)}')) | Out-Null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Clickra').Show($toast)";

            Process.Start(new ProcessStartInfo
            {
                FileName = Clickra.Core.SystemPaths.PowerShell,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
        }
        catch { /* Ignored: a failed toast must not break the conversion flow. */ }
    }

}
