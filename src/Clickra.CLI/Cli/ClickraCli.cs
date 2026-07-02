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

        [STAThread]
        static void Main(string[] args)
        {
            AttachParentConsoleForCli(args);
            try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch { }
            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }
            if (args.Length == 0 || args[0] == "-v" || args[0] == "--version")
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
                
                if (args.Length == 0)
                {
                    DashboardWindow.Show();
                    return;
                }

                Console.WriteLine($"Clickra v{version} (Modern Shell Edition)");
                Console.WriteLine("Author: Youchen Jiang");
                Console.WriteLine("Commands: ppt2pdf, word2pdf, excel2pdf, merge-pdf, compress-pdf, img2pdf, img-merge, img-stitch, translate-pdf, decrypt-pdf, --deploy");
                return;
            }

            if (args[0].ToLowerInvariant() == "--deploy" && args.Length >= 2)
            {
                DeployAssets(args[1]);
                return;
            }

            bool quietByDefault = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool quiet = quietByDefault;
            var argList = args.ToList();
            if (argList.Contains("--quiet"))
            {
                quiet = true;
                argList.Remove("--quiet");
            }
            if (argList.Contains("--no-ui"))
            {
                quiet = true;
                argList.Remove("--no-ui");
            }
            if (argList.Contains("--show-ui"))
            {
                quiet = false;
                argList.Remove("--show-ui");
            }
            string? outputDirOverride = ExtractOptionValue(argList, "--out-dir", "-o", "--out");
            bool hasCliLevel = argList.Contains("--level") || argList.Contains("--compression-level");
            string compressionLevel = ExtractOptionValue(argList, "--level", "--compression-level") ?? "balanced";

            if (argList.Count < 2)
            {
                Console.WriteLine("Usage: Clickra <command> [options] <file...>");
                Console.WriteLine("Options: --quiet / --no-ui  (Run in background without GUI)");
                Console.WriteLine("         --show-ui          (Force show progress window)");
                Console.WriteLine("         --out-dir <dir> / -o <dir> / --out <dir>  (Write outputs to directory)");
                Console.WriteLine("         --level <small|balanced|high>  (PDF compression level)");
                Console.WriteLine("Deployment: Clickra --deploy <target_dir>");
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
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string startTimeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            try
            {
                // 登記進行中作業狀態（靜默模式下不顯示進度視窗，但仍需即時狀態）
                if (quiet)
                {
                    try { ClickraStorage.StartActiveRecord(command, files.Count); } catch { }
                    try { ClickraStorage.SetActiveRecordInProgress(); } catch { }
                }

                switch (command)
                {
                    case "ppt2pdf":
                        ValidateExtensions(files, command, quiet, ".pptx", ".ppt");
                        if (quiet) FileProcessor.ConvertPptToPdf(files, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                        else ProgressWindow.Show(command, files);
                        break;
                    case "word2pdf":
                        ValidateExtensions(files, command, quiet, ".docx", ".doc");
                        if (quiet) FileProcessor.ConvertWordToPdf(files, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                        else ProgressWindow.Show(command, files);
                        break;
                    case "excel2pdf":
                        ValidateExtensions(files, command, quiet, ".xlsx", ".xls");
                        if (quiet) FileProcessor.ConvertExcelToPdf(files, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                        else ProgressWindow.Show(command, files);
                        break;
                    case "merge-pdf":
                        ValidateExtensions(files, command, quiet, ".pdf");
                        RequireMinFiles(files, command, 2, quiet);
                        if (quiet) FileProcessor.MergePdfs(files, Path.Combine(outputDir, "Merged_PDF.pdf"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                        else ProgressWindow.Show(command, files);
                        break;
                    case "compress-pdf":
                        ValidateExtensions(files, command, quiet, ".pdf");
                        RequireMinFiles(files, command, 1, quiet);
                        if (quiet)
                        {
                            Dictionary<string, object>? pdfOptions = null;
                            if (!hasCliLevel)
                            {
                                string dpiStr = ClickraStorage.GetSetting("PdfCompressTargetDpi");
                                if (string.IsNullOrEmpty(dpiStr)) dpiStr = "120";
                                string qualityStr = ClickraStorage.GetSetting("PdfCompressJpegQuality");
                                if (string.IsNullOrEmpty(qualityStr)) qualityStr = "75";
                                string stripStr = ClickraStorage.GetSetting("PdfCompressStripFonts");
                                if (string.IsNullOrEmpty(stripStr)) stripStr = "true";
                                string minifyStr = ClickraStorage.GetSetting("PdfCompressMinifyContent");
                                if (string.IsNullOrEmpty(minifyStr)) minifyStr = "true";

                                if (!int.TryParse(dpiStr, out int dpi)) dpi = 120;
                                if (!int.TryParse(qualityStr, out int quality)) quality = 75;

                                pdfOptions = new Dictionary<string, object>
                                {
                                    { "target_dpi", dpi },
                                    { "jpeg_quality", quality },
                                    { "strip_fonts", stripStr.Equals("true", StringComparison.OrdinalIgnoreCase) },
                                    { "minify_content", minifyStr.Equals("true", StringComparison.OrdinalIgnoreCase) }
                                };
                            }

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
                        else ProgressWindow.Show(command, files);
                        break;
                    case "img2pdf":
                        ValidateExtensions(files, command, quiet, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 1, quiet);
                        if (quiet)
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
                        else ProgressWindow.Show(command, files);
                        break;
                    case "img-merge":
                        ValidateExtensions(files, command, quiet, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 2, quiet);
                        if (quiet) FileProcessor.ConvertImagesToPdf(files, Path.Combine(outputDir, "Merged_Images.pdf"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                        else ProgressWindow.Show(command, files);
                        break;
                    case "img-stitch":
                        ValidateExtensions(files, command, quiet, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 2, quiet);
                        if (quiet) FileProcessor.StitchImages(files, Path.Combine(outputDir, "Stitched_Image.png"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                        else ProgressWindow.Show(command, files);
                        break;
                    case "translate-pdf":
                        ValidateExtensions(files, command, quiet, ".pdf");
                        RequireMinFiles(files, command, 1, quiet);
                        if (quiet)
                        {
                            string targetLang = ClickraStorage.GetSetting("TranslateTargetLang");
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
                                    FinishConsoleProgressLine();
                                    Console.WriteLine($"[Warning] 翻譯期間檔案消失，已跳過: {f}");
                                }
                                catch (DirectoryNotFoundException)
                                {
                                    FinishConsoleProgressLine();
                                    Console.WriteLine($"[Warning] 翻譯期間資料夾消失，已跳過: {f}");
                                }
                                catch (Exception ex)
                                {
                                    FinishConsoleProgressLine();
                                    Console.WriteLine($"[Error] 翻譯檔案時發生未預期的錯誤，已跳過: {f}. 錯誤訊息: {ex.Message}");
                                }
                            }
                        }
                        else ProgressWindow.Show(command, files);
                        break;
                    case "decrypt-pdf":
                        ValidateExtensions(files, command, quiet, ".pdf");
                        RequireMinFiles(files, command, 1, quiet);
                        if (quiet)
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
                        else ProgressWindow.Show(command, files);
                        break;
#if DEBUG
                    case "test-layout":
                        {
                            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(files[0]);
                            int pageNum = 6;
                            var pageArg = argList.Skip(1).FirstOrDefault(a => int.TryParse(a, out _));
                            if (pageArg != null && int.TryParse(pageArg, out int p))
                            {
                                pageNum = p;
                            }
                            var page = pigDoc.GetPage(pageNum);
                            var words = UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor.NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();
                            var segmenter = new UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter.DocstrumBoundingBoxes();
                            bool isTablePage = words.Any(w => w.Text.Equals("Table", StringComparison.OrdinalIgnoreCase) || 
                                                              w.Text.Equals("表", StringComparison.OrdinalIgnoreCase));
                            var blocks = PdfTranslateProcessor.GetMergedBlocks(segmenter.GetBlocks(words), page.Width, isTablePage);
                            int blockIdx = 0;
                            foreach (var block in blocks)
                            {
                                var blockLines = PdfParagraph.MergeHorizontalLines(block.TextLines);
                                var currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
                                bool? currentIsMath = null;
                                foreach (var line in blockLines)
                                {
                                    bool isMath = PdfParagraph.IsMathLine(line);
                                    bool startsNew = PdfTranslateProcessor.StartsNewParagraphOrSection(line.Text);

                                    bool prevLineEndedEarly = false;
                                    bool prevLineWasHeading = false;
                                    if (currentGroup.Count > 0)
                                    {
                                        var prevLine = currentGroup[currentGroup.Count - 1];
                                        if (prevLine.BoundingBox.Right < block.Right - 20.0)
                                        {
                                            prevLineEndedEarly = true;
                                        }
                                        if (PdfTranslateProcessor.IsHeadingLine(prevLine))
                                        {
                                            prevLineWasHeading = true;
                                        }
                                    }

                                    bool shouldSplit = startsNew || (prevLineEndedEarly && !prevLineWasHeading) || (prevLineWasHeading && !FontUtilities.IsLineBold(line));

                                    if (currentGroup.Count == 0)
                                    {
                                        currentGroup.Add(line);
                                        currentIsMath = isMath;
                                    }
                                    else if (isMath == currentIsMath && !shouldSplit)
                                    {
                                        currentGroup.Add(line);
                                    }
                                    else
                                    {
                                        var paragraph = new PdfParagraph(currentGroup);
                                        Console.WriteLine($"Block {blockIdx} Para: [{paragraph.X0:F1}, {paragraph.Y0:F1}, {paragraph.X1:F1}, {paragraph.Y1:F1}] avgFontSize: {paragraph.AverageFontSize:F2} '{paragraph.TextWithPlaceholders}'");
                                        currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line };
                                        currentIsMath = isMath;
                                    }
                                }
                                if (currentGroup.Count > 0)
                                {
                                    var paragraph = new PdfParagraph(currentGroup);
                                    Console.WriteLine($"Block {blockIdx} Para: [{paragraph.X0:F1}, {paragraph.Y0:F1}, {paragraph.X1:F1}, {paragraph.Y1:F1}] avgFontSize: {paragraph.AverageFontSize:F2} '{paragraph.TextWithPlaceholders}'");
                                }
                                blockIdx++;
                            }
                        }
                        break;
#endif
                    default:
                        Console.WriteLine("Unknown command: " + command);
                        break;
                }
                
                if (quiet)
                {
                    try { ClickraStorage.CompleteActiveRecord(command, startTimeStr, true, ""); } catch { }
                    System.Threading.Thread.Sleep(1500);
                    try { ClickraStorage.ClearActiveRecord(); } catch { }
                }
                Console.WriteLine("Operation completed successfully.");
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

    }
}
