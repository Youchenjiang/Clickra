using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Modules.Image.Application;
using Clickra.Core.Modules.Markdown.Application;
using Clickra.Core.Modules.Office.Application;
using Clickra.Core.Modules.PdfCompression.Application;
using Clickra.Core.Modules.PdfDecrypt.Application;
using Clickra.Core.Modules.PdfMerge.Application;
using Clickra.Core.Modules.PdfSplit.Application;
using Clickra.Core.Modules.PdfTranslation.Application;
using Clickra.Core.Processors;

namespace Clickra.UI;

internal sealed class NativeConversionExecutionAdapter
{
    private readonly string? _existingTaskId;
    private readonly string? _outputDirOverride;
    private readonly IReadOnlyDictionary<string, object>? _commandOptions;
    private readonly int _startIndex;
    private readonly CancellationToken _cancellationToken;
    private readonly Action<string> _taskStarted;
    private readonly Action<int> _recordTaskIndex;
    private readonly Func<string, bool, string> _resolveDecryptPassword;
    private readonly Func<string, string> _promptSplitPages;
    private readonly Action<ConversionResult, string, string?, bool> _completeResult;

    public NativeConversionExecutionAdapter(
        NativeConversionExecutionState state,
        NativeConversionExecutionCallbacks callbacks)
    {
        _existingTaskId = state.ExistingTaskId;
        _outputDirOverride = state.OutputDirOverride;
        _commandOptions = state.CommandOptions;
        _startIndex = state.StartIndex;
        _cancellationToken = state.CancellationToken;
        _taskStarted = callbacks.TaskStarted;
        _recordTaskIndex = callbacks.RecordTaskIndex;
        _resolveDecryptPassword = callbacks.ResolveDecryptPassword;
        _promptSplitPages = callbacks.PromptSplitPages;
        _completeResult = callbacks.CompleteResult;
    }

