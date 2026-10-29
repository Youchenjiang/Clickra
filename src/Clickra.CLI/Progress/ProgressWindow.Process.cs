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
            List<string> currentFiles;
            string cmd;
            try
            {
                lock (_stateLock)
                {
                    currentFiles = _files ?? new List<string>();
                    cmd = _command;
                }

                if (currentFiles.Count == 0)
                {
                    _progressPresentation.Succeed(Loc("cli_progress_no_files"));
                    PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
                    Thread.Sleep(1000);
                    PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                    return;
                }

                Action<int, int, string> progressCallback = (curr, tot, msg) =>
                {
                    _progressPresentation.Report(curr, tot, msg);
                    lock (_stateLock)
                    {
                        TaskProgressPresentationState state = _progressPresentation.State;
                        if (state.Total > 0) _targetWidth = 448.0 * state.Current / state.Total;
                    }
                    UpdateTrayIconProgress();
                };

                var adapter = new NativeConversionExecutionAdapter(
                    new NativeConversionExecutionState(
                        _existingTaskId,
                        _outputDirOverride,
                        _commandOptions,
                        _startIndex,
                        _cts.Token),
                    new NativeConversionExecutionCallbacks(
                        taskId => TaskId = taskId,
                        TryRecordTaskIndex,
                        (inputPath, isRetry) => ResolveDecryptPassword(hwnd, inputPath, isRetry),
                        inputPath => PromptVisualSplitPages(hwnd, inputPath),
                        (result, command, successMessage, setAllDoneMessage) =>
                            CompleteApplicationResult(
                                hwnd,
                                currentFiles,
                                progressCallback,
                                result,
                                command,
                                successMessage,
                                setAllDoneMessage)));

                if (adapter.TryRun(cmd, currentFiles, progressCallback))
                    return;

                throw new InvalidOperationException($"Unknown convert command '{cmd}'.");
            }
            catch (Exception ex)
            {
                bool wasCanceled = _cts.IsCancellationRequested || ex is OperationCanceledException;
                string errorMsg = wasCanceled ? UserAbortedMessage : ex.Message;

                if (wasCanceled) _progressPresentation.Cancel(errorMsg);
                else
                {
                    _progressPresentation.Fail(errorMsg);
                    _failureToAlert = errorMsg;
                }
                PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
        }

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
                string completedMessage = setAllDoneMessage
                    ? Loc(ProgressAllDoneKey)
                    : _progressPresentation.State.Message;
                _progressPresentation.Succeed(completedMessage);
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
            if (result.Status == ConversionResultStatus.Canceled)
                _progressPresentation.Cancel(errorMsg);
            else if (result.Status == ConversionResultStatus.Parked)
                _progressPresentation.Park(errorMsg);
            else
            {
                _progressPresentation.Fail(errorMsg);
                _failureToAlert = errorMsg;
            }
            PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
            ConversionTaskCleanup.Delete(result.TaskId, bestEffort: true);
            PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
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
            _progressPresentation.WaitForInput();

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
            _progressPresentation.ResumeRunning();

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
            _progressPresentation.WaitForInput();

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
            _progressPresentation.ResumeRunning();

            if (cancelled)
            {
                throw new OperationCanceledException(Localization.T("error_user_aborted", SettingsPresentationController.Shared.Language));
            }
            return input ?? "";
        }

        /// <summary>Shows a Windows toast notification on success, unless notifications are disabled.</summary>
        private void ShowToastNotification(string command, int count)
        {
            if (!SettingsPresentationController.Shared.NotificationsEnabled)
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
            ConversionTaskProgressController.RecordFileStarting(TaskId, index);
        }
    }
}
