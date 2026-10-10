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
            IConversionUseCase useCase = ConversionUseCases.GetRequired(CompressPdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                CompressPdfUseCase.CommandName,
                files,
                options,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));
            var observer = new QuietCompressObserver(files);

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress, observer)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
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

                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated.pdf");
                string dbgLog = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_renderdbg.log");
                string healthReport = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated_health.json");
                ClickraDebug.Clear();
                Console.WriteLine($"[Progress] {Loc("cli_progress_translating_pdf_start", Path.GetFileName(f), i + 1, files.Count)}");
                WriteConsoleProgress(0, 100, Loc("cli_progress_translating_pdf", Path.GetFileName(f), i + 1, files.Count));
                try
                {
                    FileProcessor.TranslatePdf(f, outName, targetLang, WriteConsoleProgress);
                    WriteConsoleProgress(100, 100, Loc("cli_progress_translating_pdf_done", Path.GetFileName(f), i + 1, files.Count));
                    FinishConsoleProgressLine();
                    ClickraDebug.SaveTo(dbgLog);
                    Console.WriteLine($"[Debug] Render log: {dbgLog} ({ClickraDebug.Lines.Count} entries)");
                }
                catch (FileNotFoundException)
                {
                    translationFailed = true;
                    FinishConsoleProgressLine();
                    Console.WriteLine($"[Warning] {Loc("cli_warn_translate_file_vanished", f)}");
                }
                catch (DirectoryNotFoundException)
                {
                    translationFailed = true;
                    FinishConsoleProgressLine();
                    Console.WriteLine($"[Warning] {Loc("cli_warn_translate_dir_vanished", f)}");
                }
                catch (Exception ex)
                {
                    translationFailed = true;
                    FinishConsoleProgressLine();
                    ClickraDebug.SaveTo(dbgLog);
                    Console.WriteLine($"[Error] {Loc("cli_err_translate_failed", f, ex.Message)}");
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
                DispatchMarkdownQuiet(files, outputDir, outputDirOverride, toWord);
            }
            else
            {
                ProgressWindow.Show(command, files, outputDirOverride);
            }
            return true;
        }

        private static void DispatchMarkdownQuiet(
            IReadOnlyList<string> files,
            string outputDir,
            string? outputDirOverride,
            bool toWord)
        {
            for (int i = 0; i < files.Count; i++)
            {
                string targetDir = string.IsNullOrWhiteSpace(outputDirOverride)
                    ? ClickraStorage.GetOutputDir(files[i])
                    : outputDir;
                string output = Path.Combine(
                    targetDir,
                    Path.GetFileNameWithoutExtension(files[i]) + (toWord ? ".docx" : ".pdf"));
                if (toWord)
                    FileProcessor.ConvertMarkdownToWord(files[i], output, onProgress: (_, _, msg) => Console.WriteLine($"[Progress] {msg}"));
                else
                    FileProcessor.ConvertMarkdownToPdf(files[i], output, (_, _, msg) => Console.WriteLine($"[Progress] {msg}"));
            }
        }

        /// <summary>Handles office-conversion commands (ppt2pdf, word2pdf, excel2pdf).</summary>
        private static bool DispatchOfficeCommand(string command, List<string> files, bool quiet)
        {
            switch (command)
            {
                case "ppt2pdf":
                    ValidateExtensions(files, command, quiet, ".pptx", ".ppt");
                    if (quiet) FileProcessor.ConvertPptToPdf(files, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                    else ProgressWindow.Show(command, files);
                    return true;
                case "word2pdf":
                    ValidateExtensions(files, command, quiet, ".docx", ".doc");
                    if (quiet) FileProcessor.ConvertWordToPdf(files, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                    else ProgressWindow.Show(command, files);
                    return true;
                case "excel2pdf":
                    ValidateExtensions(files, command, quiet, ".xlsx", ".xls");
                    if (quiet) FileProcessor.ConvertExcelToPdf(files, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                    else ProgressWindow.Show(command, files);
                    return true;
                default:
                    return false;
            }
        }

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
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(MergePdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                MergePdfUseCase.CommandName,
                files,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
        }

        /// <summary>Runs the split-pdf command in quiet mode, writing one output file per input.</summary>
        private static void HandleSplitPdfQuiet(List<string> files, string outputDir, string pagesOption)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(SplitPdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                SplitPdfUseCase.CommandName,
                files,
                new Dictionary<string, object>
                {
                    [SplitPdfUseCase.PagesOptionKey] = pagesOption
                },
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(pagesOption),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));
            var observer = new QuietSplitObserver(files);

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress, observer)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
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
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(command);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                command,
                files,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
        }

        /// <summary>Merges images into one PDF in quiet mode.</summary>
        private static void HandleImgMergeQuiet(List<string> files, string outputDir)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(ImgMergeUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                ImgMergeUseCase.CommandName,
                files,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
        }

        /// <summary>Stitches images into one PNG in quiet mode.</summary>
        private static void HandleImgStitchQuiet(List<string> files, string outputDir)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(ImgStitchUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                ImgStitchUseCase.CommandName,
                files,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
        }

        /// <summary>Compresses images in quiet mode using one shared output directory.</summary>
        private static void HandleImgCompressQuiet(List<string> files, string outputDir)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(ImgCompressUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                ImgCompressUseCase.CommandName,
                files,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
        }

        /// <summary>Converts each image to its own PDF in quiet mode.</summary>
        private static void HandleImg2PdfQuiet(List<string> files, string outputDir)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(Img2PdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                Img2PdfUseCase.CommandName,
                files,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var observer = new QuietImg2PdfObserver(files);

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null, observer)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));

            Console.WriteLine($"[Progress] {Loc("cli_progress_converting_image_saving")}");
        }

        /// <summary>Removes the password from each PDF in quiet mode, translating
        /// password errors into a localized message.</summary>
        private static void HandleDecryptPdfQuiet(List<string> files, string outputDir)
        {
            IConversionUseCase useCase = ConversionUseCases.GetRequired(DecryptPdfUseCase.CommandName);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                DecryptPdfUseCase.CommandName,
                files,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            string passwordError = Localization.T(
                "error_pdf_password_quiet",
                ClickraStorage.GetSetting(ClickraSettings.Language));
            var interaction = new DelegateConversionInteraction(
                (_, _, _, _) => Task.FromException<string?>(new InvalidOperationException(passwordError)),
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
            var progress = new SynchronousProgress<ConversionProgress>(state =>
                Console.WriteLine($"[Progress] {state.Message}"));
            var observer = new QuietDecryptObserver(files);

            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress, observer)
                .GetAwaiter()
                .GetResult();
            if (result.Status != ConversionResultStatus.Succeeded)
                throw new InvalidOperationException(result.Error ?? Loc("error_processing_failed"));
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