    public bool TryRun(
        string command,
        List<string> files,
        Action<int, int, string> progressCallback)
    {
        if (command.Equals(DecryptPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunDecrypt(files, progressCallback);
            return true;
        }
        if (command.Equals(SplitPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunSplit(files, progressCallback);
            return true;
        }
        if (command.Equals(CompressPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunCompress(files, progressCallback);
            return true;
        }
        if (command.Equals(Img2PdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunSimple(
                command,
                files,
                progressCallback,
                new SimpleApplicationRunOptions(
                    null,
                    true,
                    new Img2PdfExecutionObserver(_taskStarted, files, progressCallback),
                    Localization.T("cli_progress_converting_image_saving")));
            return true;
        }
        if (command.Equals(MergePdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)
            || command.Equals(ImgMergeUseCase.CommandName, StringComparison.OrdinalIgnoreCase)
            || command.Equals(ImgStitchUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunSimple(
                command,
                files,
                progressCallback,
                new SimpleApplicationRunOptions(null, false, new TaskIdExecutionObserver(_taskStarted)));
            return true;
        }
        if (command.Equals(ImgCompressUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunSimple(
                command,
                files,
                progressCallback,
                new SimpleApplicationRunOptions(
                    _outputDirOverride,
                    true,
                    new TaskIdExecutionObserver(_taskStarted)));
            return true;
        }
        if (ConversionUseCases.TryGet(command, out IConversionUseCase? useCase)
            && useCase is ImageFormatConvertUseCase)
        {
            RunSimple(
                command,
                files,
                progressCallback,
                new SimpleApplicationRunOptions(
                    _outputDirOverride,
                    true,
                    new TaskIdExecutionObserver(_taskStarted)));
            return true;
        }
        if (command.Equals(TranslatePdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunTranslate(files, progressCallback);
            return true;
        }
        if (command.Equals(WordToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)
            || command.Equals(PptToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)
            || command.Equals(ExcelToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunSimple(
                command,
                files,
                progressCallback,
                new SimpleApplicationRunOptions(
                    _outputDirOverride,
                    true,
                    new TaskIdExecutionObserver(_taskStarted)));
            return true;
        }
        if (command.Equals(MarkdownToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)
            || command.Equals(MarkdownToWordUseCase.CommandName, StringComparison.OrdinalIgnoreCase))
        {
            RunSimple(
                command,
                files,
                progressCallback,
                new SimpleApplicationRunOptions(
                    _outputDirOverride,
                    true,
                    new TaskIdExecutionObserver(_taskStarted, _recordTaskIndex),
                    CommandOptions: _commandOptions));
            return true;
        }
        return false;
    }

    private void RunDecrypt(List<string> files, Action<int, int, string> progressCallback)
    {
        _ = ConversionUseCases.GetRequired(DecryptPdfUseCase.CommandName);
        ConversionPlan plan = ConversionExecutionService.Plan(
            DecryptPdfUseCase.CommandName,
            files,
            outputOverride: _outputDirOverride,
            TrackTaskLifecycle: true,
            planOptions: new ExecutionPlanOptions(_existingTaskId, true, _startIndex));
        var interaction = new DelegateConversionInteraction(
            (_, inputPath, isRetry, _) => Task.FromResult<string?>(_resolveDecryptPassword(inputPath, isRetry)),
            (_, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
        var progress = CreatePdfProgress(files, progressCallback, "cli_progress_decrypting_pdf_stage");

        ConversionResult result = ConversionExecutionService.Execute(
            plan,
            interaction,
            progress,
            new FileStageExecutionObserver(
                _taskStarted,
                files,
                progressCallback,
                10,
                "cli_progress_decrypting_pdf"),
            _cancellationToken);

        _completeResult(
            result,
            DecryptPdfUseCase.CommandName,
            Localization.T("cli_progress_decrypting_pdf_saving"),
            true);
    }

    private void RunSplit(List<string> files, Action<int, int, string> progressCallback)
    {
        string pagesOption = GetSplitPagesOptionFromCommandLine();
        _ = ConversionUseCases.GetRequired(SplitPdfUseCase.CommandName);
        var commandOptions = new Dictionary<string, object>
        {
            [SplitPdfUseCase.PagesOptionKey] = pagesOption
        };
        ConversionPlan plan = ConversionExecutionService.Plan(
            SplitPdfUseCase.CommandName,
            files,
            options: commandOptions,
            outputOverride: _outputDirOverride,
            TrackTaskLifecycle: true,
            planOptions: new ExecutionPlanOptions(_existingTaskId, true, _startIndex));
        var interaction = new DelegateConversionInteraction(
            (_, _, _, _) => Task.FromResult<string?>(null),
            (_, inputPath, _) => Task.FromResult<string?>(_promptSplitPages(inputPath)),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
        var progress = CreatePdfProgress(files, progressCallback, "cli_progress_splitting_pdf_stage");

        ConversionResult result = ConversionExecutionService.Execute(
            plan,
            interaction,
            progress,
            new FileStageExecutionObserver(
                _taskStarted,
                files,
                progressCallback,
                10,
                "cli_progress_splitting_pdf"),
            _cancellationToken);

        _completeResult(
            result,
            SplitPdfUseCase.CommandName,
            Localization.T("cli_progress_splitting_pdf_done"),
            true);
    }

    private void RunCompress(List<string> files, Action<int, int, string> progressCallback)
    {
        var (plan, interaction) = PrepareResumableExecution(CompressPdfUseCase.CommandName, files);
        string compressionSummary = "";
        var progress = CreatePdfProgress(
            files,
            progressCallback,
            "cli_progress_compressing_pdf_stage",
            (fileProgress, message) =>
            {
                if (fileProgress >= 100 && !string.IsNullOrWhiteSpace(message))
                    compressionSummary = message;
            });

        ConversionResult result = ConversionExecutionService.Execute(
            plan,
            interaction,
            progress,
            new FileStageExecutionObserver(
                _taskStarted,
                files,
                progressCallback,
                10,
                "cli_progress_compressing_pdf"),
            _cancellationToken);

        _completeResult(
            result,
            CompressPdfUseCase.CommandName,
            string.IsNullOrWhiteSpace(compressionSummary)
                ? Localization.T("cli_progress_compressing_pdf_done")
                : compressionSummary,
            false);
    }

    private void RunSimple(
        string command,
        List<string> files,
        Action<int, int, string> progressCallback,
        SimpleApplicationRunOptions options)
    {
        _ = ConversionUseCases.GetRequired(command);
        ConversionPlan plan = ConversionExecutionService.Plan(
            command,
            files,
            options.CommandOptions,
            outputOverride: options.OutputOverride,
            TrackTaskLifecycle: true,
            planOptions: new ExecutionPlanOptions(_existingTaskId, true, options.Resume ? _startIndex : 0));

        var interaction = new DelegateConversionInteraction(
            (_, _, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
        IProgress<ConversionProgress>? progress = command == Img2PdfUseCase.CommandName
            ? null
            : new CallbackProgress<ConversionProgress>(state =>
                progressCallback(state.Current, state.Total, state.Message));

        ConversionResult result = ConversionExecutionService.Execute(
            plan,
            interaction,
            progress,
            options.Observer,
            _cancellationToken);

        _completeResult(result, command, options.SuccessMessage, true);
    }

    private void RunTranslate(List<string> files, Action<int, int, string> progressCallback)
    {
        var (plan, interaction) = PrepareResumableExecution(TranslatePdfUseCase.CommandName, files);
        var observer = new TranslateExecutionObserver(_taskStarted, files, progressCallback);
        var progress = new CallbackProgress<ConversionProgress>(state =>
        {
            int fileIndex = observer.CurrentFileIndex;
            int localProgress = Math.Clamp(state.Current - (fileIndex * 100), 0, 100);
            int progressPct = (int)(localProgress * 80.0 / 100) + 10;
            progressCallback(
                (fileIndex * 100) + progressPct,
                files.Count * 100,
                Localization.T("cli_progress_translating_pdf_stage", state.Message, fileIndex + 1, files.Count));
        });

        ConversionResult result = ConversionExecutionService.Execute(
            plan,
            interaction,
            progress,
            observer,
            _cancellationToken);

        if (result.Status == ConversionResultStatus.Succeeded)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            progressCallback(
                files.Count * 100,
                files.Count * 100,
                Localization.T("cli_progress_translating_pdf_saving"));
        }

        _completeResult(result, TranslatePdfUseCase.CommandName, null, true);
    }

    private (ConversionPlan Plan, IConversionInteraction Interaction)
        PrepareResumableExecution(string command, IReadOnlyList<string> files)
    {
        _ = ConversionUseCases.GetRequired(command);
        ConversionPlan plan = ConversionExecutionService.Plan(
            command,
            files,
            planOptions: new ExecutionPlanOptions(_existingTaskId, true)) with
        {
            ResumeStartIndex = _startIndex
        };
        var interaction = new DelegateConversionInteraction(
            (_, _, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
        return (plan, interaction);
    }

    private static CallbackProgress<ConversionProgress> CreatePdfProgress(
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
                Localization.T(stageLocalizationKey, state.Message, fileIndex + 1, files.Count));
        });

    private static string GetSplitPagesOptionFromCommandLine()
    {
        string pagesOption = "prompt";
        string[] cliArgs = Environment.GetCommandLineArgs();
        for (int i = 0; i < cliArgs.Length - 1; i++)
        {
            if (cliArgs[i].Equals("--pages", StringComparison.OrdinalIgnoreCase)
                || cliArgs[i].Equals("-p", StringComparison.OrdinalIgnoreCase))
            {
                pagesOption = cliArgs[i + 1];
                break;
            }
        }
        return pagesOption;
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class FileStageExecutionObserver(
        Action<string> taskStarted,
        IReadOnlyList<string> files,
        Action<int, int, string> progressCallback,
        int stageOffset,
        string localizationKey) : IConversionExecutionObserver
    {
        public void OnTaskStarted(string taskId) => taskStarted(taskId);

        public void OnFileStarting(int fileIndex) =>
            progressCallback(
                (fileIndex * 100) + stageOffset,
                files.Count * 100,
                Localization.T(localizationKey, Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count));
    }

    private sealed class Img2PdfExecutionObserver(
        Action<string> taskStarted,
        IReadOnlyList<string> files,
        Action<int, int, string> progressCallback) : IConversionExecutionObserver
    {
        public void OnTaskStarted(string taskId) => taskStarted(taskId);

        public void OnFileStarting(int fileIndex) =>
            progressCallback(
                (fileIndex * 100) + 50,
                files.Count * 100,
                Localization.T("cli_progress_converting_image", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count));
    }

    private sealed class TaskIdExecutionObserver(
        Action<string> taskStarted,
        Action<int>? onFileStarting = null) : IConversionExecutionObserver
    {
        public void OnTaskStarted(string taskId) => taskStarted(taskId);

        public void OnFileStarting(int fileIndex) => onFileStarting?.Invoke(fileIndex);
    }

    private sealed class TranslateExecutionObserver(
        Action<string> taskStarted,
        IReadOnlyList<string> files,
        Action<int, int, string> progressCallback) : IConversionExecutionObserver
    {
        public int CurrentFileIndex { get; private set; }

        public void OnTaskStarted(string taskId) => taskStarted(taskId);

        public void OnFileStarting(int fileIndex)
        {
            CurrentFileIndex = fileIndex;
            progressCallback(
                (fileIndex * 100) + 10,
                files.Count * 100,
                Localization.T("cli_progress_translating_pdf", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count));
        }
    }

    private sealed record SimpleApplicationRunOptions(
        string? OutputOverride,
        bool Resume,
        IConversionExecutionObserver Observer,
        string? SuccessMessage = null,
        IReadOnlyDictionary<string, object>? CommandOptions = null);
}

internal sealed record NativeConversionExecutionState(
    string? ExistingTaskId,
    string? OutputDirOverride,
    IReadOnlyDictionary<string, object>? CommandOptions,
    int StartIndex,
    CancellationToken CancellationToken);

internal sealed record NativeConversionExecutionCallbacks(
    Action<string> TaskStarted,
    Action<int> RecordTaskIndex,
    Func<string, bool, string> ResolveDecryptPassword,
    Func<string, string> PromptSplitPages,
    Action<ConversionResult, string, string?, bool> CompleteResult);
