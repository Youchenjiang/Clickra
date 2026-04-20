using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace ContextTools
{
    class Program
    {
        // Native Win32 MessageBox — zero WinForms dependency, keeps exe tiny
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        const uint MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_ICONERROR = 0x10;

        static void ShowWarning(string msg, string title) =>
            MessageBox(IntPtr.Zero, msg, title, MB_OK | MB_ICONWARNING);

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ContextTools <command> <file...>");
                Console.WriteLine("Commands: ppt2pdf, merge-pdf, img2pdf, img-stitch");
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
                        ConvertPptToPdf(files);
                        break;
                    case "merge-pdf":
                        ValidateExtensions(files, command, ".pdf");
                        RequireMinFiles(files, command, 2);
                        MergePdfs(files, Path.Combine(outputDir, "Merged_PDF.pdf"));
                        break;
                    case "img2pdf":
                        ValidateExtensions(files, command, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 2);
                        MergeImagesToPdf(files, Path.Combine(outputDir, "Merged_Images.pdf"));
                        break;
                    case "img-stitch":
                        ValidateExtensions(files, command, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp");
                        RequireMinFiles(files, command, 2);
                        StitchImages(files, Path.Combine(outputDir, "Stitched_Image.png"));
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
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(); // Wait so user can see what failed instead of window instantly closing
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
                ShowWarning(msg, "ContextTools — 格式錯誤");
                Environment.Exit(1);
            }
        }

        static void RequireMinFiles(List<string> files, string command, int min)
        {
            if (files.Count < min)
            {
                string msg = $"指令「{command}」至少需要 {min} 個檔案，但您只傳入了 {files.Count} 個。\n\n請多選幾個檔案後，再透過「傳送到」執行。";
                Console.WriteLine("[錯誤] " + msg);
                ShowWarning(msg, "ContextTools — 檔案數量不足");
                Environment.Exit(1);
            }
        }

        static void ConvertPptToPdf(List<string> files)
        {
            Type pptType = Type.GetTypeFromProgID("PowerPoint.Application") 
                           ?? throw new Exception("PowerPoint is not installed or accessible.");
            
            dynamic pptApp = Activator.CreateInstance(pptType);
            try
            {
                foreach (var filePath in files)
                {
                    string outputPdfPath = Path.ChangeExtension(filePath, ".pdf");
                    dynamic presentation = null;
                    try
                    {
                        // Open presentation in background (msoFalse = 0)
                        presentation = pptApp.Presentations.Open(filePath, WithWindow: 0);
                        
                        // Save as PDF (ppSaveAsPDF = 32)
                        presentation.SaveAs(outputPdfPath, 32);
                        Console.WriteLine($"Saved PPT to: {outputPdfPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to convert {filePath}: {ex.Message}");
                    }
                    finally
                    {
                        if (presentation != null)
                        {
                            presentation.Close();
                        }
                    }
                }
            }
            finally
            {
                pptApp.Quit();
            }
        }

        static void MergePdfs(List<string> files, string outputPath)
        {
            using var outDoc = new PdfDocument();
            foreach (var f in files)
            {
                Console.WriteLine($"Importing: {f}");
                using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                for (int i = 0; i < inDoc.PageCount; i++)
                {
                    outDoc.AddPage(inDoc.Pages[i]);
                }
            }
            outDoc.Save(outputPath);
            Console.WriteLine($"Merged {files.Count} PDFs to: {outputPath}");
        }

        static void MergeImagesToPdf(List<string> files, string outputPath)
        {
            using var doc = new PdfDocument();
            foreach (var f in files)
            {
                Console.WriteLine($"Adding image: {f}");
                using var ximg = XImage.FromFile(f);
                var page = doc.AddPage();
                
                // Set page size to match image pixels (assuming 72 DPI base)
                double resolutionX = ximg.HorizontalResolution > 0 ? ximg.HorizontalResolution : 72.0;
                double resolutionY = ximg.VerticalResolution > 0 ? ximg.VerticalResolution : 72.0;
                
                page.Width = ximg.PixelWidth * 72.0 / resolutionX;
                page.Height = ximg.PixelHeight * 72.0 / resolutionY;

                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(ximg, 0, 0, page.Width, page.Height);
            }
            doc.Save(outputPath);
            Console.WriteLine($"Created Image PDF at: {outputPath}");
        }

        static void StitchImages(List<string> files, string outputPath)
        {
#pragma warning disable CA1416 // Validate platform compatibility
            List<Image> images = files.Select(Image.FromFile).ToList();
            
            int totalWidth = images.Max(img => img.Width);
            int totalHeight = images.Sum(img => img.Height);

            using var stitched = new Bitmap(totalWidth, totalHeight);
            using var gfx = Graphics.FromImage(stitched);
            gfx.Clear(Color.White);

            int currentY = 0;
            foreach (var img in images)
            {
                // Draw centered horizontally
                int x = (totalWidth - img.Width) / 2;
                gfx.DrawImage(img, x, currentY, img.Width, img.Height);
                currentY += img.Height;
                img.Dispose();
            }

            stitched.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"Stitched {files.Count} images to: {outputPath}");
#pragma warning restore CA1416
        }
    }
}
