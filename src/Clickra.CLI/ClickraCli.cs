using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
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
                Console.WriteLine("Commands: ppt2pdf, word2pdf, merge-pdf, img2pdf, img-merge, img-stitch, translate-pdf, decrypt-pdf, --deploy");
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

            if (argList.Count < 2)
            {
                Console.WriteLine("Usage: Clickra <command> [options] <file...>");
                Console.WriteLine("Options: --quiet / --no-ui  (Run in background without GUI)");
                Console.WriteLine("         --show-ui          (Force show progress window)");
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
            string outputDir = ClickraStorage.GetOutputDir(files[0]);

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
                    case "merge-pdf":
                        ValidateExtensions(files, command, quiet, ".pdf");
                        RequireMinFiles(files, command, 2, quiet);
                        if (quiet) FileProcessor.MergePdfs(files, Path.Combine(outputDir, "Merged_PDF.pdf"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
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
                                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated.pdf");
                                string dbgLog = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_renderdbg.log");
                                ClickraDebug.Clear();
                                Console.WriteLine($"[Progress] 正在翻譯 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                                FileProcessor.TranslatePdf(f, outName, targetLang, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
                                ClickraDebug.SaveTo(dbgLog);
                                Console.WriteLine($"[Debug] Render log: {dbgLog} ({ClickraDebug.Lines.Count} entries)");
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

        static void ValidateExtensions(List<string> files, string command, bool quiet, params string[] allowed)
        {
            var invalid = files
                .Where(f => !allowed.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (invalid.Count > 0)
            {
                string allowedList = string.Join(", ", allowed);
                string invalidList = string.Join("\n  ", invalid.Select(Path.GetFileName));
                string msg = $"指令\u300c{command}\u300d\u53ea\u63a5\u53d7\u4ee5\u4e0b\u683c\u5f0f\uff1a{allowedList}\n\n\u4ee5\u4e0b\u6a94\u6848\u683c\u5f0f\u4e0d\u7b26\uff0c\u5df2\u4e2d\u6b62\u57f7\u884c\uff1a\n  {invalidList}";
                Console.WriteLine("[錯誤] " + msg);
                if (!quiet) ShowWarning(msg, "Clickra — 格式錯誤");
                Environment.Exit(1);
            }
        }

        static List<string> ExpandDirectoryArguments(string command, IEnumerable<string> inputs)
        {
            var allowed = command switch
            {
                "ppt2pdf" => new[] { ".pptx", ".ppt" },
                "word2pdf" => new[] { ".docx", ".doc" },
                "merge-pdf" or "translate-pdf" or "decrypt-pdf" => new[] { ".pdf" },
                "img2pdf" or "img-merge" or "img-stitch" => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" },
                _ => Array.Empty<string>()
            };

            var expanded = new List<string>();
            foreach (var input in inputs)
            {
                if (Directory.Exists(input) && allowed.Length > 0)
                {
                    expanded.AddRange(Directory.EnumerateFiles(input)
                        .Where(file => allowed.Contains(Path.GetExtension(file).ToLowerInvariant())));
                }
                else
                {
                    expanded.Add(input);
                }
            }
            return expanded;
        }

        static void RequireMinFiles(List<string> files, string command, int min, bool quiet)
        {
            if (files.Count < min)
            {
                string msg = $"指令「{command}」至少需要 {min} 個檔案，但您只傳入了 {files.Count} 個。\n\n請多選幾個檔案後，再透過「傳送到」執行。";
                Console.WriteLine("[錯誤] " + msg);
                if (!quiet) ShowWarning(msg, "Clickra — 檔案數量不足");
                Environment.Exit(1);
            }
        }
    }
}
