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

        static void Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "-v" || args[0] == "--version")
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.2";
                
                if (args.Length == 0)
                {
                    DashboardWindow.Show();
                    return;
                }

                Console.WriteLine($"Clickra v{version} (Modern Shell Edition)");
                Console.WriteLine("Author: Youchen Jiang");
                Console.WriteLine("Commands: ppt2pdf, word2pdf, merge-pdf, img2pdf, img-merge, img-stitch, --deploy");
                return;
            }

            if (args[0].ToLowerInvariant() == "--deploy" && args.Length >= 2)
            {
                DeployAssets(args[1]);
                return;
            }

            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Clickra <command> <file...>");
                Console.WriteLine("Deployment: Clickra --deploy <target_dir>");
                return;
            }

            string command = args[0].ToLowerInvariant();
            var files = args.Skip(1).OrderBy(f => f).ToList();
            string outputDir = Path.GetDirectoryName(files[0]) ?? "";

            Console.WriteLine($"Executing {command} with {files.Count} files...");

            try
            {
                switch (command)
                {
                    case "ppt2pdf":
                        ValidateExtensions(files, command, ".pptx", ".ppt");
                        FileProcessor.ConvertPptToPdf(files, msg => Console.WriteLine(msg));
                        break;
                    case "word2pdf":
                        ValidateExtensions(files, command, ".docx", ".doc");
                        FileProcessor.ConvertWordToPdf(files, msg => Console.WriteLine(msg));
                        break;
                    case "merge-pdf":
                        ValidateExtensions(files, command, ".pdf");
                        RequireMinFiles(files, command, 2);
                        FileProcessor.MergePdfs(files, Path.Combine(outputDir, "Merged_PDF.pdf"));
                        break;
                    case "img2pdf":
                        ValidateExtensions(files, command, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 1);
                        foreach (var f in files)
                        {
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf");
                            FileProcessor.ImagesToPdf(new List<string> { f }, outName);
                        }
                        break;
                    case "img-merge":
                        ValidateExtensions(files, command, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 2);
                        FileProcessor.ImagesToPdf(files, Path.Combine(outputDir, "Merged_Images.pdf"));
                        break;
                    case "img-stitch":
                        ValidateExtensions(files, command, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 2);
                        FileProcessor.StitchImages(files, Path.Combine(outputDir, "Stitched_Image.png"));
                        break;
                    default:
                        Console.WriteLine("Unknown command: " + command);
                        break;
                }
                
                Console.WriteLine("Operation completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                if (Environment.UserInteractive && !Console.IsInputRedirected)
                {
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
            }
        }

        static void ValidateExtensions(List<string> files, string command, params string[] allowed)
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
                ShowWarning(msg, "Clickra — 格式錯誤");
                Environment.Exit(1);
            }
        }

        static void RequireMinFiles(List<string> files, string command, int min)
        {
            if (files.Count < min)
            {
                string msg = $"指令「{command}」至少需要 {min} 個檔案，但您只傳入了 {files.Count} 個。\n\n請多選幾個檔案後，再透過「傳送到」執行。";
                Console.WriteLine("[錯誤] " + msg);
                ShowWarning(msg, "Clickra — 檔案數量不足");
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
