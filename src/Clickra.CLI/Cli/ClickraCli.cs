using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Models;
using Clickra.Core.Processors;
using Clickra.UI;

namespace Clickra
{
    partial class ClickraCli
    {
        private const string ErrorProcessingFailedKey = "error_processing_failed";

        /// <summary>Immutable options captured before CLI command dispatch begins.</summary>
        internal sealed record DispatchOptions(
            bool Quiet,
            string OutputDir,
            string? OutputDirOverride,
            bool HasCliLevel,
            string CompressionLevel,
            string PagesOption);

        // Native Win32 MessageBox — zero WinForms dependency, keeps exe tiny
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        const uint MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_ICONERROR = 0x10, MB_ICONINFORMATION = 0x40;

        static void ShowWarning(string msg, string title) =>
            MessageBox(IntPtr.Zero, msg, title, MB_OK | MB_ICONWARNING);

        /// <summary>CLI entry point: delegates the whole startup pipeline (console
        /// attachment, dashboard launch, argument parsing and dispatch) to
        /// <see cref="ClickraStartup"/>.</summary>
        [STAThread]
        static void Main(string[] args) => ClickraStartup.Run(args);

        /// <summary>Compresses each PDF in quiet mode, printing progress to the console.</summary>
        private static void HandleCompressPdfQuiet(List<string> files, string outputDir, bool hasCliLevel, string compressionLevel)
        {
            IReadOnlyDictionary<string, object>? options = hasCliLevel
                ? new Dictionary<string, object>
                {
                    [CompressPdfUseCase.LevelOptionKey] = compressionLevel
                }
                : null;
            RunQuietUseCase(
                CompressPdfUseCase.CommandName,
                files,
                options,
                outputDir,
                observer: new QuietCompressObserver(files));
        }

        /// <summary>Translates each PDF in quiet mode, saving render debug logs and a health
        /// report per file; sets the exit code when any file fails.</summary>
        private static void HandleTranslatePdfQuiet(List<string> files, string outputDir)
        {
            string targetLang = ClickraStorage.GetSetting(ClickraSettings.TranslateTargetLang);
            bool translationFailed = false;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (!File.Exists(f))
                {
                    Console.WriteLine($"[Warning] {Loc("cli_warn_file_missing_skip", f, i + 1, files.Count)}");
                    continue;
                }

                string dbgLog = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_renderdbg.log");
                string healthReport = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated_health.json");
                ClickraDebug.Clear();
                Console.WriteLine($"[Progress] {Loc("cli_progress_translating_pdf_start", Path.GetFileName(f), i + 1, files.Count)}");
                WriteConsoleProgress(0, 100, Loc("cli_progress_translating_pdf", Path.GetFileName(f), i + 1, files.Count));
                IConversionUseCase useCase = ConversionUseCases.GetRequired(TranslatePdfUseCase.CommandName);
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    TranslatePdfUseCase.CommandName,
                    new[] { f },
                    new Dictionary<string, object>
                    {
                        [TranslatePdfUseCase.TargetLanguageOptionKey] = targetLang
                    },
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
                var progress = new SynchronousProgress<ConversionProgress>(state =>
                    WriteConsoleProgress(state.Current, state.Total, state.Message));
                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress)
                    .GetAwaiter()
                    .GetResult();

                if (result.Status == ConversionResultStatus.Succeeded)
                {
                    WriteConsoleProgress(100, 100, Loc("cli_progress_translating_pdf_done", Path.GetFileName(f), i + 1, files.Count));
                    FinishConsoleProgressLine();
                    ClickraDebug.SaveTo(dbgLog);
                    Console.WriteLine($"[Debug] Render log: {dbgLog} ({ClickraDebug.Lines.Count} entries)");
                    continue;
                }

