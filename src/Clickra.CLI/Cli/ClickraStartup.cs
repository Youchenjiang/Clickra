using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.UI;

namespace Clickra;

/// <summary>Owns the process startup pipeline: console attachment, font and DPI
/// initialization, dashboard launch, CLI argument parsing and dispatch.</summary>
internal static class ClickraStartup
{
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
        ParseOptions(argList, out bool quiet, out string? outputDirOverride, out bool hasCliLevel, out string compressionLevel, out string pagesOption);

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
            Console.WriteLine($"[錯誤] 指令「{command}」找不到可處理的檔案。");
            return;
        }
        string outputDir = string.IsNullOrWhiteSpace(outputDirOverride)
            ? ClickraStorage.GetOutputDir(files[0])
            : Path.GetFullPath(outputDirOverride);
        string startTimeStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            ClickraCli.DispatchCommandSwitch(command, files, quiet, outputDir, hasCliLevel, compressionLevel, pagesOption);
        }
        catch (Exception ex)
        {
            ReportDispatchError(command, startTimeStr, quiet, ex);
        }
    }

    /// <summary>Configures the PDF font resolver and process DPI awareness; both steps
    /// are best-effort so a failure must never abort the process (PdfSharp falls back
    /// to its default resolver and Windows keeps the system DPI behavior).</summary>
    private static void InitializeProcessEnvironment()
    {
        try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); }
        catch (Exception ex) { Debug.WriteLine($"Font resolver init failed: {ex.Message}"); }
        try { SetProcessDpiAwarenessContext((IntPtr)(-4)); }
        catch (Exception ex) { Debug.WriteLine($"DPI awareness init failed: {ex.Message}"); }
    }

    /// <summary>Reports a dispatch failure: records it in the history when running
    /// quiet, prints the error, and waits for a key press in interactive mode.</summary>
    private static void ReportDispatchError(string command, string startTimeStr, bool quiet, Exception ex)
    {
        if (quiet)
        {
            try { ClickraStorage.CompleteActiveRecord(command, startTimeStr, false, ex.Message); }
            catch (Exception recordEx) { Debug.WriteLine($"Failed to record the failed job: {recordEx.Message}"); }
            System.Threading.Thread.Sleep(1500);
            try { ClickraStorage.ClearActiveRecord(); }
            catch (Exception recordEx) { Debug.WriteLine($"Failed to clear the active job: {recordEx.Message}"); }
        }
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        if (!quiet && Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    /// <summary>Parses and removes global CLI options (quiet mode, output directory,
    /// compression level and page range) from the argument list.</summary>
    private static void ParseOptions(List<string> argList, out bool quiet, out string? outputDirOverride, out bool hasCliLevel, out string compressionLevel, out string pagesOption)
    {
        bool quietByDefault = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
        quiet = quietByDefault;
        if (argList.Contains("--quiet")) { quiet = true; argList.Remove("--quiet"); }
        if (argList.Contains("--no-ui")) { quiet = true; argList.Remove("--no-ui"); }
        if (argList.Contains("--show-ui")) { quiet = false; argList.Remove("--show-ui"); }

        outputDirOverride = ClickraCli.ExtractOptionValue(argList, "--out-dir", "-o", "--out");
        hasCliLevel = argList.Contains("--level") || argList.Contains("--compression-level");
        compressionLevel = ClickraCli.ExtractOptionValue(argList, "--level", "--compression-level") ?? "balanced";
        pagesOption = ClickraCli.ExtractOptionValue(argList, "--pages", "-p") ?? "all";
    }

    /// <summary>Prints the CLI usage text to the console.</summary>
    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Clickra <command> [options] <file...>");
        Console.WriteLine("Options: --quiet / --no-ui  (Run in background without GUI)");
        Console.WriteLine("         --show-ui          (Force show progress window)");
        Console.WriteLine("         --out-dir <dir> / -o <dir> / --out <dir>  (Write outputs to directory)");
        Console.WriteLine("         --level <small|balanced|high>  (PDF compression level)");
        Console.WriteLine("Deployment: Clickra --deploy <target_dir>");
    }

    /// <summary>Handles the version, visual-splitter and --deploy pseudo-commands;
    /// returns true when one of them consumed the invocation.</summary>
    private static bool HandleVersionOrDeploy(string[] args)
    {
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
                DashboardWindow.Show();
                return true;
            }

            Console.WriteLine($"Clickra v{version} (Modern Shell Edition)");
            Console.WriteLine("Author: Youchen Jiang");
            Console.WriteLine("Commands: ppt2pdf, word2pdf, excel2pdf, merge-pdf, compress-pdf, img2pdf, img-merge, img-stitch, translate-pdf, decrypt-pdf, --deploy");
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
            ProgressWindow.Show("split-pdf", new List<string> { pdfPath });
        }
        return true;
    }
}
