using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Models;
using Clickra.Core.Processors;
using Clickra.UI;

namespace Clickra
{
    partial class ClickraCli
    {
        // Native Win32 MessageBox — zero WinForms dependency, keeps exe tiny
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        const uint MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_ICONERROR = 0x10, MB_ICONINFORMATION = 0x40;

        static void ShowWarning(string msg, string title) =>
            MessageBox(IntPtr.Zero, msg, title, MB_OK | MB_ICONWARNING);

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        /// <summary>CLI entry point: attaches the parent console, parses arguments and
        /// dispatches the requested command, falling back to the dashboard UI.</summary>
        [STAThread]
        static void Main(string[] args)
        {
            AttachParentConsoleForCli(args);
            try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch { }
            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }
            if (HandleVersionOrDeploy(args)) return;

            var argList = args.ToList();
            ParseOptions(argList, out bool quiet, out string? outputDirOverride, out bool hasCliLevel, out string compressionLevel, out string pagesOption);

            if (argList.Count < 2)
            {
                PrintUsage();
                return;
            }

            string command = argList[0].ToLowerInvariant();
            var files = ExpandDirectoryArguments(
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
                DispatchCommandSwitch(command, files, quiet, outputDir, hasCliLevel, compressionLevel, pagesOption);
            }
            catch (Exception ex)
            {
                if (quiet)
                {
                    try { ClickraStorage.CompleteActiveRecord(command, startTimeStr, false, ex.Message); } catch { }
                    System.Threading.Thread.Sleep(1500);
                    try { ClickraStorage.ClearActiveRecord(); } catch { }
                }
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                if (!quiet && Environment.UserInteractive && !Console.IsInputRedirected)
                {
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
            }
        }

        /// <summary>Builds the default PDF compression options from saved settings.</summary>
        private static Dictionary<string, object> BuildDefaultPdfOptions()
        {
            string dpiStr = ClickraStorage.GetSetting("PdfCompressTargetDpi");
            if (string.IsNullOrEmpty(dpiStr)) dpiStr = "150";
            string qualityStr = ClickraStorage.GetSetting("PdfCompressJpegQuality");
            if (string.IsNullOrEmpty(qualityStr)) qualityStr = "75";
            string stripStr = ClickraStorage.GetSetting("PdfCompressStripFonts");
            if (string.IsNullOrEmpty(stripStr)) stripStr = "false";
            string minifyStr = ClickraStorage.GetSetting("PdfCompressMinifyContent");
            if (string.IsNullOrEmpty(minifyStr)) minifyStr = "true";

            if (!int.TryParse(dpiStr, out int dpi)) dpi = 150;
            if (!int.TryParse(qualityStr, out int quality)) quality = 75;

            return new Dictionary<string, object>
            {
                { "target_dpi", dpi },
                { "jpeg_quality", quality },
                { "strip_fonts", stripStr.Equals("true", StringComparison.OrdinalIgnoreCase) },
                { "minify_content", minifyStr.Equals("true", StringComparison.OrdinalIgnoreCase) }
            };
        }

