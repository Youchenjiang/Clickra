using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Clickra.Core;
using Clickra.UI;

namespace Clickra
{
    class ClickraCli
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
                Console.WriteLine("Commands: ppt2pdf, word2pdf, merge-pdf, img2pdf, img-merge, img-stitch, translate-pdf, --deploy");
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
            var files = argList.Skip(1).Where(f => !int.TryParse(f, out _)).OrderBy(f => f).ToList();
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
                                FileProcessor.ImagesToPdf(new List<string> { f }, outName, null);
                            }
                            Console.WriteLine("[Progress] 轉換完成，正在儲存 PDF...");
                        }
                        else ProgressWindow.Show(command, files);
                        break;
                    case "img-merge":
                        ValidateExtensions(files, command, quiet, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 2, quiet);
                        if (quiet) FileProcessor.ImagesToPdf(files, Path.Combine(outputDir, "Merged_Images.pdf"), (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
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
                                Console.WriteLine($"[Progress] 正在翻譯 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                                FileProcessor.TranslatePdf(f, outName, targetLang, (curr, tot, msg) => Console.WriteLine($"[Progress] {msg}"));
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
                            var blocks = FileProcessor.GetMergedBlocks(segmenter.GetBlocks(words), page.Width, isTablePage);
                            int blockIdx = 0;
                            foreach (var block in blocks)
                            {
                                var blockLines = PdfParagraph.MergeHorizontalLines(block.TextLines);
                                var currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
                                bool? currentIsMath = null;
                                foreach (var line in blockLines)
                                {
                                    bool isMath = PdfParagraph.IsMathLine(line);
                                    bool startsNew = FileProcessor.StartsNewParagraphOrSection(line.Text);

                                    bool prevLineEndedEarly = false;
                                    bool prevLineWasHeading = false;
                                    if (currentGroup.Count > 0)
                                    {
                                        var prevLine = currentGroup[currentGroup.Count - 1];
                                        if (prevLine.BoundingBox.Right < block.Right - 20.0)
                                        {
                                            prevLineEndedEarly = true;
                                        }
                                        if (FileProcessor.IsHeadingLine(prevLine))
                                        {
                                            prevLineWasHeading = true;
                                        }
                                    }

                                    // When the previous line is a heading, don't split on prevLineEndedEarly
                                    bool shouldSplit = startsNew || (prevLineEndedEarly && !prevLineWasHeading) || (prevLineWasHeading && !FileProcessor.IsLineBold(line));

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
                                        foreach (var l in currentGroup)
                                        {
                                            foreach (var w in l.Words)
                                            {
                                                foreach (var let in w.Letters)
                                                {
                                                    if (paragraph.TextWithPlaceholders.Contains("Constraints") || paragraph.TextWithPlaceholders.Contains("Payloads"))
                                                    {
                                                        Console.WriteLine($"  Letter: '{let.Value}' Font: '{let.FontName}' FontSize: {let.FontSize:F2} PointSize: {let.PointSize:F2}");
                                                    }
                                                }
                                            }
                                        }
                                        currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line };
                                        currentIsMath = isMath;
                                    }
                                }
                                if (currentGroup.Count > 0)
                                {
                                    var paragraph = new PdfParagraph(currentGroup);
                                    Console.WriteLine($"Block {blockIdx} Para: [{paragraph.X0:F1}, {paragraph.Y0:F1}, {paragraph.X1:F1}, {paragraph.Y1:F1}] avgFontSize: {paragraph.AverageFontSize:F2} '{paragraph.TextWithPlaceholders}'");
                                    foreach (var l in currentGroup)
                                    {
                                        foreach (var w in l.Words)
                                        {
                                            foreach (var let in w.Letters)
                                            {
                                                if (paragraph.TextWithPlaceholders.Contains("Constraints") || paragraph.TextWithPlaceholders.Contains("Payloads"))
                                                {
                                                    Console.WriteLine($"  Letter: '{let.Value}' Font: '{let.FontName}' FontSize: {let.FontSize:F2} PointSize: {let.PointSize:F2}");
                                                }
                                            }
                                        }
                                    }
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


        static void DeployAssets(string targetDir)
        {
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            var assembly = Assembly.GetExecutingAssembly();
            var resources = new Dictionary<string, string>
            {
                { "Clickra.Resources.AppxManifest.xml", "AppxManifest.xml" },
                { "Clickra.Resources.app.png", "app.png" },
                { "Clickra.Resources.Clickra.exe.manifest", "Clickra.exe.manifest" },
                { "Clickra.Resources.ClickraShell.dll.manifest", "ClickraShell.dll.manifest" },
                { "Clickra.Resources.ClickraShell.dll", "ClickraShell.dll" }
            };

            foreach (var res in resources)
            {
                string targetPath = Path.Combine(targetDir, res.Value);
                Console.WriteLine($"Deploying {res.Value}...");

                try
                {
                    WriteResourceToFile(assembly, res.Key, targetPath);
                }
                catch (IOException)
                {
                    // 檔案鎖定處理邏輯：如果被佔用，嘗試改名備份
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupPath = targetPath + ".old_" + timestamp;
                    try
                    {
                        Console.WriteLine($"[Warning] File {res.Value} is locked. Renaming to bypass lock...");
                        File.Move(targetPath, backupPath);
                        WriteResourceToFile(assembly, res.Key, targetPath);
                        Console.WriteLine("Successfully deployed via rename-bypass.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Critical failure deploying {res.Value}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Failed to deploy {res.Value}: {ex.Message}");
                }
            }
            Console.WriteLine("Deployment completed.");
        }

        static void WriteResourceToFile(Assembly assembly, string resourceName, string targetPath)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) throw new Exception($"Resource {resourceName} not found.");
            using var fileStream = File.Create(targetPath);
            stream.CopyTo(fileStream);
        }
    }
}
