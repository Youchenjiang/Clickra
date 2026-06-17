using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Clickra.Core.Processors;
using System.Threading.Tasks;
using System.Text;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Drawing;
#pragma warning disable CA1416 // Validate platform compatibility
using System.Drawing;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Clickra.Core
{
    public static class FileProcessor
    {

        public static void MergePdfs(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfMergeProcessor();
            processor.Process(files, outputPath, null, onProgress, cancellationToken);
        }

        public static void DecryptPdf(string inputPath, string outputPath, string password = "", Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfDecryptProcessor();
            var options = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(password)) options["password"] = password;
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }

        public static void ImagesToPdf(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            int total = files.Count;
            using var doc = new PdfDocument();
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                var f = files[i];
                onProgress?.Invoke((i * 100) + 50, total * 100, $"正在處理圖片: {Path.GetFileName(f)} ({i + 1}/{total})...");
                if (!File.Exists(f)) throw new FileNotFoundException("Image file not found", f);
                using var ximg = XImage.FromFile(f);
                var page = doc.AddPage();
                
                double resolutionX = ximg.HorizontalResolution > 0 ? ximg.HorizontalResolution : 72.0;
                double resolutionY = ximg.VerticalResolution > 0 ? ximg.VerticalResolution : 72.0;
                
                page.Width = ximg.PixelWidth * 72.0 / resolutionX;
                page.Height = ximg.PixelHeight * 72.0 / resolutionY;

                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(ximg, 0, 0, page.Width, page.Height);
            }
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(total * 100, total * 100, "轉換完成，正在儲存 PDF...");
            doc.Save(outputPath);
        }

        public static void StitchImages(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            int total = files.Count;
            onProgress?.Invoke(10, total * 100, "正在分析圖片尺寸...");
            cancellationToken.ThrowIfCancellationRequested();
            List<Image> images = new List<Image>();
            try
            {
                foreach (var f in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    images.Add(Image.FromFile(f));
                }
                
                int totalWidth = images.Max(img => img.Width);
                int totalHeight = images.Sum(img => img.Height);

                using var stitched = new Bitmap(totalWidth, totalHeight);
                using var gfx = Graphics.FromImage(stitched);
                gfx.Clear(Color.White);

                int currentY = 0;
                for (int i = 0; i < images.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                    var img = images[i];
                    onProgress?.Invoke((i * 100) + 50, total * 100, $"正在拼接圖片 ({i + 1}/{total})...");
                    int x = (totalWidth - img.Width) / 2;
                    gfx.DrawImage(img, x, currentY, img.Width, img.Height);
                    currentY += img.Height;
                }

                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(total * 100, total * 100, "拼接完成，正在儲存圖片...");
                stitched.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            finally
            {
                foreach (var img in images)
                {
                    try { img?.Dispose(); } catch { }
                }
            }
        }

        public static void ConvertPptToPdf(List<string> files, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            int total = files.Count;
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                var filePath = files[i];
                int fileIndex = i;
                string fullPath = Path.GetFullPath(filePath);
                string outDir = ClickraStorage.GetOutputDir(fullPath);
                string outputPdfPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(filePath) + ".pdf");
                onProgress?.Invoke((fileIndex * 100) + 10, total * 100, $"正在準備轉換 PowerPoint: {Path.GetFileName(filePath)}...");

                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    Write-Host 'PROGRESS:20'
    $ppt = New-Object -ComObject PowerPoint.Application
    try {{
        Write-Host 'PROGRESS:50'
        $pres = $ppt.Presentations.Open('{fullPath.Replace("'", "''")}', -1, 0, 0)
        Write-Host 'PROGRESS:80'
        $pres.SaveAs('{outputPdfPath.Replace("'", "''")}', 32)
        $pres.Close()
        Write-Host 'PROGRESS:100'
    }} finally {{
        $ppt.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($ppt) | Out-Null
    }}
}} catch {{
    Write-Error $_.Exception.Message
    exit 1
}}";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    using var registration = cancellationToken.Register(() =>
                    {
                        try { process.Kill(true); } catch { }
                    });

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("PROGRESS:"))
                        {
                            if (int.TryParse(e.Data.Substring(9), out int subProg))
                            {
                                int currentProgress = (fileIndex * 100) + subProg;
                                string statusMsg = subProg switch
                                {
                                    20 => $"正在啟動 PowerPoint 引擎 ({fileIndex + 1}/{files.Count})...",
                                    50 => $"正在讀取簡報: {Path.GetFileName(filePath)}...",
                                    80 => $"正在匯出 PDF: {Path.GetFileName(filePath)}...",
                                    100 => $"已完成轉換: {Path.GetFileName(filePath)}",
                                    _ => $"正在轉換 PowerPoint: {Path.GetFileName(filePath)}..."
                                };
                                onProgress?.Invoke(currentProgress, total * 100, statusMsg);
                            }
                        }
                    };
                    process.BeginOutputReadLine();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    cancellationToken.ThrowIfCancellationRequested();

                    if (!File.Exists(outputPdfPath))
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            if (error.Contains("0x80040154") || error.Contains("New-Object"))
                                throw new Exception("Microsoft PowerPoint is not installed. This feature requires Microsoft PowerPoint to be installed on your system.");
                            else
                                throw new Exception($"PowerPoint conversion failed: {error.Trim()}");
                        }
                        throw new Exception("PowerPoint conversion failed with unknown error.");
                    }
                }
            }
        }

        public static void ConvertWordToPdf(List<string> files, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            int total = files.Count;
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                var filePath = files[i];
                int fileIndex = i;
                string fullPath = Path.GetFullPath(filePath);
                string outDir = ClickraStorage.GetOutputDir(fullPath);
                string outputPdfPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(filePath) + ".pdf");
                onProgress?.Invoke((fileIndex * 100) + 10, total * 100, $"正在準備轉換 Word: {Path.GetFileName(filePath)}...");

                // Word COM: wdExportFormatPDF = 17
                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    Write-Host 'PROGRESS:20'
    $word = New-Object -ComObject Word.Application
    try {{
        Write-Host 'PROGRESS:50'
        $doc = $word.Documents.Open('{fullPath.Replace("'", "''")}', $false, $true)
        Write-Host 'PROGRESS:80'
        $doc.ExportAsFixedFormat('{outputPdfPath.Replace("'", "''")}', 17)
        $doc.Close($false)
        Write-Host 'PROGRESS:100'
    }} finally {{
        $word.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    }}
}} catch {{
    Write-Error $_.Exception.Message
    exit 1
}}";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    using var registration = cancellationToken.Register(() =>
                    {
                        try { process.Kill(true); } catch { }
                    });

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("PROGRESS:"))
                        {
                            if (int.TryParse(e.Data.Substring(9), out int subProg))
                            {
                                int currentProgress = (fileIndex * 100) + subProg;
                                string statusMsg = subProg switch
                                {
                                    20 => $"正在啟動 Word 引擎 ({fileIndex + 1}/{files.Count})...",
                                    50 => $"正在讀取文件: {Path.GetFileName(filePath)}...",
                                    80 => $"正在匯出 PDF: {Path.GetFileName(filePath)}...",
                                    100 => $"已完成轉換: {Path.GetFileName(filePath)}",
                                    _ => $"正在轉換 Word: {Path.GetFileName(filePath)}..."
                                };
                                onProgress?.Invoke(currentProgress, total * 100, statusMsg);
                            }
                        }
                    };
                    process.BeginOutputReadLine();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    cancellationToken.ThrowIfCancellationRequested();

                    if (!File.Exists(outputPdfPath))
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            if (error.Contains("0x80040154") || error.Contains("New-Object"))
                                throw new Exception("Microsoft Word is not installed. This feature requires Microsoft Word to be installed on your system.");
                            else
                                throw new Exception($"Word conversion failed: {error.Trim()}");
                        }
                        throw new Exception("Word conversion failed with unknown error.");
                    }
                }
            }
        }

        public static void TranslatePdf(string inputPath, string outputPath, string targetLang, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            try { PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch { }
            onProgress?.Invoke(10, 100, "正在分析 PDF 版面結構與公式...");

            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
            int totalPages = pigDoc.NumberOfPages;
            var pageParagraphs = new List<List<PdfParagraph>>();

            for (int p = 1; p <= totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pigDoc.GetPage(p);
                var pageList = new List<PdfParagraph>();

                var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();
                if (words.Count == 0)
                {
                    pageParagraphs.Add(pageList);
                    continue;
                }

                var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
                foreach (var block in blocks)
                {
                    var paragraph = new PdfParagraph(block);
                    pageList.Add(paragraph);
                }

                // Pass 1: Mark initial bypassed paragraphs
                foreach (var para in pageList)
                {
                    para.IsBypassed = para.IsCode || para.IsOnlyMath || string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                                      IsEquationParagraph(para) || IsTableParagraph(para);
                }

                // Pass 2: Propagate bypass to nearby small/label paragraphs (e.g. annotations inside drawings)
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var para in pageList)
                    {
                        if (para.IsBypassed) continue;
                        
                        bool isSmallLabel = para.TextWithPlaceholders.Length <= 20;
                        if (isSmallLabel)
                        {
                            foreach (var other in pageList)
                            {
                                if (other == para || !other.IsBypassed) continue;
                                
                                bool closeX = (para.X0 <= other.X1 + 30) && (para.X1 >= other.X0 - 30);
                                bool closeY = (para.Y0 <= other.Y1 + 30) && (para.Y1 >= other.Y0 - 30);
                                
                                if (closeX && closeY)
                                {
                                    para.IsBypassed = true;
                                    changed = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                pageParagraphs.Add(pageList);
            }

            onProgress?.Invoke(30, 100, "正在翻譯文本內容...");
            var translator = TranslationEngineFactory.Create();
            object logLock = new object();

            for (int p = 0; p < totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paragraphs = pageParagraphs[p];
                if (paragraphs.Count == 0) continue;

                onProgress?.Invoke(30 + (int)(p * 40.0 / totalPages), 100, $"正在翻譯第 {p + 1}/{totalPages} 頁...");

                var tasks = new List<Task>();
                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed)
                    {
                        para.TranslatedText = para.TextWithPlaceholders;
                        continue;
                    }

                    tasks.Add(TranslateParagraphAsync(translator, para, targetLang, p, inputPath, logLock, cancellationToken));
                }

                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }

            onProgress?.Invoke(80, 100, "正在重建 PDF 佈局與公式...");
            cancellationToken.ThrowIfCancellationRequested();

            using var maskBmp = new System.Drawing.Bitmap(1, 1);
            using (var maskG = System.Drawing.Graphics.FromImage(maskBmp))
            {
                maskG.Clear(System.Drawing.Color.White);
            }
            using var ms = new System.IO.MemoryStream();
            maskBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] maskBytes = ms.ToArray();
            using var maskStream = new System.IO.MemoryStream(maskBytes);
            using var whiteMaskImg = XImage.FromStream(() => maskStream);

            using var finalDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

            string targetFontName = "Microsoft JhengHei";
            if (targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "Microsoft YaHei";
            }
            else if (targetLang.Equals("ja", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "MS Gothic";
            }
            else if (targetLang.Equals("ko", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "Malgun Gothic";
            }
            else if (targetLang.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "Arial";
            }

            for (int p = 0; p < totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = finalDoc.Pages[p];

                // Clean the page's original English text streams before adding overlays (Disabled to preserve bypassed diagrams/tables)
                /*
                try
                {
                    StripTextFromPage(page);
                }
                catch { }
                */

                var paragraphs = pageParagraphs[p];
                if (paragraphs.Count == 0) continue;

                // Ensure the page has /ExtGState with /NormalState to reset overprint and multiply blend modes
                try
                {
                    var extGStatesProp = typeof(PdfResources).GetProperty("ExtGStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (extGStatesProp != null)
                    {
                        var extGStates = extGStatesProp.GetValue(page.Resources) as PdfDictionary;
                        if (extGStates != null && !extGStates.Elements.ContainsKey("/NormalState"))
                        {
                            var normalState = new PdfDictionary();
                            normalState.Elements["/BM"] = new PdfName("/Normal");
                            normalState.Elements["/op"] = new PdfBoolean(false);
                            normalState.Elements["/OP"] = new PdfBoolean(false);
                            extGStates.Elements["/NormalState"] = normalState;
                        }
                    }
                }
                catch { }

                using var gfx = XGraphics.FromPdfPage(page);

                foreach (var para in paragraphs)
                {
                    double pageHeight = gfx.PageSize.Height;
                    double paragraphX = para.X0 - 1.5;
                    double paragraphY = pageHeight - para.Y1 - 1.5;  // TOP of paragraph in PDFsharp coords
                    double paragraphWidth = para.Width + 3.0;
                    double paragraphHeight = para.Height + 3.0;

                    if (para.IsBypassed)
                    {
                        // Bypassed paragraphs are preserved in original stream, so we don't redraw them.
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(para.TranslatedText)) continue;

                    gfx.DrawImage(whiteMaskImg, paragraphX, paragraphY, paragraphWidth, paragraphHeight);

                    RenderParagraph(gfx, para, targetFontName);
                }
            }

            onProgress?.Invoke(95, 100, "正在儲存翻譯後的檔案...");
            finalDoc.Save(outputPath);
            finalDoc.Close();
        }

        private static byte[] StripTextFromContentStream(byte[] contentBytes)
        {
            using var ms = new MemoryStream();
            int i = 0;
            int len = contentBytes.Length;
            bool inString = false;
            bool inHex = false;
            int escapeCount = 0;
            bool inText = false;

            while (i < len)
            {
                byte b = contentBytes[i];

                if (inString)
                {
                    if (b == '\\')
                    {
                        escapeCount = (escapeCount + 1) % 2;
                    }
                    else if (b == ')' && escapeCount == 0)
                    {
                        inString = false;
                    }
                    else
                    {
                        escapeCount = 0;
                    }

                    if (!inText) ms.WriteByte(b);
                    i++;
                    continue;
                }

                if (inHex)
                {
                    if (b == '>')
                    {
                        inHex = false;
                    }
                    if (!inText) ms.WriteByte(b);
                    i++;
                    continue;
                }

                if (b == '(')
                {
                    inString = true;
                    escapeCount = 0;
                    if (!inText) ms.WriteByte(b);
                    i++;
                    continue;
                }

                if (b == '<')
                {
                    inHex = true;
                    if (!inText) ms.WriteByte(b);
                    i++;
                    continue;
                }

                if (b == 'B' && i + 1 < len && contentBytes[i + 1] == 'T' && IsDelimiter(contentBytes, i - 1) && IsDelimiter(contentBytes, i + 2))
                {
                    inText = true;
                    i += 2;
                    continue;
                }

                if (b == 'E' && i + 1 < len && contentBytes[i + 1] == 'T' && IsDelimiter(contentBytes, i - 1) && IsDelimiter(contentBytes, i + 2))
                {
                    inText = false;
                    i += 2;
                    continue;
                }

                if (!inText)
                {
                    ms.WriteByte(b);
                }
                i++;
            }

            return ms.ToArray();
        }

        private static bool IsDelimiter(byte[] bytes, int index)
        {
            if (index < 0 || index >= bytes.Length) return true;
            byte b = bytes[index];
            return b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '/' || b == '[' || b == ']' || b == '<' || b == '>' || b == '(' || b == ')';
        }

        private static void StripFormXObjects(PdfDictionary dict, HashSet<string> fontsToStrip)
        {
            if (dict == null) return;
            var visited = new HashSet<PdfDictionary>();
            StripFormXObjectsInternal(dict, visited, fontsToStrip);
        }

        private static void StripFormXObjectsInternal(PdfDictionary dict, HashSet<PdfDictionary> visited, HashSet<string> fontsToStrip)
        {
            if (dict == null || !visited.Add(dict)) return;

            if (dict.Stream != null)
            {
                var subtype = dict.Elements["/Subtype"];
                if (subtype != null && (subtype.ToString() == "/Form" || subtype.ToString() == "Form"))
                {
                    byte[] decompressedBytes = dict.Stream.UnfilteredValue;
                    byte[] cleanBytes = StripSelectedText(decompressedBytes, fontsToStrip);
                    dict.Stream.Value = cleanBytes;
                    dict.Elements.Remove("/Filter");
                }
            }

            // Copy keys to avoid concurrent modification exception if dict changes (though it shouldn't)
            var keys = new List<string>();
            try
            {
                foreach (var key in dict.Elements.KeyNames)
                {
                    if (key != null)
                    {
                        keys.Add(key.ToString());
                    }
                }
            }
            catch { }

            foreach (var key in keys)
            {
                var item = dict.Elements[key];
                if (item is PdfReference reference)
                {
                    item = reference.Value;
                }

                if (item is PdfDictionary subDict)
                {
                    StripFormXObjectsInternal(subDict, visited, fontsToStrip);
                }
                else if (item is PdfArray array)
                {
                    foreach (var arrayItem in array.Elements)
                    {
                        var resolvedItem = arrayItem;
                        if (resolvedItem is PdfReference arrayRef)
                        {
                            resolvedItem = arrayRef.Value;
                        }
                        if (resolvedItem is PdfDictionary arrayDict)
                        {
                            StripFormXObjectsInternal(arrayDict, visited, fontsToStrip);
                        }
                    }
                }
            }
        }

        private static XFont GetMathFont(string originalFontName, double fontSize)
        {
            bool isItalic = originalFontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                            originalFontName.Contains("CMMI", StringComparison.OrdinalIgnoreCase) ||
                            originalFontName.Contains("mi", StringComparison.OrdinalIgnoreCase);
            bool isBold = originalFontName.Contains("Bold", StringComparison.OrdinalIgnoreCase);

            var style = XFontStyle.Regular;
            if (isItalic && isBold) style = XFontStyle.BoldItalic;
            else if (isItalic) style = XFontStyle.Italic;
            else if (isBold) style = XFontStyle.Bold;

            string fontName = "Times New Roman";
            if (originalFontName.Contains("Helvetica", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("Arial", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("Sans", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("SFNSText", StringComparison.OrdinalIgnoreCase))
            {
                fontName = "Arial";
            }
            else if (originalFontName.Contains("Sym", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("Math", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("MSAM", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("MSBM", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("CMSY", StringComparison.OrdinalIgnoreCase))
            {
                fontName = "Cambria Math";
            }
            else if (originalFontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Inconsolata", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Typewriter", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("NimbusMon", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("MonL", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("cmtt", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("ectt", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("sftt", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Teletype", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
                     System.Text.RegularExpressions.Regex.IsMatch(originalFontName, @"tt\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                fontName = "Courier New";
            }

            return new XFont(fontName, fontSize, style);
        }

        private static void RenderBypassedParagraph(XGraphics gfx, PdfParagraph para)
        {
            double pageHeight = gfx.PageSize.Height;
            XBrush brush = XBrushes.Black;

            foreach (var letter in para.AllLetters)
            {
                if (string.IsNullOrEmpty(letter.Value) || string.IsNullOrWhiteSpace(letter.Value)) continue;

                string fontName = letter.FontName ?? "";
                string cleanFontName = fontName;
                int plusIdx = fontName.IndexOf('+');
                if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                {
                    cleanFontName = fontName.Substring(plusIdx + 1);
                }

                if (PdfParagraph.MathFontRegex.IsMatch(cleanFontName))
                {
                    continue;
                }

                XFont font = GetMathFont(letter.FontName, letter.FontSize);
                double x = letter.X;
                double y = pageHeight - letter.Y;

                gfx.DrawString(letter.Value, font, brush, x, y);
            }
        }

        private static bool IsMathOrGreekCharacter(char c)
        {
            return (c >= 0x0370 && c <= 0x03FF) ||  // Greek
                   (c >= 0x1F00 && c <= 0x1FFF) ||  // Greek Extended
                   (c >= 0x2200 && c <= 0x22FF) ||  // Math Operators
                   (c >= 0x2100 && c <= 0x214F) ||  // Letterlike Symbols (like ℓ, ℒ)
                   (c >= 0x2190 && c <= 0x21FF) ||  // Arrows
                   (c >= 0x27C0 && c <= 0x27EF) ||  // Misc Math Symbols A
                   (c >= 0x2980 && c <= 0x29FF) ||  // Misc Math Symbols B
                   (c >= 0x2900 && c <= 0x297F) ||  // Supp Arrows B
                   (c >= 0x27F0 && c <= 0x27FF) ||  // Supp Arrows A
                   c == '×' || c == '÷' || c == '±' || c == '∓' || c == '∗';
        }

        private static bool IsCjkCharacter(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || // CJK Unified Ideographs
                   (c >= 0x3400 && c <= 0x4DBF) || // CJK Unified Ideographs Extension A
                   (c >= 0x3000 && c <= 0x303F) || // CJK Symbols and Punctuation
                   (c >= 0x3040 && c <= 0x30FF) || // Hiragana & Katakana
                   (c >= 0x3100 && c <= 0x312F) || // Bopomofo
                   (c >= 0xAC00 && c <= 0xD7AF) || // Hangul Syllables
                   (c >= 0x1100 && c <= 0x11FF) || // Hangul Jamo
                   c == '，' || c == '。' || c == '、' || c == '；' || c == '：' || c == '？' || c == '！';
        }

        private static string GetCleanFontName(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return "";
            int plusIdx = fontName.IndexOf('+');
            if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
            {
                return fontName.Substring(plusIdx + 1);
            }
            return fontName;
        }

        private static bool IsMonospaceFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;
            return fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Inconsolata", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Typewriter", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("NimbusMon", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("MonL", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("cmtt", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("ectt", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("sftt", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Teletype", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
                   System.Text.RegularExpressions.Regex.IsMatch(fontName, @"tt\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static bool ShouldMergeFormula(MathFormula formula, double averageFontSize)
        {
            if (formula.Letters.Count <= 1) return false;

            double minY = formula.Letters.Min(l => l.RelativeY);
            double maxY = formula.Letters.Max(l => l.RelativeY);
            double yDiff = maxY - minY;

            if (yDiff > averageFontSize * 0.15) return false;

            return true;
        }

        private static bool IsHeadingParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            // Section numbering like "1. Introduction" or "4.1. Taint Specifications" or "5.1.3. Access Path Limit"
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+(\.\d+)*\.\s+[A-Z]")) return true;

            // Uppercase section headers like "REFERENCES" or "ABSTRACT"
            if (txt.Equals("REFERENCES", StringComparison.OrdinalIgnoreCase) || 
                txt.Equals("ABSTRACT", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool IsReferenceParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\[\d+\]");
        }

        private static bool IsEquationParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            // Matches (1), (2), (3), etc. at the end
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"\(\d+\)\s*$")) return true;

            // Matches patterns like x : A -> B
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[a-zA-Z0-9_\{\}\s]+:.*(⇀|→|→|↦|⇒|⊆|∈)")) return true;

            // Density based check: if the text has math formulas/variables placeholders
            // and contains common math operator characters
            int formulaTokensCount = para.Formulas.Count;
            if (formulaTokensCount > 0)
            {
                // Check if the non-placeholder part contains mostly math operators or is very short
                string stripped = System.Text.RegularExpressions.Regex.Replace(txt, @"\{v\d+\}", "").Trim();
                if (string.IsNullOrEmpty(stripped)) return true;

                int letters = stripped.Count(char.IsLetter);
                int operators = stripped.Count(c => "=+-*/()[]{}<>,.:;|\\&!_^⇀→∈∧↓⟨⟩⊆×Σ∗↑↓⇀".Contains(c));
                
                // If the stripped text contains mostly math operators/punctuation rather than English words
                if (letters < 3 || (double)operators / (letters + operators) > 0.4)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTableParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            string[] allWords = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // 1. Matches CWE IDs or GHSA IDs in short table rows (not long prose)
            if (allWords.Length <= 10 && System.Text.RegularExpressions.Regex.IsMatch(txt, @"\b(CWE-\d+|GHSA-[a-z0-9-]+)\b")) return true;

            // 2. Specific Table 3 & Table 4 keywords and rows starting with R1-R7
            if (allWords.Length <= 20 && (
                System.Text.RegularExpressions.Regex.IsMatch(txt, @"\b(Ruleset|Sources/Sinks|Baseline|Enhanced|Hybrid|Enabled|Disabled|R1R2R3R4R5R6R7|Just source/sink|Needed call graph|call graph|Barriers|Group)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                string.Equals(txt, "Base", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(txt, "Custom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(txt, "Combined", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Base (", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Custom (", StringComparison.OrdinalIgnoreCase)
            )) return true;
            if (allWords.Length <= 10 && System.Text.RegularExpressions.Regex.IsMatch(txt, @"^R\d\b")) return true;

            // 3. Matches column headers found only in table rows (short paragraphs)
            if (allWords.Length <= 30 && System.Text.RegularExpressions.Regex.IsMatch(txt, @"\b(CWE|TP|FN|FP|TICR|EXH|Ruleset|Recall|Alerts|Precision)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                // Confirm it's actually a table header by checking that most words are short or numeric
                int shortOrNumeric = allWords.Count(w => w.Length <= 4 || double.TryParse(w, out _));
                if ((double)shortOrNumeric / allWords.Length > 0.5) return true;
            }

            // 4. Density check: if a high percentage of words are numbers or single letters or short codes
            if (allWords.Length > 4)
            {
                int tableIndicators = 0;
                foreach (var w in allWords)
                {
                    if (double.TryParse(w, out _) || 
                        w.Length <= 2 || 
                        w == "✓" || w == "x" || w == "X" ||
                        w == "-" ||
                        System.Text.RegularExpressions.Regex.IsMatch(w, @"^(CWE|GHSA|TICR|EXH|TP|FN|FP|Total|Recall)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        tableIndicators++;
                    }
                }

                if ((double)tableIndicators / allWords.Length > 0.6)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsWithLargeImage(PdfParagraph para, UglyToad.PdfPig.Content.Page pigPage)
        {
            try
            {
                foreach (var img in pigPage.GetImages())
                {
                    if (img.Bounds.Width > 80 && img.Bounds.Height > 80)
                    {
                        var bounds = img.Bounds;
                        bool intersectX = (para.X0 <= bounds.Right) && (para.X1 >= bounds.Left);
                        bool intersectY = (para.Y0 <= bounds.Top) && (para.Y1 >= bounds.Bottom);
                        if (intersectX && intersectY)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static void StripTextFromPage(PdfPage page)
        {
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null) return;

            var fonts = resources.Elements.GetDictionary("/Font");
            if (fonts == null) return;

            var fontsToStrip = new HashSet<string>();
            foreach (var key in fonts.Elements.KeyNames)
            {
                var fontItem = fonts.Elements[key];
                if (fontItem is PdfReference reference) fontItem = reference.Value;
                if (fontItem is PdfDictionary fontDict)
                {
                    var baseFont = fontDict.Elements.GetName("/BaseFont");
                    if (!string.IsNullOrEmpty(baseFont))
                    {
                        string cleanFontName = baseFont.Replace("/", "").Trim();
                        int plusIdx = cleanFontName.IndexOf('+');
                        if (plusIdx >= 0 && plusIdx < cleanFontName.Length - 1)
                        {
                            cleanFontName = cleanFontName.Substring(plusIdx + 1);
                        }

                        // Check if it is a math or code font
                        bool isMathOrCode = PdfParagraph.MathFontRegex.IsMatch(cleanFontName);
                        if (!isMathOrCode)
                        {
                            fontsToStrip.Add(key.ToString().TrimStart('/'));
                        }
                    }
                }
            }

            // Strip Form XObjects on the page to prevent duplicate overlapping text in diagrams
            StripFormXObjects(resources, fontsToStrip);

            // Now modify the page content stream
            var contents = page.Contents;
            for (int i = 0; i < contents.Elements.Count; i++)
            {
                var contentObj = contents.Elements[i];
                if (contentObj is PdfReference reference) contentObj = reference.Value;
                if (contentObj is PdfDictionary contentDict && contentDict.Stream != null)
                {
                    byte[] decompressedBytes = contentDict.Stream.UnfilteredValue;
                    byte[] cleanBytes = StripSelectedText(decompressedBytes, fontsToStrip);
                    contentDict.Stream.Value = cleanBytes;
                    contentDict.Elements.Remove("/Filter");
                }
            }
        }

        private static byte[] StripSelectedText(byte[] contentBytes, HashSet<string> fontsToStrip)
        {
            using var ms = new MemoryStream();
            int i = 0;
            int len = contentBytes.Length;
            
            string currentFontResource = "";
            bool stripActive = false;

            var tokens = new List<string>();

            while (i < len)
            {
                byte b = contentBytes[i];

                if (b == '(')
                {
                    int start = i;
                    i++;
                    int escapeCount = 0;
                    while (i < len)
                    {
                        byte sb = contentBytes[i];
                        if (sb == '\\')
                        {
                            escapeCount = (escapeCount + 1) % 2;
                        }
                        else if (sb == ')' && escapeCount == 0)
                        {
                            i++;
                            break;
                        }
                        else
                        {
                            escapeCount = 0;
                        }
                        i++;
                    }
                    int end = i;

                    if (stripActive)
                    {
                        ms.WriteByte((byte)'(');
                        ms.WriteByte((byte)')');
                    }
                    else
                    {
                        ms.Write(contentBytes, start, end - start);
                    }
                    continue;
                }

                if (b == '<')
                {
                    if (i + 1 < len && contentBytes[i + 1] == '<')
                    {
                        ms.WriteByte((byte)'<');
                        ms.WriteByte((byte)'<');
                        i += 2;
                        continue;
                    }

                    int start = i;
                    i++;
                    while (i < len && contentBytes[i] != '>')
                    {
                        i++;
                    }
                    if (i < len) i++;
                    int end = i;

                    if (stripActive)
                    {
                        ms.WriteByte((byte)'<');
                        ms.WriteByte((byte)'>');
                    }
                    else
                    {
                        ms.Write(contentBytes, start, end - start);
                    }
                    continue;
                }

                if (IsDelimiter(contentBytes, i))
                {
                    ms.WriteByte(b);
                    i++;
                    continue;
                }

                int tokenStart = i;
                while (i < len && !IsDelimiter(contentBytes, i) && contentBytes[i] != '(' && contentBytes[i] != '<')
                {
                    i++;
                }
                int tokenLen = i - tokenStart;
                string token = Encoding.ASCII.GetString(contentBytes, tokenStart, tokenLen);
                ms.Write(contentBytes, tokenStart, tokenLen);

                tokens.Add(token);
                if (tokens.Count > 3) tokens.RemoveAt(0);

                if (token == "Tf" && tokens.Count >= 3)
                {
                    string fontName = tokens[tokens.Count - 3];
                    currentFontResource = fontName;
                    stripActive = fontsToStrip.Contains(fontName.TrimStart('/'));
                }
            }

            return ms.ToArray();
        }

        private static double RenderParagraph(XGraphics gfx, PdfParagraph para, string targetFontName, bool measureOnly = false)
        {
            double pageHeight = gfx.PageSize.Height;
            double paragraphX = para.X0;
            double paragraphY = pageHeight - para.Y1;
            double paragraphWidth = para.Width;
            double paragraphHeight = para.Height;

            string text = (para.TranslatedText ?? "").Replace('∗', '*');
            text = text.Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "").Replace("\uFEFF", "");
            var tokens = TokenizeTranslatedText(text);


            double fontSize = para.AverageFontSize;
            string fontNameForPara = targetFontName;
            if (para.IsCode)
            {
                fontNameForPara = "Courier New";
            }
            XFontStyle fontStyle = para.IsBold || IsHeadingParagraph(para) ? XFontStyle.Bold : XFontStyle.Regular;
            XFont mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
            XBrush brush = XBrushes.Black;

            // Handle rotations (90, 180, 270)
            bool isRotated = false;
            double layoutWidth = paragraphWidth;
            XGraphicsState? state = null;
            string dirStr = para.TextDirection?.ToString() ?? "";

            if (dirStr == "Rotate270")
            {
                double startX = para.X0;
                double startY = pageHeight - para.Y0;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(-90);
                layoutWidth = para.Height;
                isRotated = true;
            }
            else if (dirStr == "Rotate90")
            {
                double startX = para.X1;
                double startY = pageHeight - para.Y1;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(90);
                layoutWidth = para.Height;
                isRotated = true;
            }
            else if (dirStr == "Rotate180")
            {
                double startX = para.X1;
                double startY = pageHeight - para.Y0;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(180);
                layoutWidth = paragraphWidth;
                isRotated = true;
            }
            List<LayoutRow> rows = LayoutParagraph(tokens, mainFont, para.Formulas, layoutWidth, fontSize, para.AverageFontSize, gfx);

            // Compute dynamic line spacing
            double lineSpacingMultiplier = 1.35; // Default CJK line height
            if (targetFontName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
            {
                lineSpacingMultiplier = 1.2;
            }
            double lineHeight = fontSize * lineSpacingMultiplier;

            double limitHeight = isRotated ? para.Width : paragraphHeight;
            double totalHeight = rows.Count * lineHeight;
            
            bool disableScaling = (rows.Count <= 1) || IsHeadingParagraph(para);
            if (totalHeight > limitHeight && !disableScaling)
            {
                double requiredLineSpacingMultiplier = limitHeight / (rows.Count * fontSize);
                if (requiredLineSpacingMultiplier >= 1.0)
                {
                    lineSpacingMultiplier = requiredLineSpacingMultiplier;
                    lineHeight = fontSize * lineSpacingMultiplier;
                }
                else
                {
                    lineSpacingMultiplier = 1.0;
                    double scale = limitHeight / (rows.Count * fontSize);
                    scale = Math.Max(0.8, scale);
                    fontSize *= scale;
                    mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
                    lineHeight = fontSize * lineSpacingMultiplier;
                    rows = LayoutParagraph(tokens, mainFont, para.Formulas, layoutWidth, fontSize, para.AverageFontSize, gfx);
                }
            }

            // Actual rendered height = number of rows × line height
            double renderedHeight = rows.Count * lineHeight;

            // In measure-only mode, skip all drawing and just return the height
            if (measureOnly)
            {
                if (state != null) gfx.Restore(state);
                return renderedHeight;
            }

            double currentY = isRotated ? fontSize : (paragraphY + fontSize);

            foreach (var row in rows)
            {
                double rowWidth = row.Elements.Sum(e => e.Width);
                double startX = paragraphX;
                if (isRotated)
                {
                    startX = 0;
                    if (para.Alignment == PdfParagraph.TextAlignment.Center) startX = (layoutWidth - rowWidth) / 2;
                    else if (para.Alignment == PdfParagraph.TextAlignment.Right) startX = layoutWidth - rowWidth;
                }
                else
                {
                    if (para.Alignment == PdfParagraph.TextAlignment.Center) startX = paragraphX + (paragraphWidth - rowWidth) / 2;
                    else if (para.Alignment == PdfParagraph.TextAlignment.Right) startX = paragraphX + (paragraphWidth - rowWidth);
                }

                double currentX = startX;
                int idx = 0;
                while (idx < row.Elements.Count)
                {
                    var element = row.Elements[idx];
                    if (element.IsFormula)
                    {
                        var formula = para.Formulas[element.FormulaId];
                        double scale = fontSize / para.AverageFontSize;

                        bool hasMono = formula.Letters.Any(l => IsMonospaceFont(l.FontName));
                        double formulaScale = scale;
                        if (hasMono)
                        {
                            formulaScale *= 1.0;
                        }

                        if (ShouldMergeFormula(formula, para.AverageFontSize))
                        {
                            string mergedText = string.Join("", formula.Letters.Select(l => l.Value));
                            double fSize = formula.Letters[0].FontSize * formulaScale;
                            
                            string fontToUse = formula.Letters[0].FontName;
                            foreach (var l in formula.Letters)
                            {
                                if (IsMonospaceFont(l.FontName))
                                {
                                    fontToUse = l.FontName;
                                    break;
                                }
                            }
                            
                            XFont mathFont = GetMathFont(fontToUse, fSize);

                            double avgY = formula.Letters.Average(l => l.RelativeY);
                            double my = currentY - avgY * formulaScale + (fontSize * 0.05);

                            gfx.DrawString(mergedText, mathFont, brush, currentX, my);
                        }
                        else
                        {
                            foreach (var ml in formula.Letters)
                            {
                                double fSize = ml.FontSize * formulaScale;
                                XFont mathFont = GetMathFont(ml.FontName, fSize);

                                double mx = currentX + ml.RelativeX * formulaScale;
                                // Add a minor alignment offset (0.05 * fontSize) to lower Latin formulas slightly relative to CJK baseline
                                double my = currentY - ml.RelativeY * formulaScale + (fontSize * 0.05);

                                gfx.DrawString(ml.Value, mathFont, brush, mx, my);
                            }
                        }
                        currentX += element.Width;
                        idx++;
                    }
                    else
                    {
                        var sbMerged = new StringBuilder();
                        double textStartX = currentX;
                        double textWidth = 0;
                        while (idx < row.Elements.Count && !row.Elements[idx].IsFormula)
                        {
                            var elem = row.Elements[idx];
                            if (elem.Text.Length == 1 && IsMathOrGreekCharacter(elem.Text[0]))
                            {
                                if (sbMerged.Length > 0)
                                {
                                    gfx.DrawString(sbMerged.ToString(), mainFont, brush, textStartX, currentY);
                                    sbMerged.Clear();
                                }
                                string mathFontName = targetFontName;
                                if (targetFontName == "Arial" || targetFontName == "Times New Roman")
                                {
                                    mathFontName = "Segoe UI Symbol";
                                }
                                XFont mathFont = new XFont(mathFontName, mainFont.Size, mainFont.Style);
                                gfx.DrawString(elem.Text, mathFont, brush, currentX, currentY);
                                textStartX = currentX + elem.Width;
                            }
                            else
                            {
                                sbMerged.Append(elem.Text);
                            }
                            textWidth += elem.Width;
                            currentX += elem.Width;
                            idx++;
                        }
                        if (sbMerged.Length > 0)
                        {
                            gfx.DrawString(sbMerged.ToString(), mainFont, brush, textStartX, currentY);
                        }
                    }
                }
                currentY += lineHeight;
            }

            if (state != null)
            {
                gfx.Restore(state);
            }

            return renderedHeight;
        }

        private static List<string> TokenizeTranslatedText(string text)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            int i = 0;
            int len = text.Length;
            while (i < len)
            {
                if (text[i] == '{' && i + 2 < len && text[i + 1] == 'v')
                {
                    int j = i;
                    while (j < len && text[j] != '}') j++;
                    if (j < len && text[j] == '}')
                    {
                        if (sb.Length > 0)
                        {
                            list.Add(sb.ToString());
                            sb.Clear();
                        }
                        list.Add(text.Substring(i, j - i + 1));
                        i = j + 1;
                        continue;
                    }
                }

                char c = text[i];
                if (IsCjkCharacter(c) || IsMathOrGreekCharacter(c))
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add(c.ToString());
                    i++;
                    continue;
                }

                if (c == ' ')
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add(" ");
                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        private static List<LayoutRow> LayoutParagraph(List<string> tokens, XFont font, List<MathFormula> formulas, double maxWidth, double fontSize, double averageFontSize, XGraphics gfx)
        {
            var rows = new List<LayoutRow>();
            var currentRow = new LayoutRow();
            double currentX = 0;

            foreach (var token in tokens)
            {
                bool isFormula = token.StartsWith("{v") && token.EndsWith("}");
                double width = 0;
                int formulaId = -1;

                if (isFormula)
                {
                    if (int.TryParse(token.Substring(2, token.Length - 3), out formulaId) && formulaId >= 0 && formulaId < formulas.Count)
                    {
                        var formula = formulas[formulaId];
                        double formulaScale = fontSize / averageFontSize;
                        bool hasMono = formula.Letters.Any(l => IsMonospaceFont(l.FontName));
                        if (hasMono)
                        {
                            formulaScale *= 1.0;
                        }
                        width = formula.Width * formulaScale;
                    }
                }
                else
                {
                    if (token == " ")
                    {
                        width = gfx.MeasureString(" ", font).Width;
                    }
                    else if (token.Length == 1 && IsMathOrGreekCharacter(token[0]))
                    {
                        string mathFontName = font.FontFamily.Name;
                        if (font.FontFamily.Name == "Arial" || font.FontFamily.Name == "Times New Roman")
                        {
                            mathFontName = "Segoe UI Symbol";
                        }
                        XFont mathFont = new XFont(mathFontName, font.Size, font.Style);
                        width = gfx.MeasureString(token, mathFont).Width;
                    }
                    else
                    {
                        width = gfx.MeasureString(token, font).Width;
                    }
                }
                
                // If single token is wider than maxWidth, split at URL-friendly breakpoints
                if (width > maxWidth && !isFormula && token.Length > 1 && token != " ")
                {
                    // Try to split the token at URL/path-friendly characters
                    var breakChars = new char[] { '/', '-', '.', '_', '=' };
                    var subTokens = new List<string>();
                    var sb2 = new System.Text.StringBuilder();
                    foreach (char ch in token)
                    {
                        if (Array.IndexOf(breakChars, ch) >= 0)
                        {
                            sb2.Append(ch);
                            subTokens.Add(sb2.ToString());
                            sb2.Clear();
                        }
                        else
                        {
                            sb2.Append(ch);
                        }
                    }
                    if (sb2.Length > 0) subTokens.Add(sb2.ToString());

                    if (subTokens.Count > 1)
                    {
                        foreach (var sub in subTokens)
                        {
                            double subWidth = gfx.MeasureString(sub, font).Width;
                            if (currentX + subWidth > maxWidth && currentRow.Elements.Count > 0)
                            {
                                rows.Add(currentRow);
                                currentRow = new LayoutRow();
                                currentX = 0;
                            }
                            currentRow.Elements.Add(new LayoutElement { Text = sub, IsFormula = false, FormulaId = -1, Width = subWidth });
                            currentX += subWidth;
                        }
                        continue;
                    }
                }

                if (currentX + width > maxWidth && currentRow.Elements.Count > 0)
                {
                    rows.Add(currentRow);
                    currentRow = new LayoutRow();
                    currentX = 0;
                    if (token == " ") continue;
                }

                currentRow.Elements.Add(new LayoutElement
                {
                    Text = token,
                    IsFormula = isFormula,
                    FormulaId = formulaId,
                    Width = width
                });
                currentX += width;
            }

            if (currentRow.Elements.Count > 0)
            {
                rows.Add(currentRow);
            }
 
            return rows;
        }

        private class LayoutElement
        {
            public string Text { get; set; } = "";
            public bool IsFormula { get; set; }
            public int FormulaId { get; set; }
            public double Width { get; set; }
        }

        private class LayoutRow
        {
            public List<LayoutElement> Elements { get; set; } = new List<LayoutElement>();
        }

        private static async Task TranslateParagraphAsync(ITranslationEngine translator, PdfParagraph para, string targetLang, int pageIndex, string inputPath, object logLock, CancellationToken cancellationToken)
        {
            try
            {
                string result = await translator.TranslateAsync(para.TextWithPlaceholders, targetLang, cancellationToken);
                para.TranslatedText = string.IsNullOrWhiteSpace(result) ? para.TextWithPlaceholders : result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                para.TranslatedText = para.TextWithPlaceholders;
                try
                {
                    string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {pageIndex + 1}] Error: {ex.Message}{Environment.NewLine}";
                    lock (logLock)
                    {
                        File.AppendAllText(logPath, logLine);
                    }
                }
                catch { }
            }
        }
    }

    public class PdfParagraph
    {
        public enum TextAlignment
        {
            Left,
            Center,
            Right
        }

        public static readonly System.Text.RegularExpressions.Regex MathFontRegex = new(
            @"CM[^R]|MS.M|XY|MT|BL|RM|EU|LA|RS|LINE|LCIRCLE|TeX-|rsfs|txsy|wasy|stmary|.*Mono|.*Code|.*Sym|.*Math|Courier|Console|Inconsolata|Typewriter|NimbusMon|MonL|cmtt|ectt|sftt|tt\d+|Teletype",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
        );

        public string TextWithPlaceholders { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double AverageFontSize { get; set; }
        public bool IsOnlyMath { get; set; }
        public bool IsCode { get; set; }
        public bool IsBypassed { get; set; }
        public bool IsBold { get; set; }
        public bool brk { get; set; } // Paragraph line-break marker
        public List<MathFormula> Formulas { get; set; } = new List<MathFormula>();
        public object TextDirection { get; set; } = "Rotate0";
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public List<PdfLetter> AllLetters { get; set; } = new List<PdfLetter>();

        public double Width => X1 - X0;
        public double Height => Y1 - Y0;

        private string GetLetterDirection(UglyToad.PdfPig.Content.Letter letter)
        {
            double dx = letter.EndBaseLine.X - letter.StartBaseLine.X;
            double dy = letter.EndBaseLine.Y - letter.StartBaseLine.Y;
            double angleDeg = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angleDeg < 0) angleDeg += 360;

            if (angleDeg >= 45 && angleDeg < 135) return "Rotate270";
            if (angleDeg >= 135 && angleDeg < 225) return "Rotate180";
            if (angleDeg >= 225 && angleDeg < 315) return "Rotate90";
            return "Rotate0";
        }

        public PdfParagraph(UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock block)
        {
            X0 = Math.Min(block.BoundingBox.Left, block.BoundingBox.Right);
            Y0 = Math.Min(block.BoundingBox.Bottom, block.BoundingBox.Top);
            X1 = Math.Max(block.BoundingBox.Left, block.BoundingBox.Right);
            Y1 = Math.Max(block.BoundingBox.Bottom, block.BoundingBox.Top);

            // Determine dominant text direction
            var directions = new Dictionary<object, int>();
            foreach (var line in block.TextLines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        var dir = GetLetterDirection(letter);
                        directions[dir] = directions.GetValueOrDefault(dir, 0) + 1;
                    }
                }
            }
            if (directions.Count > 0)
            {
                TextDirection = directions.OrderByDescending(kv => kv.Value).First().Key;
            }

            // Detect alignment
            double totalLeftGap = 0;
            double totalRightGap = 0;
            int lineCountWithGaps = 0;
            foreach (var line in block.TextLines)
            {
                double leftGap = line.BoundingBox.Left - X0;
                double rightGap = X1 - line.BoundingBox.Right;
                if (leftGap > 5 && rightGap > 5)
                {
                    totalLeftGap += leftGap;
                    totalRightGap += rightGap;
                    lineCountWithGaps++;
                }
            }
            if (lineCountWithGaps > 0)
            {
                double avgLeft = totalLeftGap / lineCountWithGaps;
                double avgRight = totalRightGap / lineCountWithGaps;
                double diff = Math.Abs(avgLeft - avgRight);
                if (diff < 15)
                {
                    Alignment = TextAlignment.Center;
                }
                else if (avgLeft > avgRight + 15)
                {
                    Alignment = TextAlignment.Right;
                }
                else
                {
                    Alignment = TextAlignment.Left;
                }
            }
            else
            {
                Alignment = TextAlignment.Left;
            }

            AnalyzeBlock(block);
        }

        private void AnalyzeBlock(UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock block)
        {
            var sb = new StringBuilder();
            var currentFormula = new List<UglyToad.PdfPig.Content.Letter>();
            int bracketsCount = 0;

            double totalFontSize = 0;
            int letterCount = 0;

            int boldCount = 0;
            int totalCount = 0;
            // Compute average font size first
            foreach (var line in block.TextLines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        totalFontSize += letter.FontSize;
                        letterCount++;

                        totalCount++;
                        if (letter.FontName != null && 
                            (letter.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                             letter.FontName.Contains("Medi", StringComparison.OrdinalIgnoreCase) ||
                             letter.FontName.Contains("bx", StringComparison.OrdinalIgnoreCase) ||
                             letter.FontName.Contains("bf", StringComparison.OrdinalIgnoreCase)))
                        {
                            boldCount++;
                        }

                        AllLetters.Add(new PdfLetter
                        {
                            Value = letter.Value ?? "",
                            FontName = letter.FontName ?? "Times New Roman",
                            FontSize = letter.FontSize,
                            X = letter.Location.X,
                            Y = letter.Location.Y
                        });
                    }
                }
            }
            AverageFontSize = letterCount > 0 ? totalFontSize / letterCount : 10;
            IsBold = totalCount > 0 && ((double)boldCount / totalCount) > 0.5;

            for (int lineIdx = 0; lineIdx < block.TextLines.Count; lineIdx++)
            {
                var line = block.TextLines[lineIdx];
                for (int wordIdx = 0; wordIdx < line.Words.Count; wordIdx++)
                {
                    var word = line.Words[wordIdx];
                    bool isMathWord = IsMathWord(word);
                    for (int letterIdx = 0; letterIdx < word.Letters.Count; letterIdx++)
                    {
                        var letter = word.Letters[letterIdx];
                        bool isMath = IsMathCharacter(letter, isMathWord);

                        // Brackets grouping logic
                        bool curV = isMath;
                        if (!curV)
                        {
                            if (currentFormula.Count > 0 && letter.Value == "(")
                            {
                                curV = true;
                                bracketsCount++;
                            }
                            else if (bracketsCount > 0 && letter.Value == ")")
                            {
                                curV = true;
                                bracketsCount--;
                            }
                        }

                        if (curV)
                        {
                            currentFormula.Add(letter);
                        }
                        else
                        {
                            // Close current formula
                            if (currentFormula.Count > 0)
                            {
                                int id = Formulas.Count;
                                Formulas.Add(new MathFormula(id, currentFormula));
                                sb.Append($"{{v{id}}}");
                                currentFormula.Clear();
                                bracketsCount = 0;
                            }

                            sb.Append(letter.Value);
                        }
                    }

                    // Add spaces between words
                    if (wordIdx < line.Words.Count - 1)
                    {
                        if (currentFormula.Count == 0)
                        {
                            sb.Append(" ");
                        }
                    }
                }

                // Add spaces and mark wrap for multiline paragraphs
                if (lineIdx < block.TextLines.Count - 1)
                {
                    if (currentFormula.Count > 0)
                    {
                        int id = Formulas.Count;
                        Formulas.Add(new MathFormula(id, currentFormula));
                        sb.Append($"{{v{id}}}");
                        currentFormula.Clear();
                        bracketsCount = 0;
                    }
                    sb.Append(" ");
                    brk = true;
                }
            }

            // Flush remaining formula
            if (currentFormula.Count > 0)
            {
                int id = Formulas.Count;
                Formulas.Add(new MathFormula(id, currentFormula));
                sb.Append($"{{v{id}}}");
            }

            TextWithPlaceholders = sb.ToString();
            IsOnlyMath = Formulas.Count == 1 && TextWithPlaceholders.Trim() == "{v0}";
            IsCode = IsCodeBlock(TextWithPlaceholders) || IsMonospaceBlock(block);
        }

        private bool IsMonospaceBlock(UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock block)
        {
            int monoCount = 0;
            int totalCount = 0;
            foreach (var line in block.TextLines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        totalCount++;
                        var fontName = letter.FontName;
                        if (fontName != null)
                        {
                            string cleanFontName = fontName;
                            int plusIdx = fontName.IndexOf('+');
                            if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                            {
                                cleanFontName = fontName.Substring(plusIdx + 1);
                            }
                            if (cleanFontName.Contains("Type3", StringComparison.OrdinalIgnoreCase) ||
                                (MathFontRegex.IsMatch(cleanFontName) && 
                                 (cleanFontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Inconsolata", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Typewriter", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("NimbusMon", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("MonL", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("cmtt", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("ectt", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("sftt", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Teletype", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                                  cleanFontName.Contains("Code", StringComparison.OrdinalIgnoreCase))))
                            {
                                monoCount++;
                            }
                        }
                    }
                }
            }
            return totalCount > 0 && ((double)monoCount / totalCount) > 0.6;
        }

        private bool IsCodeBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            // 1. Multiple line number labels (e.g. "1:", "182:") at the start of lines
            var lineNumRegex = new System.Text.RegularExpressions.Regex(@"^[ \t]*\d+:", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (lineNumRegex.Matches(text).Count >= 2) return true;

            // 2. Strict text-based signature for code vs prose:
            // Must contain curly braces, AND must contain strict code keywords as whole words,
            // AND must NOT contain many common English prose stop words.
            string textWithoutPlaceholders = System.Text.RegularExpressions.Regex.Replace(text, @"\{v\d+\}", "");
            bool containsBrace = textWithoutPlaceholders.Contains("{") || textWithoutPlaceholders.Contains("}");
            if (containsBrace)
            {
                var codeKeywordsRegex = new System.Text.RegularExpressions.Regex(
                    @"\b(function|const|let|typeof|module|exports|import|require|return|public|private|class|void|int|string|boolean|var|for|if|while)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                int keywordMatches = codeKeywordsRegex.Matches(textWithoutPlaceholders).Count;

                var proseWordsRegex = new System.Text.RegularExpressions.Regex(
                    @"\b(the|this|that|with|from|these|those|which|where|when|because|although|however|therefore)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                int proseMatches = proseWordsRegex.Matches(textWithoutPlaceholders).Count;

                if (keywordMatches >= 3 && proseMatches <= 1)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsMathWord(UglyToad.PdfPig.Content.Word word)
        {
            foreach (var letter in word.Letters)
            {
                var fontName = letter.FontName;
                if (fontName != null)
                {
                    string cleanFontName = fontName;
                    int plusIdx = fontName.IndexOf('+');
                    if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                    {
                        cleanFontName = fontName.Substring(plusIdx + 1);
                    }
                    if (MathFontRegex.IsMatch(cleanFontName))
                    {
                        return true;
                    }
                }

                if (letter.Value != null)
                {
                    if (letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase)) return true;
                    foreach (char c in letter.Value)
                    {
                        if ((c >= 0x0370 && c <= 0x03FF) || (c >= 0x1F00 && c <= 0x1FFF)) return true; // Greek
                        if (c >= 0x2200 && c <= 0x22FF) return true; // Math Ops
                        if (c >= 0x2100 && c <= 0x214F) return true; // Letterlike
                        if ((c >= 0x27C0 && c <= 0x27EF) || (c >= 0x2980 && c <= 0x29FF)) return true; // Misc Math
                    }
                }
            }
            return false;
        }

        private bool IsMathCharacter(UglyToad.PdfPig.Content.Letter letter, bool isMathWord)
        {
            if (letter.Value == "•" || letter.Value == "\u2022")
            {
                return false;
            }

            var fontName = letter.FontName;
            if (fontName != null)
            {
                // Remove subset prefix (e.g. "AAAAAA+CMMI10" -> "CMMI10")
                string cleanFontName = fontName;
                int plusIdx = fontName.IndexOf('+');
                if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
                {
                    cleanFontName = fontName.Substring(plusIdx + 1);
                }

                if (MathFontRegex.IsMatch(cleanFontName))
                {
                    return true;
                }
            }

            if (letter.Value != null && letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase)) return true;

            if (letter.Value.Length == 1)
            {
                char c = letter.Value[0];
                
                // Greek and Coptic range
                if ((c >= 0x0370 && c <= 0x03FF) || (c >= 0x1F00 && c <= 0x1FFF)) return true;
                
                // Mathematical Operators
                if (c >= 0x2200 && c <= 0x22FF) return true;
                
                // Letterlike Symbols
                if (c >= 0x2100 && c <= 0x214F) return true;
                
                // Miscellaneous Mathematical Symbols
                if ((c >= 0x27C0 && c <= 0x27EF) || (c >= 0x2980 && c <= 0x29FF)) return true;

                // Subscript/Superscript ratios (ONLY if the word is a math/code word!)
                if (isMathWord && letter.FontSize < AverageFontSize * 0.79) return true;
            }

            return false;
        }

    }

    public class MathFormula
    {
        public int Id { get; set; }
        public List<MathLetter> Letters { get; set; } = new List<MathLetter>();
        public double Width { get; set; }

        public MathFormula(int id, List<UglyToad.PdfPig.Content.Letter> letters)
        {
            Id = id;
            double minX = letters.Min(l => l.Location.X);
            foreach (var l in letters)
            {
                Letters.Add(new MathLetter
                {
                    Value = (l.Value ?? "").Replace('∗', '*'),
                    FontName = l.FontName ?? "Times New Roman",
                    FontSize = l.FontSize,
                    RelativeX = l.Location.X - minX,
                    RelativeY = l.Location.Y - letters[0].Location.Y
                });
            }
            Width = letters.Max(l => l.GlyphRectangle.Right) - minX;
        }
    }

    public class MathLetter
    {
        public string Value { get; set; } = "";
        public string FontName { get; set; } = "";
        public double FontSize { get; set; }
        public double RelativeX { get; set; }
        public double RelativeY { get; set; }
    }

    public class PdfLetter
    {
        public string Value { get; set; } = "";
        public string FontName { get; set; } = "";
        public double FontSize { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }
}