                translationFailed = true;
                FinishConsoleProgressLine();
                if (result.FailureKind == ConversionFailureKind.DirectoryNotFound)
                {
                    Console.WriteLine($"[Warning] {Loc("cli_warn_translate_dir_vanished", f)}");
                }
                else if (result.FailureKind == ConversionFailureKind.FileNotFound)
                {
                    Console.WriteLine($"[Warning] {Loc("cli_warn_translate_file_vanished", f)}");
                }
                else
                {
                    ClickraDebug.SaveTo(dbgLog);
                    Console.WriteLine($"[Error] {Loc("cli_err_translate_failed", f, result.Error ?? "")}");
                    Console.WriteLine($"[Debug] Health report: {healthReport}");
                }
            }
            if (translationFailed) Environment.ExitCode = 1;
        }

        /// <summary>Routes a command to the office, PDF or image dispatcher and reports
        /// unknown commands.</summary>
        internal static void DispatchCommandSwitch(
            string command,
            List<string> files,
            DispatchOptions options)
        {
            if (DispatchOfficeCommand(command, files, options.Quiet)) return;
            if (DispatchMarkdownCommand(command, files, options.Quiet, options.OutputDir, options.OutputDirOverride)) return;
            if (DispatchPdfCommand(command, files, options.Quiet, options.OutputDir, options.HasCliLevel, options.CompressionLevel, options.PagesOption)) return;
            if (DispatchImageCommand(command, files, options.Quiet, options.OutputDir, options.OutputDirOverride)) return;

            Console.WriteLine(Loc("cli_err_prefix") + Loc("cli_err_unknown_command", command));
        }

        /// <summary>Handles local Markdown conversion without requiring an Office engine.</summary>
        private static bool DispatchMarkdownCommand(string command, List<string> files, bool quiet, string outputDir, string? outputDirOverride)
        {
            bool toPdf = command.Equals("md2pdf", StringComparison.OrdinalIgnoreCase);
            bool toWord = command.Equals("md2word", StringComparison.OrdinalIgnoreCase);
            if (!toPdf && !toWord) return false;

            string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(command);
            ValidateExtensions(files, command, quiet, allowed);
            RequireMinFiles(files, command, ConvertCommandRegistry.GetMinFiles(command), quiet);
            if (quiet)
            {
                HandleMarkdownQuiet(command, files, outputDir, outputDirOverride);
            }
            else
            {
                ProgressWindow.Show(command, files, outputDirOverride);
            }
            return true;
        }

        private static void HandleMarkdownQuiet(
            string command,
            IReadOnlyList<string> files,
            string outputDir,
            string? outputDirOverride)
            => RunQuietUseCase(
                command,
                files,
                MarkdownPdfOptions.Create(),
                string.IsNullOrWhiteSpace(outputDirOverride) ? null : outputDir);

        /// <summary>Handles office-conversion commands (ppt2pdf, word2pdf, excel2pdf).</summary>
        private static bool DispatchOfficeCommand(string command, List<string> files, bool quiet)
        {
            switch (command)
            {
                case "ppt2pdf":
                    ValidateExtensions(files, command, quiet, ".pptx", ".ppt");
                    if (quiet) HandleOfficeToPdfQuiet(command, files);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "word2pdf":
                    ValidateExtensions(files, command, quiet, ".docx", ".doc");
                    if (quiet) HandleOfficeToPdfQuiet(command, files);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "excel2pdf":
                    ValidateExtensions(files, command, quiet, ".xlsx", ".xls");
                    if (quiet) HandleOfficeToPdfQuiet(command, files);
                    else ProgressWindow.Show(command, files);
                    return true;
                default:
                    return false;
            }
        }

        private static void HandleOfficeToPdfQuiet(string command, IReadOnlyList<string> files)
            => RunQuietUseCase(command, files, null, outputDir: null);

        /// <summary>Handles PDF commands (merge, compress, split, translate, decrypt).</summary>
        private static bool DispatchPdfCommand(
            string command,
            List<string> files,
            bool quiet,
            string outputDir,
            bool hasCliLevel,
            string compressionLevel,
            string pagesOption)
        {
            switch (command)
            {
                case "merge-pdf":
                    return DispatchPdfCase(command, files, quiet, 2,
                        () => HandleMergePdfQuiet(files, outputDir));
                case "compress-pdf":
                    return DispatchPdfCase(command, files, quiet, 1,
                        () => HandleCompressPdfQuiet(files, outputDir, hasCliLevel, compressionLevel));
                case "split-pdf":
                    return DispatchPdfCase(command, files, quiet, 1,
                        () => HandleSplitPdfQuiet(files, outputDir, pagesOption));
                case "translate-pdf":
                    return DispatchPdfCase(command, files, quiet, 1,
                        () => HandleTranslatePdfQuiet(files, outputDir));
                case "decrypt-pdf":
                    return DispatchPdfCase(command, files, quiet, 1,
                        () => HandleDecryptPdfQuiet(files, outputDir));
                default:
                    return false;
            }
        }

        /// <summary>Validates a PDF command's arguments, then runs the quiet handler or opens
        /// the progress window; returns true (the command was consumed).</summary>
        private static bool DispatchPdfCase(string command, List<string> files, bool quiet, int minFiles, Action quietAction)
        {
            ValidateExtensions(files, command, quiet, ".pdf");
            RequireMinFiles(files, command, minFiles, quiet);
            if (quiet) quietAction();
            else ProgressWindow.Show(command, files);
            return true;
        }

        /// <summary>Runs merge-pdf in quiet mode, preserving the shared output directory.</summary>
        private static void HandleMergePdfQuiet(List<string> files, string outputDir)
            => RunQuietUseCase(MergePdfUseCase.CommandName, files, null, outputDir);

        /// <summary>Runs the split-pdf command in quiet mode, writing one output file per input.</summary>
        private static void HandleSplitPdfQuiet(List<string> files, string outputDir, string pagesOption)
        {
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(pagesOption),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            RunQuietUseCase(
                SplitPdfUseCase.CommandName,
                files,
                new Dictionary<string, object>
                {
                    [SplitPdfUseCase.PagesOptionKey] = pagesOption
                },
                outputDir,
                interaction,
                new QuietSplitObserver(files));
        }

        /// <summary>Handles image conversion, merge and stitching commands.</summary>
        private static bool DispatchImageCommand(string command, List<string> files, bool quiet, string outputDir, string? outputDirOverride)
        {
            string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(command);
            switch (command)
            {
                case "img2pdf":
                    ValidateExtensions(files, command, quiet, allowed);
                    RequireMinFiles(files, command, 1, quiet);
                    if (quiet) HandleImg2PdfQuiet(files, outputDir);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "img-merge":
                    ValidateExtensions(files, command, quiet, allowed);
                    RequireMinFiles(files, command, 2, quiet);
                    if (quiet) HandleImgMergeQuiet(files, outputDir);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "img-stitch":
                    ValidateExtensions(files, command, quiet, allowed);
                    RequireMinFiles(files, command, 2, quiet);
                    if (quiet) HandleImgStitchQuiet(files, outputDir);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "img-compress":
                    ValidateExtensions(files, command, quiet, allowed);
                    RequireMinFiles(files, command, ConvertCommandRegistry.GetMinFiles(command), quiet);
                    if (quiet) HandleImgCompressQuiet(files, outputDirOverride ?? outputDir);
                    else ProgressWindow.Show(command, files, outputDirOverride);
                    return true;
                case "img-to-png":
                case "img-to-jpg":
                case "img-to-webp":
                case "img-to-gif":
                case "img-to-heic":
                    return DispatchImageFormatCommand(command, files, quiet, outputDir, outputDirOverride);
                default:
                    return false;
            }
        }

        /// <summary>Validates and dispatches one of the img-to-* format conversion commands.</summary>
        private static bool DispatchImageFormatCommand(string command, List<string> files, bool quiet, string outputDir, string? outputDirOverride)
        {
            string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(command);
            ValidateExtensions(files, command, quiet, allowed);
            RequireMinFiles(files, command, ConvertCommandRegistry.GetMinFiles(command), quiet);

            if (quiet)
            {
                HandleImageFormatQuiet(command, files, outputDir);
            }
            else
            {
                ProgressWindow.Show(command, files, outputDirOverride);
            }

            return true;
        }

        /// <summary>Converts images to the requested format in quiet mode using one shared output directory.</summary>
        private static void HandleImageFormatQuiet(string command, List<string> files, string outputDir)
            => RunQuietUseCase(command, files, null, outputDir);

        /// <summary>Merges images into one PDF in quiet mode.</summary>
        private static void HandleImgMergeQuiet(List<string> files, string outputDir)
            => RunQuietUseCase(ImgMergeUseCase.CommandName, files, null, outputDir);

        /// <summary>Stitches images into one PNG in quiet mode.</summary>
        private static void HandleImgStitchQuiet(List<string> files, string outputDir)
            => RunQuietUseCase(ImgStitchUseCase.CommandName, files, null, outputDir);

        /// <summary>Compresses images in quiet mode using one shared output directory.</summary>
        private static void HandleImgCompressQuiet(List<string> files, string outputDir)
            => RunQuietUseCase(ImgCompressUseCase.CommandName, files, null, outputDir);

        /// <summary>Converts each image to its own PDF in quiet mode.</summary>
        private static void HandleImg2PdfQuiet(List<string> files, string outputDir)
        {
            RunQuietUseCase(
                Img2PdfUseCase.CommandName,
                files,
                null,
                outputDir,
                observer: new QuietImg2PdfObserver(files),
                reportProgress: false);

            Console.WriteLine($"[Progress] {Loc("cli_progress_converting_image_saving")}");
        }

        /// <summary>Removes the password from each PDF in quiet mode, translating
        /// password errors into a localized message.</summary>
        private static void HandleDecryptPdfQuiet(List<string> files, string outputDir)
        {
            string passwordError = Localization.T(
                "error_pdf_password_quiet",
                ClickraStorage.GetSetting(ClickraSettings.Language));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromException<string?>(new InvalidOperationException(passwordError)),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            RunQuietUseCase(
                DecryptPdfUseCase.CommandName,
                files,
                null,
                outputDir,
                interaction,
                new QuietDecryptObserver(files));
        }

        private static void RunQuietUseCase(
            string command,
            IReadOnlyList<string> files,
            IReadOnlyDictionary<string, object>? options,
            string? outputDir,
            IConversionInteraction? interaction = null,
            IConversionExecutionObserver? observer = null,
            bool reportProgress = true)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(command);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                command,
                files,
                options,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            interaction ??= new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            IProgress<ConversionProgress>? progress = reportProgress
                ? new SynchronousProgress<ConversionProgress>(state =>
                    Console.WriteLine($"[Progress] {state.Message}"))
                : null;

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress, observer)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc(ErrorProcessingFailedKey));
        }

        private sealed class QuietDecryptObserver(IReadOnlyList<string> files) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) { }

            public void OnFileStarting(int fileIndex) =>
                Console.WriteLine($"[Progress] {Loc("cli_progress_decrypting_pdf", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count)}");
        }

        private sealed class QuietSplitObserver(IReadOnlyList<string> files) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) { }

            public void OnFileStarting(int fileIndex) =>
                Console.WriteLine($"[Progress] {Loc("cli_progress_splitting_pdf", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count)}");
        }

        private sealed class QuietCompressObserver(IReadOnlyList<string> files) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) { }

            public void OnFileStarting(int fileIndex) =>
                Console.WriteLine($"[Progress] {Loc("cli_progress_compressing_pdf", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count)}");
        }

        private sealed class QuietImg2PdfObserver(IReadOnlyList<string> files) : IConversionExecutionObserver
        {
            public void OnTaskStarted(string taskId) { }

            public void OnFileStarting(int fileIndex) =>
                Console.WriteLine($"[Progress] {Loc("cli_progress_converting_image", Path.GetFileName(files[fileIndex]), fileIndex + 1, files.Count)}");
        }

        private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
        {
            public void Report(T value) => report(value);
        }
    }
}