        /// <summary>Compresses each PDF in quiet mode, printing progress to the console.</summary>
        private static void HandleCompressPdfQuiet(List<string> files, string outputDir, bool hasCliLevel, string compressionLevel)
        {
            Dictionary<string, object>? pdfOptions = hasCliLevel ? null : BuildDefaultPdfOptions();

            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_compressed.pdf");
                Console.WriteLine($"[Progress] 正在壓縮 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                if (pdfOptions != null)
                {
                    FileProcessor.CompressPdf(f, outName, pdfOptions, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                }
                else
                {
                    FileProcessor.CompressPdf(f, outName, compressionLevel, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                }
            }
        }

        /// <summary>Translates each PDF in quiet mode, saving render debug logs and a health
        /// report per file; sets the exit code when any file fails.</summary>
        private static void HandleTranslatePdfQuiet(List<string> files, string outputDir)
        {
            string targetLang = ClickraStorage.GetSetting("TranslateTargetLang");
            bool translationFailed = false;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (!File.Exists(f))
                {
                    Console.WriteLine($"[Warning] 跳過已不存在的 PDF: {f} ({i + 1}/{files.Count})");
                    continue;
                }

                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated.pdf");
                string dbgLog = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_renderdbg.log");
                string healthReport = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated_health.json");
                ClickraDebug.Clear();
                Console.WriteLine($"[Progress] 開始翻譯 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})");
                WriteConsoleProgress(0, 100, $"正在翻譯 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                try
                {
                    FileProcessor.TranslatePdf(f, outName, targetLang, WriteConsoleProgress);
                    WriteConsoleProgress(100, 100, $"完成翻譯 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})");
                    FinishConsoleProgressLine();
                    ClickraDebug.SaveTo(dbgLog);
                    Console.WriteLine($"[Debug] Render log: {dbgLog} ({ClickraDebug.Lines.Count} entries)");
                }
                catch (FileNotFoundException)
                {
                    translationFailed = true;
                    FinishConsoleProgressLine();
                    Console.WriteLine($"[Warning] 翻譯期間檔案消失，已跳過: {f}");
                }
                catch (DirectoryNotFoundException)
                {
                    translationFailed = true;
                    FinishConsoleProgressLine();
                    Console.WriteLine($"[Warning] 翻譯期間資料夾消失，已跳過: {f}");
                }
                catch (Exception ex)
                {
                    translationFailed = true;
                    FinishConsoleProgressLine();
                    ClickraDebug.SaveTo(dbgLog);
                    Console.WriteLine($"[Error] 翻譯檔案未完成: {f}. 錯誤訊息: {ex.Message}");
                    Console.WriteLine($"[Debug] Health report: {healthReport}");
                }
            }
            if (translationFailed) Environment.ExitCode = 1;
        }

        /// <summary>Handles the version, visual-splitter and --deploy pseudo-commands;
        /// returns true when one of them consumed the invocation.</summary>
        private static bool HandleVersionOrDeploy(string[] args)
        {
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

            if (args[0].Equals("--visual-splitter", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--splitter", StringComparison.OrdinalIgnoreCase))
            {
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

            if (args[0].Equals("--deploy", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
            {
                DeployAssets(args[1]);
                return true;
            }

            return false;
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

            outputDirOverride = ExtractOptionValue(argList, "--out-dir", "-o", "--out");
            hasCliLevel = argList.Contains("--level") || argList.Contains("--compression-level");
            compressionLevel = ExtractOptionValue(argList, "--level", "--compression-level") ?? "balanced";
            pagesOption = ExtractOptionValue(argList, "--pages", "-p") ?? "all";
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

        /// <summary>Routes a command to the office, PDF or image dispatcher and reports
        /// unknown commands.</summary>
        private static void DispatchCommandSwitch(
            string command,
            List<string> files,
            bool quiet,
            string outputDir,
            bool hasCliLevel,
            string compressionLevel,
            string pagesOption)
        {
            if (DispatchOfficeCommand(command, files, quiet)) return;
            if (DispatchPdfCommand(command, files, quiet, outputDir, hasCliLevel, compressionLevel, pagesOption)) return;
            if (DispatchImageCommand(command, files, quiet, outputDir)) return;

            Console.WriteLine($"[錯誤] 未知指令: {command}");
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
                    ValidateExtensions(files, command, quiet, ".pdf");
                    RequireMinFiles(files, command, 2, quiet);
                    if (quiet) FileProcessor.MergePdfs(files, Path.Combine(outputDir, "Merged_PDF.pdf"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                    else ProgressWindow.Show(command, files);
                    return true;
                case "compress-pdf":
                    ValidateExtensions(files, command, quiet, ".pdf");
                    RequireMinFiles(files, command, 1, quiet);
                    if (quiet) HandleCompressPdfQuiet(files, outputDir, hasCliLevel, compressionLevel);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "split-pdf":
                    ValidateExtensions(files, command, quiet, ".pdf");
                    RequireMinFiles(files, command, 1, quiet);
                    if (quiet)
                    {
                        for (int i = 0; i < files.Count; i++)
                        {
                            var f = files[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_split.pdf");
                            Console.WriteLine($"[Progress] 正在分割 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                            FileProcessor.SplitPdf(f, outName, pagesOption, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                        }
                    }
                    else ProgressWindow.Show(command, files);
                    return true;
                case "translate-pdf":
                    ValidateExtensions(files, command, quiet, ".pdf");
                    RequireMinFiles(files, command, 1, quiet);
                    if (quiet) HandleTranslatePdfQuiet(files, outputDir);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "decrypt-pdf":
                    ValidateExtensions(files, command, quiet, ".pdf");
                    RequireMinFiles(files, command, 1, quiet);
                    if (quiet) HandleDecryptPdfQuiet(files, outputDir);
                    else ProgressWindow.Show(command, files);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Handles image commands (img2pdf, img-merge, img-stitch).</summary>
        private static bool DispatchImageCommand(string command, List<string> files, bool quiet, string outputDir)
        {
            switch (command)
            {
                case "img2pdf":
                    ValidateExtensions(files, command, quiet, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                    RequireMinFiles(files, command, 1, quiet);
                    if (quiet) HandleImg2PdfQuiet(files, outputDir);
                    else ProgressWindow.Show(command, files);
                    return true;
                case "img-merge":
                    ValidateExtensions(files, command, quiet, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                    RequireMinFiles(files, command, 2, quiet);
                    if (quiet) FileProcessor.ConvertImagesToPdf(files, Path.Combine(outputDir, "Merged_Images.pdf"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                    else ProgressWindow.Show(command, files);
                    return true;
                case "img-stitch":
                    ValidateExtensions(files, command, quiet, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                    RequireMinFiles(files, command, 2, quiet);
                    if (quiet) FileProcessor.StitchImages(files, Path.Combine(outputDir, "Stitched_Image.png"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                    else ProgressWindow.Show(command, files);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Converts each image to its own PDF in quiet mode.</summary>
        private static void HandleImg2PdfQuiet(List<string> files, string outputDir)
        {
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf");
                Console.WriteLine($"[Progress] 正在轉換圖片: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                FileProcessor.ConvertImagesToPdf(new List<string> { f }, outName, null);
            }
            Console.WriteLine("[Progress] 轉換完成，正在儲存 PDF...");
        }

        /// <summary>Removes the password from each PDF in quiet mode, translating
        /// password errors into a localized message.</summary>
        private static void HandleDecryptPdfQuiet(List<string> files, string outputDir)
        {
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_decrypted.pdf");
                Console.WriteLine($"[Progress] 正在移除密碼: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");

                try
                {
                    FileProcessor.DecryptPdf(f, outName, "", (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                }
                catch (Exception ex)
                {
                    bool isPasswordError = ex is PdfSharp.Pdf.IO.PdfReaderException &&
                                           ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

                    if (isPasswordError)
                    {
                        throw new InvalidOperationException(Localization.T("error_pdf_password_quiet", ClickraStorage.GetSetting("Language")));
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
    }
}
