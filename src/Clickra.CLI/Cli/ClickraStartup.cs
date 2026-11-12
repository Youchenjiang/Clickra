using Clickra.Core.Application;
using Clickra.Core.Modules.Sys.Settings.Application;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Processors;
using Clickra.UI;

namespace Clickra;

/// <summary>Owns the process startup pipeline: console attachment, font and DPI
/// initialization, dashboard launch, CLI argument parsing and dispatch.</summary>
internal static partial class ClickraStartup
{
    private const string CommandSummary = "Commands: ppt2pdf, word2pdf, excel2pdf, md2pdf, md2word, merge-pdf, compress-pdf, translate-pdf, decrypt-pdf, split-pdf, print-pdf, pdf2img, img2pdf, img-merge, img-stitch, img-to-png, img-to-jpg, img-to-webp, img-to-gif, img-to-heic, batch-rename, --deploy";
    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>Runs the CLI pipeline, delegating command execution to
    /// <see cref="ClickraCli"/> and launching the dashboard when no command is given.</summary>
    public static void Run(string[] args)
    {
        ClickraCli.AttachParentConsoleForCli(args);
        InitializeProcessEnvironment();
        if (HandleVersionOrDeploy(args)) return;

        var argList = args.ToList();
        var cliOpts = ParseOptions(argList);

        if (argList.Count > 0 &&
            !ConvertCommandRegistry.IsKnownCommand(argList[0]) &&
            (File.Exists(argList[0]) || Directory.Exists(argList[0])))
        {
            var targetFiles = argList.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
            if (targetFiles.Count > 0)
            {
                if (TryDelegateToLauncher(args))
                    return;
                if (OperatingSystem.IsWindows())
                {
                    DashboardWindow.Show(targetFiles);
                }
                else
                {
                    Console.WriteLine("[Warning] Dashboard GUI is only supported on Windows.");
                }
                return;
            }
        }

        if (argList.Count < 2)
        {
            PrintUsage();
            return;
        }

        string command = argList[0].ToLowerInvariant();
        var files = ClickraCli.ExpandDirectoryArguments(
                command,
                argList.Skip(1).Where(f => !int.TryParse(f, out _)))
            .OrderBy(f => f)
            .ToList();
        if (files.Count == 0)
        {
            Console.WriteLine(ClickraCli.Loc("cli_err_prefix") + ClickraCli.Loc("cli_err_no_files_found", command));
            Environment.ExitCode = 1;
            return;
        }
        string startTimeStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            string outputDir = string.IsNullOrWhiteSpace(cliOpts.OutputDirOverride)
                ? StoragePathController.GetOutputDirectory(files[0])
                : Path.GetFullPath(cliOpts.OutputDirOverride);
            if (!string.IsNullOrWhiteSpace(cliOpts.OutputDirOverride))
            {
                Directory.CreateDirectory(outputDir);
            }
            var dispatchOptions = new ClickraCli.DispatchOptions(
                cliOpts.Quiet,
                outputDir,
                cliOpts.OutputDirOverride,
                cliOpts.HasCliLevel,
                cliOpts.CompressionLevel,
                cliOpts.PagesOption,
                cliOpts.PrinterOption,
                cliOpts.CopiesOption,
                cliOpts.FitToPageOption,
                cliOpts.DarkenOption,
                cliOpts.DuplexOption,
                cliOpts.PaperSizeOption,
                cliOpts.OrientationOption);
            ClickraCli.DispatchCommandSwitch(command, files, dispatchOptions);
        }
        catch (Exception ex)
        {
            ReportDispatchError(command, startTimeStr, cliOpts.Quiet, ex);
        }
    }

    /// <summary>Configures the PDF font resolver and process DPI awareness; both steps
    /// are best-effort so a failure must never abort the process (PdfSharp falls back
    /// to its default resolver and Windows keeps the system DPI behavior).</summary>
    private static void InitializeProcessEnvironment()
    {
        try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); }
        catch (Exception ex) { Debug.WriteLine($"Font resolver init failed: {ex.Message}"); }
        if (OperatingSystem.IsWindows())
        {
            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); }
            catch (Exception ex) { Debug.WriteLine($"DPI awareness init failed: {ex.Message}"); }
        }
    }

    /// <summary>Reports a dispatch failure: records it in the history when running
    /// quiet, prints the error, and waits for a key press in interactive mode.</summary>
    private static void ReportDispatchError(string command, string startTimeStr, bool quiet, Exception ex)
    {
        if (quiet)
        {
            try
            {
                ConversionTaskLifecycle.RecordDispatchFailure(command, ex.Message, startTimeStr);
            }
            catch (Exception recordEx) { Debug.WriteLine($"Failed to record the failed job: {recordEx.Message}"); }
        }
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        // Headless callers must be able to distinguish a failed conversion from success.
        Environment.ExitCode = 1;
        if (!quiet && Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    internal sealed record RawCliOptions(
        bool Quiet,
        string? OutputDirOverride,
        bool HasCliLevel,
        string CompressionLevel,
        string PagesOption,
        string? PrinterOption,
        int CopiesOption,
        bool FitToPageOption,
        bool DarkenOption,
        string? DuplexOption,
        string? PaperSizeOption,
        string? OrientationOption);

    /// <summary>Parses and removes global CLI options (quiet mode, output directory,
    /// compression level, page range, printer, copies, duplex, and paper size) from the argument list.</summary>
    private static RawCliOptions ParseOptions(List<string> argList)
    {
        bool quietByDefault = SettingsController.QuietMode || !OperatingSystem.IsWindows();
        bool quiet = quietByDefault;
        if (argList.Contains("--quiet")) { quiet = true; argList.Remove("--quiet"); }
        if (argList.Contains("--no-ui")) { quiet = true; argList.Remove("--no-ui"); }
        if (argList.Contains("--headless")) { quiet = true; argList.Remove("--headless"); }
        if (argList.Contains("--show-ui"))
        {
            if (OperatingSystem.IsWindows())
            {
                quiet = false;
            }
            else
            {
                Console.WriteLine("[Warning] GUI progress window is only supported on Windows. Running in headless mode.");
                quiet = true;
            }
            argList.Remove("--show-ui");
        }

        string? outputDirOverride = ClickraCli.ExtractOptionValue(argList, "--out-dir", "-o", "--out");
        bool hasCliLevel = argList.Contains("--level") || argList.Contains("--compression-level");
        string compressionLevel = ClickraCli.ExtractOptionValue(argList, "--level", "--compression-level") ?? "balanced";
        string pagesOption = ClickraCli.ExtractOptionValue(argList, "--pages", "-p") ?? "all";
        string? printerOption = ClickraCli.ExtractOptionValue(argList, "--printer", "-m");
        string? copiesStr = ClickraCli.ExtractOptionValue(argList, "--copies", "-c");
        int copiesOption = int.TryParse(copiesStr, out int cVal) ? Math.Max(1, cVal) : 1;
        bool fitToPageOption = !argList.Contains("--no-fit");
        if (argList.Contains("--no-fit")) argList.Remove("--no-fit");
        bool darkenOption = argList.Contains("--darken");
        if (darkenOption) argList.Remove("--darken");
        string? duplexOption = ClickraCli.ExtractOptionValue(argList, "--duplex", "-d");
        string? paperSizeOption = ClickraCli.ExtractOptionValue(argList, "--paper", "--paper-size");
        string? orientationOption = ClickraCli.ExtractOptionValue(argList, "--orientation", "--orient");

        return new RawCliOptions(
            quiet,
            outputDirOverride,
            hasCliLevel,
            compressionLevel,
            pagesOption,
            printerOption,
            copiesOption,
            fitToPageOption,
            darkenOption,
            duplexOption,
            paperSizeOption,
            orientationOption);
    }

    /// <summary>Prints the CLI usage text to the console.</summary>
    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Clickra <command> [options] <file...>");
        Console.WriteLine("Options: --headless / --quiet / --no-ui  (Run in background without GUI)");
        Console.WriteLine("         --show-ui          (Force show progress window)");
        Console.WriteLine("         --out-dir <dir> / -o <dir> / --out <dir>  (Write outputs to directory)");
        Console.WriteLine("         --level <small|balanced|high>  (PDF compression level)");
        Console.WriteLine("         --pages <range> / -p <range>  (Page range for print/split)");
        Console.WriteLine("         --printer <name> / -m <name>  (Printer name for PDF print)");
        Console.WriteLine("         --copies <count> / -c <count> (Number of print copies)");
        Console.WriteLine("         --duplex <simplex|long|short> (Duplex printing mode)");
        Console.WriteLine("         --paper <name>     (Paper size name for print, e.g. A4, Letter)");
        Console.WriteLine("         --orientation <auto|portrait|landscape> (Print orientation)");
        Console.WriteLine("         --darken           (Darken text and enhance contrast for print)");
        Console.WriteLine("         batch-rename       (Batch rename files with numbering, regex, case conversion)");
        Console.WriteLine(CommandSummary);
        Console.WriteLine("Deployment: Clickra --deploy <target_dir>");
    }

    /// <summary>Handles the version, visual-splitter and --deploy pseudo-commands;
    /// returns true when one of them consumed the invocation.</summary>
    private static bool HandleVersionOrDeploy(string[] args)
    {
        if (TryHandleBatchRenameArgs(args)) return true;

        if (args.Any(arg =>
                arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("help", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return true;
        }

        if (args.Length == 0 || args[0] == "-v" || args[0] == "--version")
        {
            var version = typeof(ClickraCli).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
            if (args.Length == 0)
            {
                if (TryDelegateToLauncher(args))
                    return true;
                if (OperatingSystem.IsWindows())
                {
                    DashboardWindow.Show();
                    return true;
                }
                PrintUsage();
                return true;
            }

            Console.WriteLine($"Clickra v{version} (Modern Shell Edition)");
            Console.WriteLine("Author: Youchen Jiang");
            Console.WriteLine(CommandSummary);
            return true;
        }

        if (TryHandleVisualSplitterArgs(args)) return true;

        if (args[0].Equals("--deploy", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
        {
            ClickraCli.DeployAssets(args[1]);
            return true;
        }

        return false;
    }

    /// <summary>Opens the visual splitter for the first PDF when --visual-splitter or
    /// --splitter is passed; returns true when either flag consumed the invocation.</summary>
    private static bool TryHandleVisualSplitterArgs(string[] args)
    {
        if (!args[0].Equals("--visual-splitter", StringComparison.OrdinalIgnoreCase) &&
            !args[0].Equals("--splitter", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string pdfPath = args.Length > 1 ? args[1] : "";
        if (string.IsNullOrEmpty(pdfPath))
        {
            var found = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.pdf");
            if (found.Length > 0) pdfPath = found[0];
        }
        if (!string.IsNullOrEmpty(pdfPath))
        {
            if (OperatingSystem.IsWindows())
            {
                ProgressWindow.Show("split-pdf", new List<string> { pdfPath });
            }
            else
            {
                Console.WriteLine("[Warning] Visual splitter GUI is only supported on Windows.");
            }
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Launch routing: ClickraLauncher is the single L0 UI router.
    // When invoked directly without routing, delegate to ClickraLauncher.exe.
    // ------------------------------------------------------------------

    private const string RoutedEnvironmentVariable = "CLICKRA_LAUNCHER_ROUTED";

    /// <summary>
    /// Delegates UI invocation to ClickraLauncher.exe (the single L0 launch router)
    /// unless this process was already dispatched by ClickraLauncher.
    /// Returns true if delegated to ClickraLauncher; false to continue in NativeAOT mode.
    /// </summary>
    private static bool TryDelegateToLauncher(string[] args)
    {
        if (Environment.GetEnvironmentVariable(RoutedEnvironmentVariable) == "1")
            return false;

        string launcher = Path.Combine(AppContext.BaseDirectory, "ClickraLauncher.exe");
        if (!File.Exists(launcher))
            return false;

        try
        {
            var psi = new ProcessStartInfo(launcher) { UseShellExecute = false };
            foreach (string arg in args)
                psi.ArgumentList.Add(arg);
            using var proc = Process.Start(psi);
            return proc is not null;
        }
        catch
        {
            return false;
        }
    }
}
