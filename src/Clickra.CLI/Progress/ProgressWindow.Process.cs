using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public partial class ProgressWindow
    {
        private const string ProgressAllDoneKey = "cli_progress_all_done";
        private const string UserAbortedMessage = "User Aborted";

        private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
        {
            public void Report(T value) => callback(value);
        }

        private sealed class DecryptExecutionObserver(
            ProgressWindow owner,
            IReadOnlyList<string> files,
            Action<int, int, string> progressCallback) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) => owner.TaskId = taskId;

            public void OnFileStarting(int fileIndex) =>
                progressCallback(
                    (fileIndex * 100) + 10,
                    files.Count * 100,
                    Loc("cli_progress_decrypting_pdf", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count));
        }

        private sealed class SplitExecutionObserver(
            ProgressWindow owner,
            IReadOnlyList<string> files,
            Action<int, int, string> progressCallback) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) => owner.TaskId = taskId;

            public void OnFileStarting(int fileIndex) =>
                progressCallback(
                    (fileIndex * 100) + 10,
                    files.Count * 100,
                    Loc("cli_progress_splitting_pdf", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count));
        }

        private sealed class CompressExecutionObserver(
            ProgressWindow owner,
            IReadOnlyList<string> files,
            Action<int, int, string> progressCallback) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) => owner.TaskId = taskId;

            public void OnFileStarting(int fileIndex) =>
                progressCallback(
                    (fileIndex * 100) + 10,
                    files.Count * 100,
                    Loc("cli_progress_compressing_pdf", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count));
        }

        private sealed class Img2PdfExecutionObserver(
            ProgressWindow owner,
            IReadOnlyList<string> files,
            Action<int, int, string> progressCallback) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) => owner.TaskId = taskId;

            public void OnFileStarting(int fileIndex) =>
                progressCallback(
                    (fileIndex * 100) + 50,
                    files.Count * 100,
                    Loc("cli_progress_converting_image", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count));
        }

        private sealed class TaskIdExecutionObserver(ProgressWindow owner) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) => owner.TaskId = taskId;

            public void OnFileStarting(int fileIndex) { }
        }

        private string? _inputPassword = null;
        private bool _passwordCancelled = false;
        private volatile bool _isPromptingPassword = false;
        private string _passwordPromptFilename = "";
        private bool _passwordPromptIsRetry = false;

        /// <summary>Runs the command on the background thread, driving the progress callback,
        /// password prompts and the visual splitter, then closes the window and records the
        /// outcome in the persistent history.</summary>
        // skipcq: CS-R1140
        private void RunProcessing(IntPtr hwnd)
        {
            List<string> currentFiles = new List<string>();
            string cmd = "";
            ConversionTaskLifecycle? lifecycle = null;
            try
            {
                lock (_stateLock)
                {
                    currentFiles = _files ?? new List<string>();
                    cmd = _command;
                }

                if (currentFiles.Count == 0)
                {
                    lock (_stateLock) { _completed = true; _message = Loc("cli_progress_no_files"); }
                    PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
                    Thread.Sleep(1000);
                    PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                    return;
                }

                Action<int, int, string> progressCallback = (curr, tot, msg) =>
                {
                    lock (_stateLock)
                    {
                        _current = curr;
                        if (tot > 0) _total = tot;
                        _message = msg;
                        if (_total > 0) _targetWidth = 448.0 * _current / _total;
                    }
                    UpdateTrayIconProgress();
                };

                if (TryRunMigratedApplicationCommand(hwnd, cmd, currentFiles, progressCallback))
                    return;

                // 立即建立 Pending 任務紀錄，讓 Dashboard 可即時看到；每個任務有
                // 獨立的進度檔（tasks/task-{id}.tmp），並行任務不會互相覆蓋。
                // resume 時沿用原任務檔（_existingTaskId），避免重複建立與歷史重複寫入。
                lifecycle = ConversionTaskLifecycle.Start(cmd, currentFiles, _existingTaskId, bestEffort: true);
                TaskId = lifecycle.TaskId;

                List<string> plannedOutputs = ConvertCommandRegistry.EstimateOutputs(cmd, currentFiles, _outputDirOverride);
                switch (cmd)
                {
                    case "translate-pdf":
                        RunTranslatePdf(currentFiles, plannedOutputs, progressCallback);
                        break;
                    default:
                        RunSharedCommand(cmd, currentFiles, plannedOutputs, progressCallback);
                        break;
                }

                string endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string outputs = string.Join(";", plannedOutputs);

                lock (_stateLock)
                {
                    _completed = true;
                    _message = Loc(ProgressAllDoneKey);
                }
                PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);

                lifecycle.CompleteSuccess(outputs, endTime);

                ShowToastNotification(cmd, currentFiles.Count);

                Thread.Sleep(1500);
                lifecycle.Delete();
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
            catch (Exception ex)
            {
                string endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string outputs = currentFiles.Count > 0 ? GetOutputPathForError(cmd, currentFiles, _outputDirOverride) : "";

                bool wasCanceled = _cts.IsCancellationRequested || ex is OperationCanceledException;
                string errorMsg = wasCanceled ? UserAbortedMessage : ex.Message;

                lock (_stateLock)
                {
                    _hasError = true;
                    _errorMessage = errorMsg;
                }
                PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);

                lifecycle?.CompleteFailure(errorMsg, outputs, endTime);
                lifecycle?.Delete();
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
        }

        private void RunApplicationDecrypt(
            IntPtr hwnd,
            List<string> files,
            Action<int, int, string> progressCallback)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(DecryptPdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                DecryptPdfUseCase.CommandName,
                files,
                ExistingTaskId: _existingTaskId,
                OutputOverride: _outputDirOverride,
                BestEffortTaskPersistence: true)) with
            {
                ResumeStartIndex = _startIndex
            };
            var interaction = new DelegateConversionInteraction(
                (index, inputPath, isRetry, token) =>
                    System.Threading.Tasks.Task.FromResult<string?>(ResolveDecryptPassword(hwnd, inputPath, isRetry)),
                (_, _, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                (_, _, _) => System.Threading.Tasks.Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = CreatePdfApplicationProgress(
                files,
                progressCallback,
                "cli_progress_decrypting_pdf_stage");

            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    interaction,
                    progress,
                    new DecryptExecutionObserver(this, files, progressCallback),
                    _cts.Token)
                .GetAwaiter()
                .GetResult();

            CompleteApplicationResult(
                hwnd,
                files,
                progressCallback,
                result,
                DecryptPdfUseCase.CommandName,
                Loc("cli_progress_decrypting_pdf_saving"),
                setAllDoneMessage: true);
        }

        private void RunApplicationSplit(
            IntPtr hwnd,
            List<string> files,
            Action<int, int, string> progressCallback)
        {
            string pagesOption = GetSplitPagesOptionFromCommandLine();
            IConversionUseCase useCase = ConversionUseCases.GetRequired(SplitPdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                SplitPdfUseCase.CommandName,
                files,
                new Dictionary<string, object>
                {
                    [SplitPdfUseCase.PagesOptionKey] = pagesOption
                },
                ExistingTaskId: _existingTaskId,
                OutputOverride: _outputDirOverride,
                BestEffortTaskPersistence: true)) with
            {
                ResumeStartIndex = _startIndex
            };
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                (index, inputPath, token) =>
                    System.Threading.Tasks.Task.FromResult<string?>(PromptVisualSplitPages(hwnd, inputPath)),
                (_, _, _) => System.Threading.Tasks.Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = CreatePdfApplicationProgress(
                files,
                progressCallback,
                "cli_progress_splitting_pdf_stage");

            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    interaction,
                    progress,
                    new SplitExecutionObserver(this, files, progressCallback),
                    _cts.Token)
                .GetAwaiter()
                .GetResult();

            CompleteApplicationResult(
                hwnd,
                files,
                progressCallback,
                result,
                SplitPdfUseCase.CommandName,
                Loc("cli_progress_splitting_pdf_done"),
                setAllDoneMessage: true);
        }

        private void RunApplicationCompress(
            IntPtr hwnd,
            List<string> files,
            Action<int, int, string> progressCallback)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(CompressPdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                CompressPdfUseCase.CommandName,
                files,
                ExistingTaskId: _existingTaskId,
                BestEffortTaskPersistence: true)) with
            {
                ResumeStartIndex = _startIndex
            };
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                (_, _, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                (_, _, _) => System.Threading.Tasks.Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            string compressionSummary = "";
            var progress = CreatePdfApplicationProgress(
                files,
                progressCallback,
                "cli_progress_compressing_pdf_stage",
                (fileProgress, message) =>
                {
                    if (fileProgress >= 100 && !string.IsNullOrWhiteSpace(message))
                        compressionSummary = message;
                });

            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    interaction,
                    progress,
                    new CompressExecutionObserver(this, files, progressCallback),
                    _cts.Token)
                .GetAwaiter()
                .GetResult();

            CompleteApplicationResult(
                hwnd,
                files,
                progressCallback,
                result,
                CompressPdfUseCase.CommandName,
                string.IsNullOrWhiteSpace(compressionSummary)
                    ? Loc("cli_progress_compressing_pdf_done")
                    : compressionSummary,
                setAllDoneMessage: false);
        }

        private static CallbackProgress<ConversionProgress> CreatePdfApplicationProgress(
            List<string> files,
            Action<int, int, string> progressCallback,
            string stageLocalizationKey,
            Action<int, string>? observeFileProgress = null) =>
            new(state =>
            {
                int fileIndex = Math.Clamp((Math.Max(state.Current, 1) - 1) / 100, 0, files.Count - 1);
                int fileProgress = Math.Clamp(state.Current - (fileIndex * 100), 0, 100);
                int progressPct = (int)(fileProgress * 0.8) + 10;
                observeFileProgress?.Invoke(fileProgress, state.Message);
                progressCallback(
                    (fileIndex * 100) + progressPct,
                    files.Count * 100,
                    Loc(stageLocalizationKey, state.Message, fileIndex + 1, files.Count));
            });

        private void CompleteApplicationResult(
            IntPtr hwnd,
            List<string> files,
            Action<int, int, string> progressCallback,
            ConversionResult result,
            string command,
            string? successMessage,
            bool setAllDoneMessage)
        {
            if (result.Status == ConversionResultStatus.Succeeded)
            {
                if (successMessage is not null)
                    progressCallback(files.Count * 100, files.Count * 100, successMessage);
                lock (_stateLock)
                {
                    _completed = true;
                    if (setAllDoneMessage)
                        _message = Loc(ProgressAllDoneKey);
                }
                PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
                ShowToastNotification(command, files.Count);
                Thread.Sleep(1500);
                ConversionTaskCleanup.Delete(result.TaskId, bestEffort: true);
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                return;
            }

            string errorMsg = result.Status == ConversionResultStatus.Canceled
                ? UserAbortedMessage
                : result.Error ?? "";
            lock (_stateLock)
            {
                _hasError = true;
                _errorMessage = errorMsg;
            }
            PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
            ConversionTaskCleanup.Delete(result.TaskId, bestEffort: true);
            PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
        }

        private bool TryRunMigratedApplicationCommand(
            IntPtr hwnd,
            string command,
            List<string> files,
            Action<int, int, string> progressCallback)
        {
            if (command.Equals(DecryptPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                RunApplicationDecrypt(hwnd, files, progressCallback);
                return true;
            }
            if (command.Equals(SplitPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                RunApplicationSplit(hwnd, files, progressCallback);
                return true;
            }
            if (command.Equals(CompressPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                RunApplicationCompress(hwnd, files, progressCallback);
                return true;
            }
            if (command.Equals(Img2PdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                RunSimpleApplicationCommand(
                    hwnd, command, files, progressCallback, outputOverride: null,
                    resume: true,
                    observer: new Img2PdfExecutionObserver(this, files, progressCallback),
                    successMessage: Loc("cli_progress_converting_image_saving"));
                return true;
            }
            if (command.Equals(MergePdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)
                || command.Equals(ImgMergeUseCase.CommandName, StringComparison.OrdinalIgnoreCase)
                || command.Equals(ImgStitchUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                RunSimpleApplicationCommand(
                    hwnd, command, files, progressCallback, outputOverride: null,
                    resume: false,
                    observer: new TaskIdExecutionObserver(this));
                return true;
            }
            if (command.Equals(ImgCompressUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                RunSimpleApplicationCommand(
                    hwnd, command, files, progressCallback, _outputDirOverride,
                    resume: true,
                    observer: new TaskIdExecutionObserver(this));
                return true;
            }
            if (ConversionUseCases.TryGet(command, out IConversionUseCase? useCase)
                && useCase is ImageFormatConvertUseCase)
            {
                RunSimpleApplicationCommand(
                    hwnd, command, files, progressCallback, _outputDirOverride,
                    resume: true,
                    observer: new TaskIdExecutionObserver(this));
                return true;
            }
            return false;
        }

        private void RunSimpleApplicationCommand(
            IntPtr hwnd,
            string command,
            List<string> files,
            Action<int, int, string> progressCallback,
            string? outputOverride,
            bool resume,
            IConversionExecutionObserver observer,
            string? successMessage = null)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(command);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                command,
                files,
                ExistingTaskId: _existingTaskId,
                OutputOverride: outputOverride,
                BestEffortTaskPersistence: true));
            if (resume)
                plan = plan with { ResumeStartIndex = _startIndex };

            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                (_, _, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                (_, _, _) => System.Threading.Tasks.Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            IProgress<ConversionProgress>? progress = command == Img2PdfUseCase.CommandName
                ? null
                : new CallbackProgress<ConversionProgress>(state =>
                    progressCallback(state.Current, state.Total, state.Message));

            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    interaction,
                    progress,
                    observer,
                    _cts.Token)
                .GetAwaiter()
                .GetResult();

            CompleteApplicationResult(
                hwnd,
                files,
                progressCallback,
                result,
                command,
                successMessage,
                setAllDoneMessage: true);
        }

        /// <summary>Runs commands that do not need Native-only progress or prompt handling through
        /// the shared Core dispatcher. Native-specific cases remain explicit in RunProcessing.</summary>
        private void RunSharedCommand(string command, List<string> files, List<string> outputs, Action<int, int, string> progressCallback)
        {
            ConvertCommandRunner.Run(
                command,
                files,
                outputs,
                progressCallback,
                new ConvertCommandRunner.ConversionOptions(
                    _ => System.Threading.Tasks.Task.FromResult<string?>(null),
                    (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                    _startIndex,
                    CommandOptions: _commandOptions,
                    OnFileStarting: TryRecordTaskIndex),
                _cts.Token);
        }
        /// <summary>Translates each PDF to the saved target language, reporting per-file
        /// progress through the callback.</summary>
        private void RunTranslatePdf(List<string> files, List<string> outputs, Action<int, int, string> progressCallback)
        {
            string targetLang = ClickraStorage.GetSetting(ClickraSettings.TranslateTargetLang);
            for (int i = _startIndex; i < files.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                TryRecordTaskIndex(i);
                var f = files[i];
                progressCallback((i * 100) + 10, files.Count * 100, Loc("cli_progress_translating_pdf", Path.GetFileName(f), i + 1, files.Count));
                FileProcessor.TranslatePdf(f, outputs[i], targetLang, (curr, tot, msg) => {
                    int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                    progressCallback((i * 100) + progressPct, files.Count * 100, Loc("cli_progress_translating_pdf_stage", msg, i + 1, files.Count));
                }, _cts.Token);
            }
            _cts.Token.ThrowIfCancellationRequested();
            progressCallback(files.Count * 100, files.Count * 100, Loc("cli_progress_translating_pdf_saving"));
        }

        /// <summary>Reads the --pages / -p page-range option from the command line.</summary>
        private static string GetSplitPagesOptionFromCommandLine()
        {
            string pagesOption = "prompt";
            var cliArgs = Environment.GetCommandLineArgs();
            for (int a = 0; a < cliArgs.Length - 1; a++)
            {
                if (cliArgs[a].Equals("--pages", StringComparison.OrdinalIgnoreCase) || cliArgs[a].Equals("-p", StringComparison.OrdinalIgnoreCase))
                {
                    pagesOption = cliArgs[a + 1];
                    break;
                }
            }
            return pagesOption;
        }

        /// <summary>Launches the Native visual splitter and returns the selected page specification.</summary>
        private string PromptVisualSplitPages(IntPtr hwnd, string filePath)
        {
            lock (_stateLock)
            {
                _isPromptingPassword = true;
                _isPromptingVisualSplitter = true;
                _passwordPromptFilename = filePath;
                _passwordPromptIsRetry = false;
                _inputPassword = null;
                _passwordCancelled = false;
                InitializeVisualSplitter(filePath);
            }

            PostMessageW(hwnd, WM_USER_SHOW_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
            _passwordEvent.WaitOne();

            bool cancelled = false;
            string targetPages;
            lock (_stateLock)
            {
                cancelled = _passwordCancelled;
                targetPages = string.IsNullOrWhiteSpace(_inputPassword) ? BuildVisualSplitSpec() : _inputPassword.Trim();
                _isPromptingPassword = false;
                _isPromptingVisualSplitter = false;
            }

            if (cancelled)
            {
                throw new OperationCanceledException(Loc("cli_progress_pages_input_canceled"));
            }
            return targetPages;
        }

        /// <summary>Shows the password prompt and waits for the user's input, throwing
        /// OperationCanceledException when the prompt is cancelled.</summary>
        private string ResolveDecryptPassword(IntPtr hwnd, string filePath, bool isRetry)
        {
            lock (_stateLock)
            {
                _isPromptingPassword = true;
                _passwordPromptFilename = filePath;
                _passwordPromptIsRetry = isRetry;
                _inputPassword = null;
                _passwordCancelled = false;
            }

            PostMessageW(hwnd, WM_USER_SHOW_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
            _passwordEvent.WaitOne();

            // The prompt window writes these fields on the UI thread while this
            // thread waits on _passwordEvent, so read them through Volatile.Read.
            bool cancelled = Volatile.Read(ref _passwordCancelled);
            string? input = Volatile.Read(ref _inputPassword);
            lock (_stateLock)
            {
                _isPromptingPassword = false;
            }

            if (cancelled)
            {
                throw new OperationCanceledException(Localization.T("error_user_aborted", ClickraStorage.GetSetting(ClickraSettings.Language)));
            }
            return input ?? "";
        }

        /// <summary>Best-effort output-path rendering for failure history. Validation failures can
        /// originate inside output planning itself, so error logging must never invoke the same
        /// failing planner and mask the original exception or skip task cleanup.</summary>
        private static string GetOutputPathForError(string cmd, List<string> inputFiles, string? outputDirOverride)
        {
            try
            {
                return string.Join(";", ConvertCommandRegistry.EstimateOutputs(cmd, inputFiles, outputDirOverride));
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Shows a Windows toast notification on success, unless notifications are disabled.</summary>
        private void ShowToastNotification(string command, int count)
        {
            if (!ClickraStorage.GetSettingBool(ClickraSettings.Notification))
                return;

            try
            {
                string title = Loc("cli_progress_toast_title");
                string body = Loc("cli_progress_toast_body", Loc(ConvertCommandRegistry.GetLabelKey(command)), count);
                
                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
    $textNodes = $template.GetElementsByTagName('text')
    $textNodes.Item(0).AppendChild($template.CreateTextNode('{title.Replace("'", "''").Replace("`", "``").Replace("\"", "`\"")}')) | Out-Null
    $textNodes.Item(1).AppendChild($template.CreateTextNode('{body.Replace("'", "''").Replace("`", "``").Replace("\"", "`\"")}')) | Out-Null
    $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Clickra')
    $notifier.Show($toast)
}} catch {{
    # Ignore toast failures
}}";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(startInfo);
                p?.WaitForExit();
            }
            catch
            {
                // Toasts are optional UI feedback; notification failures must not fail a completed conversion.
            }
        }

        private void TryRecordTaskIndex(int index)
        {
            try
            {
                ClickraStorage.SetActiveRecordIndex(index);
            }
            catch
            {
                // Legacy active-record progress is best effort and must not abort file processing.
            }

            if (string.IsNullOrEmpty(TaskId)) return;
            try
            {
                ClickraStorage.SetTaskIndex(TaskId, index);
            }
            catch
            {
                // Per-task progress persistence is best effort; conversion output remains authoritative.
            }
        }
    }
}
