using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace ContextTools
{
    class Program
    {
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
                        ConvertPptToPdf(files);
                        break;
                    case "merge-pdf":
                        MergePdfs(files, Path.Combine(outputDir, "Merged_PDF.pdf"));
                        break;
                    case "img2pdf":
                        MergeImagesToPdf(files, Path.Combine(outputDir, "Merged_Images.pdf"));
                        break;
                    case "img-stitch":
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
