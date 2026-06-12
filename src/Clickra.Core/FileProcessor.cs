using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Drawing;
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
            int total = files.Count;
            using var outDoc = new PdfDocument();
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                var f = files[i];
                onProgress?.Invoke((i * 100) + 50, total * 100, $"正在合併: {Path.GetFileName(f)} ({i + 1}/{total})...");
                using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                for (int j = 0; j < inDoc.PageCount; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    outDoc.AddPage(inDoc.Pages[j]);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(total * 100, total * 100, "合併完成，正在儲存檔案...");
            outDoc.Save(outputPath);
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
            try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch { }
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

                var segmenter = new DocstrumBoundingBoxes();
                bool isTablePage = words.Any(w => w.Text.Equals("Table", StringComparison.OrdinalIgnoreCase) || 
                                                  w.Text.Equals("表", StringComparison.OrdinalIgnoreCase));
                var blocks = GetMergedBlocks(segmenter.GetBlocks(words), page.Width, isTablePage);
                foreach (var block in blocks)
                {
                    var blockLines = PdfParagraph.MergeHorizontalLines(block.TextLines);
                    var currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
                    bool? currentIsMath = null;

                    double minX = block.TextLines.Count > 0 ? block.TextLines.Min(l => l.BoundingBox.Left) : 0;
                    double maxX = block.TextLines.Count > 0 ? block.TextLines.Max(l => l.BoundingBox.Right) : 0;
                    double blockWidth = maxX - minX;

                    bool isTableBlock = blockLines.Count >= 2 && 
                                        (isTablePage || blockWidth < 150.0) && 
                                        (blockLines.Average(l => l.Words.Count) <= 3.5);

                    foreach (var line in blockLines)
                    {
                        bool isMath = PdfParagraph.IsMathLine(line);
                        bool startsNew = StartsNewParagraphOrSection(line.Text);

                        bool prevLineEndedEarly = false;
                        bool prevLineWasHeading = false;
                        if (currentGroup.Count > 0)
                        {
                            var prevLine = currentGroup[currentGroup.Count - 1];
                            if (prevLine.BoundingBox.Right < block.Right - 20.0)
                            {
                                prevLineEndedEarly = true;
                            }
                            if (IsHeadingLine(prevLine))
                            {
                                prevLineWasHeading = true;
                            }
                        }

                        bool prevLineHasGap = isTablePage && currentGroup.Count > 0 && HasColumnGap(currentGroup[currentGroup.Count - 1]);
                        bool currLineHasGap = isTablePage && HasColumnGap(line);
                        bool forceSplit = isTableBlock && currentGroup.Count > 0;

                        // When the previous line is a heading, don't split on prevLineEndedEarly
                        // (headings naturally end early; e.g., '2.1 Text Representation and Modality' + 'Alignment')
                        bool shouldSplit = startsNew || (prevLineEndedEarly && !prevLineWasHeading) || (prevLineWasHeading && !IsLineBold(line)) || prevLineHasGap || currLineHasGap || forceSplit;

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
                            pageList.Add(paragraph);

                            currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line };
                            currentIsMath = isMath;
                        }
                    }

                    if (currentGroup.Count > 0)
                    {
                        var paragraph = new PdfParagraph(currentGroup);
                        pageList.Add(paragraph);
                    }
                }

                // Pass 0: Sanitize TextWithPlaceholders — remove stray '):(...)' bracket artifacts
                // These appear when AnalyzeLines absorbs the opening '(' of a parenthetical phrase
                // into a formula token, leaving a dangling '):(label)' in the text.
                // e.g. "{v0}):(Equation (1))" -> "{v0}"   or   "InfoNCE):(Equation (1))" -> "InfoNCE"
                foreach (var para in pageList)
                {
                    if (string.IsNullOrWhiteSpace(para.TextWithPlaceholders)) continue;
                    string twp = para.TextWithPlaceholders.Trim();
                    // Find first occurrence of "):(" — a stray closing paren followed by colon+open
                    int artifactIdx = twp.IndexOf("):(", System.StringComparison.Ordinal);
                    if (artifactIdx > 0)
                    {
                        para.TextWithPlaceholders = twp.Substring(0, artifactIdx).TrimEnd();
                    }
                }

                // Pass 1: Mark initial bypassed paragraphs
                foreach (var para in pageList)
                {
                    para.IsBypassed = para.IsCode || para.IsOnlyMath || string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                                      IsEquationParagraph(para) || IsTableParagraph(para);
                }

                // Pass 1.1: Bypass author block on page 1
                if (p == 1)
                {
                    double abstractY0 = -1;
                    foreach (var para in pageList)
                    {
                        string txt = para.TextWithPlaceholders.Trim();
                        if (txt.StartsWith("ABSTRACT", StringComparison.OrdinalIgnoreCase) ||
                            txt.StartsWith("摘要", StringComparison.OrdinalIgnoreCase))
                        {
                            abstractY0 = para.Y0;
                            break;
                        }
                    }
                    if (abstractY0 > 0)
                    {
                        foreach (var para in pageList)
                        {
                            if (para.Y0 > abstractY0 && para.AverageFontSize < 15.0)
                            {
                                para.IsBypassed = true;
                            }
                        }
                    }
                }

                // Pass 1.5: Mark table paragraphs geometrically
                MarkTableParagraphs(pageList, page.Width);

                // Pass 2: Propagate bypass to nearby small/label paragraphs (e.g. annotations inside drawings)
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var para in pageList)
                    {
                        if (para.IsBypassed) continue;
                        if (para.IsTable) continue; // Prevent table/diagram cells from being bypassed by proximity
                        
                        bool isSmallLabel = para.TextWithPlaceholders.Length <= 20 && !IsHeadingParagraph(para);
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

                MergeVerticallyAdjacentParagraphs(pageList);


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

                var paragraphsToTranslate = new List<PdfParagraph>();
                var textsToTranslate = new List<string>();

                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed)
                    {
                        para.TranslatedText = para.TextWithPlaceholders;
                    }
                    else
                    {
                        paragraphsToTranslate.Add(para);
                        textsToTranslate.Add(para.TextWithPlaceholders);
                    }
                }

                if (textsToTranslate.Count > 0)
                {
                    try
                    {
                        var results = translator.TranslateBatchAsync(textsToTranslate, targetLang, cancellationToken).GetAwaiter().GetResult();
                        if (results.Count == paragraphsToTranslate.Count)
                        {
                            for (int i = 0; i < results.Count; i++)
                            {
                                string rawResult = string.IsNullOrWhiteSpace(results[i]) 
                                    ? paragraphsToTranslate[i].TextWithPlaceholders 
                                    : results[i];
                                paragraphsToTranslate[i].TranslatedText = PostProcessTranslation(
                                    paragraphsToTranslate[i].TextWithPlaceholders,
                                    rawResult,
                                    targetLang
                                );
                            }
                        }
                        else
                        {
                            throw new Exception("Mismatched batch translation results count.");
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
                            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {p + 1}] Batch translation failed, falling back to sequential. Error: {ex.Message}{Environment.NewLine}";
                            lock (logLock)
                            {
                                File.AppendAllText(logPath, logLine);
                            }
                        }
                        catch { }

                        foreach (var para in paragraphsToTranslate)
                        {
                            try
                            {
                                string result = translator.TranslateAsync(para.TextWithPlaceholders, targetLang, cancellationToken).GetAwaiter().GetResult();
                                string rawResult = string.IsNullOrWhiteSpace(result) ? para.TextWithPlaceholders : result;
                                para.TranslatedText = PostProcessTranslation(
                                    para.TextWithPlaceholders,
                                    rawResult,
                                    targetLang
                                );
                            }
                            catch (Exception exSub)
                            {
                                para.TranslatedText = para.TextWithPlaceholders;
                                try
                                {
                                    string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
                                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {p + 1}] Sequential fallback error: {exSub.Message}{Environment.NewLine}";
                                    lock (logLock)
                                    {
                                        File.AppendAllText(logPath, logLine);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }

            onProgress?.Invoke(80, 100, "正在重建 PDF 佈局與公式...");
            cancellationToken.ThrowIfCancellationRequested();


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

                var paragraphs = pageParagraphs[p];
                if (paragraphs.Count == 0) continue;

                // Map annotations to paragraphs
                try
                {
                    for (int i = 0; i < page.Annotations.Count; i++)
                    {
                        var annot = page.Annotations[i];
                        var rect = annot.Rectangle;
                        
                        var paraOverlaps = new Dictionary<PdfParagraph, List<PdfLetter>>();
                        foreach (var para in paragraphs)
                        {
                            var overlapping = para.AllLetters
                                .Where(l => l.Left >= rect.X1 - 2.5 && l.Right <= rect.X2 + 2.5 &&
                                            l.Bottom >= rect.Y1 - 2.5 && l.Top <= rect.Y2 + 2.5)
                                .OrderBy(l => l.X)
                                .ToList();
                            if (overlapping.Count > 0)
                            {
                                paraOverlaps[para] = overlapping;
                            }
                        }
                        
                        if (paraOverlaps.Count > 0)
                        {
                            var bestPair = paraOverlaps.OrderByDescending(kv => kv.Value.Count).First();
                            var bestPara = bestPair.Key;
                            var overlappingLetters = bestPair.Value;
                            
                            string searchText = string.Join("", overlappingLetters.Select(l => l.Value)).Trim();
                            if (!string.IsNullOrEmpty(searchText))
                            {
                                int occurrenceIdx = GetOccurrenceIndex(bestPara.AllLetters, overlappingLetters, searchText);
                                bestPara.Annotations.Add(new ParagraphAnnotationInfo
                                {
                                    PdfAnnotation = annot,
                                    Text = searchText,
                                    OccurrenceIndex = occurrenceIdx
                                });
                            }
                        }
                    }
                }
                catch { }

                // Check if the page has tables (if so, we use white masks to preserve the original tables)
                bool pageHasTable = paragraphs.Any(para => para.IsTable);

                // Clean the page's original English text streams before adding overlays
                try
                {
                    StripTextFromPage(page);
                }
                catch { }

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
                try
                {
                    gfx.Internals.ContentStringBuilder.Append(" /NormalState gs ");
                }
                catch { }

                // Adjust layout for reference paragraphs to prevent overlaps
                try
                {
                    AdjustParagraphLayout(paragraphs, gfx, targetFontName);
                }
                catch { }

                // Pass 1: Draw white masks ONLY for translated paragraphs
                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed) continue;
                    if (para.IsTable) continue; // Skip table cells/diagram boxes to avoid erasing lines
                    if (string.IsNullOrWhiteSpace(para.TranslatedText)) continue;

                    double pageHeight = gfx.PageSize.Height;
                    double paragraphX = para.OriginalX0 - 1.5;
                    double paragraphY = pageHeight - para.OriginalY1 - 1.5;  // TOP of paragraph in PDFsharp coords
                    double paragraphWidth = (para.OriginalX1 - para.OriginalX0) + 3.0;
                    double paragraphHeight = (para.OriginalY1 - para.OriginalY0) + 3.0;

                    gfx.DrawRectangle(XBrushes.White, paragraphX, paragraphY, paragraphWidth, paragraphHeight);
                }

                // Pass 2: Render all paragraphs (translated overlays and selectively redrawn bypassed text)
                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed)
                    {
                        // Skip math equations and code blocks as their fonts were not stripped
                        if (para.IsCode || para.IsOnlyMath || IsEquationParagraph(para)) continue;

                        // Redraw table cells, headings, and other text in English
                        RenderParagraph(gfx, para, targetFontName);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(para.TranslatedText)) continue;
                        RenderParagraph(gfx, para, targetFontName);
                    }
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
                    var localFontsToStrip = new HashSet<string>(fontsToStrip);
                    var resources = dict.Elements.GetDictionary("/Resources");
                    if (resources != null)
                    {
                        var fonts = resources.Elements.GetDictionary("/Font");
                        if (fonts != null)
                        {
                            localFontsToStrip.Clear();
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

                                        bool isMathOrCode = PdfParagraph.MathFontRegex.IsMatch(cleanFontName);
                                        if (!isMathOrCode)
                                        {
                                            localFontsToStrip.Add(key.ToString().TrimStart('/'));
                                        }
                                    }
                                }
                            }
                        }
                    }

                    byte[] decompressedBytes = dict.Stream.UnfilteredValue;
                    byte[] cleanBytes = StripSelectedText(decompressedBytes, localFontsToStrip);
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

        public static bool IsLineBold(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line)
        {
            if (line == null || line.Words == null || line.Words.Count == 0) return false;
            int totalCount = 0;
            int boldCount = 0;
            foreach (var word in line.Words)
            {
                foreach (var letter in word.Letters)
                {
                    totalCount++;
                    if (letter.FontName != null)
                    {
                        if (letter.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                            letter.FontName.Contains("Medi", StringComparison.OrdinalIgnoreCase) ||
                            letter.FontName.Contains("bx", StringComparison.OrdinalIgnoreCase) ||
                            letter.FontName.Contains("bf", StringComparison.OrdinalIgnoreCase))
                        {
                            boldCount++;
                        }
                    }
                }
            }
            return totalCount > 0 && ((double)boldCount / totalCount) > 0.5;
        }

        public static string NormalizeMathValue(string val)
        {
            if (string.IsNullOrEmpty(val)) return val;
            var sb = new StringBuilder();
            for (int i = 0; i < val.Length; i++)
            {
                int cp = val[i];
                if (i < val.Length - 1 && char.IsHighSurrogate(val[i]) && char.IsLowSurrogate(val[i + 1]))
                {
                    cp = char.ConvertToUtf32(val[i], val[i + 1]);
                    i++;
                }

                // Map Plane 1 Mathematical Alphanumeric Symbols to ASCII
                if (cp >= 0x1D400 && cp <= 0x1D7FF)
                {
                    // Bold
                    if (cp >= 0x1D400 && cp <= 0x1D419) sb.Append((char)('A' + (cp - 0x1D400)));
                    else if (cp >= 0x1D41A && cp <= 0x1D433) sb.Append((char)('a' + (cp - 0x1D41A)));
                    // Italic
                    else if (cp >= 0x1D434 && cp <= 0x1D44D) sb.Append((char)('A' + (cp - 0x1D434)));
                    else if (cp >= 0x1D44E && cp <= 0x1D467) sb.Append((char)('a' + (cp - 0x1D44E)));
                    // Bold Italic
                    else if (cp >= 0x1D468 && cp <= 0x1D481) sb.Append((char)('A' + (cp - 0x1D468)));
                    else if (cp >= 0x1D482 && cp <= 0x1D49B) sb.Append((char)('a' + (cp - 0x1D482)));
                    // Script
                    else if (cp >= 0x1D49C && cp <= 0x1D4B5) sb.Append((char)('A' + (cp - 0x1D49C)));
                    else if (cp >= 0x1D4B6 && cp <= 0x1D4CF) sb.Append((char)('a' + (cp - 0x1D4B6)));
                    // Bold Script
                    else if (cp >= 0x1D4D0 && cp <= 0x1D4E9) sb.Append((char)('A' + (cp - 0x1D4D0)));
                    else if (cp >= 0x1D4EA && cp <= 0x1D503) sb.Append((char)('a' + (cp - 0x1D4EA)));
                    // Fraktur
                    else if (cp >= 0x1D504 && cp <= 0x1D51D) sb.Append((char)('A' + (cp - 0x1D504)));
                    else if (cp >= 0x1D51E && cp <= 0x1D537) sb.Append((char)('a' + (cp - 0x1D51E)));
                    // Double-struck
                    else if (cp >= 0x1D538 && cp <= 0x1D551) sb.Append((char)('A' + (cp - 0x1D538)));
                    else if (cp >= 0x1D552 && cp <= 0x1D56B) sb.Append((char)('a' + (cp - 0x1D552)));
                    // Bold Fraktur
                    else if (cp >= 0x1D56C && cp <= 0x1D585) sb.Append((char)('A' + (cp - 0x1D56C)));
                    else if (cp >= 0x1D586 && cp <= 0x1D59F) sb.Append((char)('a' + (cp - 0x1D586)));
                    // Sans-serif
                    else if (cp >= 0x1D5A0 && cp <= 0x1D5B9) sb.Append((char)('A' + (cp - 0x1D5A0)));
                    else if (cp >= 0x1D5BA && cp <= 0x1D5D3) sb.Append((char)('a' + (cp - 0x1D5BA)));
                    // Sans-serif Bold
                    else if (cp >= 0x1D5D4 && cp <= 0x1D5ED) sb.Append((char)('A' + (cp - 0x1D5D4)));
                    else if (cp >= 0x1D5EE && cp <= 0x1D607) sb.Append((char)('a' + (cp - 0x1D5EE)));
                    // Sans-serif Italic
                    else if (cp >= 0x1D608 && cp <= 0x1D621) sb.Append((char)('A' + (cp - 0x1D608)));
                    else if (cp >= 0x1D622 && cp <= 0x1D63B) sb.Append((char)('a' + (cp - 0x1D622)));
                    // Sans-serif Bold Italic
                    else if (cp >= 0x1D63C && cp <= 0x1D655) sb.Append((char)('A' + (cp - 0x1D63C)));
                    else if (cp >= 0x1D656 && cp <= 0x1D66F) sb.Append((char)('a' + (cp - 0x1D656)));
                    // Monospace
                    else if (cp >= 0x1D670 && cp <= 0x1D689) sb.Append((char)('A' + (cp - 0x1D670)));
                    else if (cp >= 0x1D68A && cp <= 0x1D6A3) sb.Append((char)('a' + (cp - 0x1D68A)));
                    // Math Bold Greek
                    else if (cp >= 0x1D6A8 && cp <= 0x1D6C0) sb.Append((char)(0x0391 + (cp - 0x1D6A8)));
                    else if (cp >= 0x1D6C2 && cp <= 0x1D6DA) sb.Append((char)(0x03B1 + (cp - 0x1D6C2)));
                    // Math Italic Greek
                    else if (cp >= 0x1D6E2 && cp <= 0x1D6FA) sb.Append((char)(0x0391 + (cp - 0x1D6E2)));
                    else if (cp >= 0x1D6FC && cp <= 0x1D714) sb.Append((char)(0x03B1 + (cp - 0x1D6FC)));
                    // Math Bold Italic Greek
                    else if (cp >= 0x1D71C && cp <= 0x1D734) sb.Append((char)(0x0391 + (cp - 0x1D71C)));
                    else if (cp >= 0x1D736 && cp <= 0x1D74E) sb.Append((char)(0x03B1 + (cp - 0x1D736)));
                    // Math Sans-serif Bold Greek
                    else if (cp >= 0x1D756 && cp <= 0x1D76E) sb.Append((char)(0x0391 + (cp - 0x1D756)));
                    else if (cp >= 0x1D770 && cp <= 0x1D788) sb.Append((char)(0x03B1 + (cp - 0x1D770)));
                    // Math Sans-serif Bold Italic Greek
                    else if (cp >= 0x1D790 && cp <= 0x1D7A8) sb.Append((char)(0x0391 + (cp - 0x1D790)));
                    else if (cp >= 0x1D7AA && cp <= 0x1D7C2) sb.Append((char)(0x03B1 + (cp - 0x1D7AA)));
                    // Math Digits
                    else if (cp >= 0x1D7CE && cp <= 0x1D7D7) sb.Append((char)('0' + (cp - 0x1D7CE)));
                    else if (cp >= 0x1D7D8 && cp <= 0x1D7E1) sb.Append((char)('0' + (cp - 0x1D7D8)));
                    else if (cp >= 0x1D7E2 && cp <= 0x1D7EB) sb.Append((char)('0' + (cp - 0x1D7E2)));
                    else if (cp >= 0x1D7EC && cp <= 0x1D7F5) sb.Append((char)('0' + (cp - 0x1D7EC)));
                    else if (cp >= 0x1D7F6 && cp <= 0x1D7FF) sb.Append((char)('0' + (cp - 0x1D7F6)));
                    else sb.Append(char.ConvertFromUtf32(cp));
                }
                else
                {
                    sb.Append(char.ConvertFromUtf32(cp));
                }
            }
            return sb.ToString();
        }

        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang)
        {
            if (string.IsNullOrEmpty(translatedText)) return translatedText;

            // 1. Restore email addresses
            try
            {
                var emailRegex = new System.Text.RegularExpressions.Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                var originalEmails = emailRegex.Matches(originalText).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToList();
                if (originalEmails.Count > 0)
                {
                    var transEmailRegex = new System.Text.RegularExpressions.Regex(@"[^\s@]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                    var transMatches = transEmailRegex.Matches(translatedText).Cast<System.Text.RegularExpressions.Match>().ToList();
                    for (int i = 0; i < Math.Min(originalEmails.Count, transMatches.Count); i++)
                    {
                        int index = translatedText.IndexOf(transMatches[i].Value);
                        if (index >= 0)
                        {
                            translatedText = translatedText.Remove(index, transMatches[i].Value.Length).Insert(index, originalEmails[i]);
                        }
                    }
                }
            }
            catch { }

            // 2. Terminology replacements
            if (targetLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                bool isTraditional = !targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);

                // ABSTRACT / Abstract -> 摘要
                if (originalText.Trim().Equals("ABSTRACT", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = "摘要";
                }
                else
                {
                    translatedText = translatedText.Replace("抽象", "摘要");
                }

                // titles -> 作品
                if (originalText.Contains("title", StringComparison.OrdinalIgnoreCase) && !originalText.Contains("entitle", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("標題", isTraditional ? "作品" : "作品");
                    translatedText = translatedText.Replace("标题", "作品");
                }

                // features -> 特徵
                if (originalText.Contains("features", StringComparison.OrdinalIgnoreCase) || originalText.Contains("feature", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("功能", isTraditional ? "特徵" : "特征");
                    translatedText = translatedText.Replace("特性", isTraditional ? "特徵" : "特征");
                }

                // character -> 角色
                if (originalText.Contains("character", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("字元", "角色");
                    translatedText = translatedText.Replace("字符", "角色");
                }

                // LLM -> 大型語言模型 / 大型语言模型
                if (originalText.Contains("LLM", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("法學碩士", isTraditional ? "大型語言模型" : "大型语言模型");
                    translatedText = translatedText.Replace("法学硕士", isTraditional ? "大型語言模型" : "大型语言模型");
                }

                // sink -> 接收端 / 接收器
                if (originalText.Contains("sink", StringComparison.OrdinalIgnoreCase))
                {
                    translatedText = translatedText.Replace("水槽", isTraditional ? "接收端" : "接收器");
                }
            }

            // 3. Remove stray formula-bracket artifacts like '):(Equation (1))' or '):' that appear
            //    when the formula extractor incorrectly consumed the opening '(' of a parenthetical phrase,
            //    leaving only the closing ')' in the text string.
            try
            {
                // If the ENTIRE text is a stray artifact (e.g. "InfoNCE):(Equation (1))")
                // detect: word-part + ')' + ':' + '(' + ... + ')'
                var fullArtifactRegex = new System.Text.RegularExpressions.Regex(
                    @"^(.+?)\)\s*:\s*\(.+\)\s*$",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var fullMatch = fullArtifactRegex.Match(translatedText.Trim());
                if (fullMatch.Success)
                {
                    // Keep only the part before the stray ')'
                    translatedText = fullMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Partial: remove trailing '):(something)' from otherwise normal text
                    var trailingArtifact = new System.Text.RegularExpressions.Regex(
                        @"\)\s*:\s*\(.+\)\s*$",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    translatedText = trailingArtifact.Replace(translatedText, "").Trim();
                }

                // Also remove orphan '):(...)' that starts the string
                var leadingArtifact = new System.Text.RegularExpressions.Regex(
                    @"^\)\s*:\s*",
                    System.Text.RegularExpressions.RegexOptions.None);
                translatedText = leadingArtifact.Replace(translatedText, "").Trim();
            }
            catch { }

            return translatedText;
        }

        private static XFont GetMathFont(string originalFontName, double fontSize)
        {
            bool isItalic = originalFontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                            originalFontName.Contains("CMMI", StringComparison.OrdinalIgnoreCase) ||
                            originalFontName.Contains("mi", StringComparison.OrdinalIgnoreCase);
            bool isBold = originalFontName.Contains("Bold", StringComparison.OrdinalIgnoreCase);

            var style = XFontStyleEx.Regular;
            if (isItalic && isBold) style = XFontStyleEx.BoldItalic;
            else if (isItalic) style = XFontStyleEx.Italic;
            else if (isBold) style = XFontStyleEx.Bold;

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

                gfx.DrawString(letter.Value.Normalize(NormalizationForm.FormKD), font, brush, x, y);
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

        private static bool IsLatinExtendedOrSymbol(char c)
        {
            if (c >= 0x0080 && c <= 0x024F) return true;
            return IsMathOrGreekCharacter(c);
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

            // If the formula contains any math or Greek characters, don't merge it so they can be drawn with custom fonts
            foreach (var l in formula.Letters)
            {
                if (l.Value.Length == 1 && IsMathOrGreekCharacter(l.Value[0]))
                {
                    return false;
                }
            }

            double minY = formula.Letters.Min(l => l.RelativeY);
            double maxY = formula.Letters.Max(l => l.RelativeY);
            double yDiff = maxY - minY;

            if (yDiff > averageFontSize * 0.15) return false;

            return true;
        }

        private static void AdjustParagraphLayout(List<PdfParagraph> paragraphs, XGraphics gfx, string targetFontName)
        {
            // Disabled shifting to prevent layout collisions and scrambled reference blocks
        }

        public static bool StartsNewParagraphOrSection(string text)
        {
            string trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return false;

            if (trimmed.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Keyword", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("關鍵字", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("关键字", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Matches "[1]", "1.", "1)", "a.", "a)", "•", "-", "*"
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^(?:\[\d+\]|\d+[\.\)]|[a-zA-Z][\.\)]|[•\-\*])(?:\s|$)")) return true;

            // Check if it's a section header
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d{1,2}(?:\.\d{1,2}){0,4}\.?(?:\s+[^a-z]|$)")) return true;
            if (trimmed.Length < 30 && trimmed.Any(char.IsLetter) && trimmed.All(c => !char.IsLower(c))) return true;

            // Check for Table/Figure/RQ captions/headings to prevent them from merging with nearby text blocks
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^(?:Table|Figure|Fig|表|圖|RQ\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;

            return false;
        }

        public static bool IsHeadingLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line)
        {
            string txt = line.Text.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            // Section numbering like "1. Introduction" or "3.4.1 Projection before Fusion" or "3.2.1 資料收集"
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2}){0,4}\.?(?:\s+[^a-z]|$)")) return true;

            // Uppercase section headers like "REFERENCES", "ABSTRACT", "APPENDIX"
            if (txt.Length < 30 && txt.Any(char.IsLetter) && txt.All(c => !char.IsLower(c))) return true;

            return false;
        }

        public class MergedBlock
        {
            public List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> TextLines { get; set; } = new();
            public double Right { get; set; }
        }

        public static List<MergedBlock> GetMergedBlocks(IEnumerable<UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock> docstrumBlocks, double pageWidth, bool isTablePage = false)
        {
            double maxGap = isTablePage ? 8.0 : 15.0;
            double center = pageWidth / 2.0;
            var list = docstrumBlocks.Select(b => new MergedBlock
            {
                TextLines = b.TextLines.ToList(),
                Right = b.BoundingBox.Right
            }).ToList();

            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var b1 = list[i];
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var b2 = list[j];

                        // Check if they should be merged horizontally
                        bool canMerge = false;
                        foreach (var l1 in b1.TextLines)
                        {
                            foreach (var l2 in b2.TextLines)
                            {
                                double verticalOverlap = Math.Min(l1.BoundingBox.Top, l2.BoundingBox.Top) - Math.Max(l1.BoundingBox.Bottom, l2.BoundingBox.Bottom);
                                double minHeight = Math.Min(l1.BoundingBox.Height, l2.BoundingBox.Height);
                                if (minHeight <= 0 || verticalOverlap / minHeight <= 0.5) continue;

                                // Check gap between their horizontal boundaries
                                double gap = l1.BoundingBox.Left < l2.BoundingBox.Left
                                    ? l2.BoundingBox.Left - l1.BoundingBox.Right
                                    : l1.BoundingBox.Left - l2.BoundingBox.Right;

                                double c1 = (l1.BoundingBox.Left + l1.BoundingBox.Right) / 2.0;
                                double c2 = (l2.BoundingBox.Left + l2.BoundingBox.Right) / 2.0;
                                bool isL1Left = c1 < center;
                                bool isL2Left = c2 < center;
                                double allowedGap = (isL1Left != isL2Left) ? 5.0 : maxGap;

                                if (gap >= -5.0 && gap <= allowedGap)
                                {
                                    canMerge = true;
                                    break;
                                }
                            }
                            if (canMerge) break;
                        }

                        if (canMerge)
                        {
                            // Merge b2 into b1
                            b1.TextLines.AddRange(b2.TextLines);
                            b1.Right = Math.Max(b1.Right, b2.Right);

                            list.RemoveAt(j);
                            mergedAny = true;
                            break;
                        }
                    }
                    if (mergedAny) break;
                }
            }

            return list;
        }

        private static void MergeVerticallyAdjacentParagraphs(List<PdfParagraph> paragraphs)
        {
            if (paragraphs.Count <= 1) return;

            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                // Sort by Y1 descending (top to bottom on the page)
                var sorted = paragraphs.OrderByDescending(p => p.Y1).ToList();

                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var p1 = sorted[i];
                    if (p1.IsBypassed || string.IsNullOrWhiteSpace(p1.TextWithPlaceholders)) continue;

                    // If p1 is a heading, do not merge anything into it
                    if (IsHeadingParagraph(p1)) continue;

                    // If p1 ends with sentence-ending punctuation, do not merge subsequent paragraphs
                    string clean1 = p1.TextWithPlaceholders.Trim();
                    if (clean1.EndsWith(".") || clean1.EndsWith("?") || clean1.EndsWith("!") || clean1.EndsWith(":") || 
                        clean1.EndsWith("。") || clean1.EndsWith("」") || clean1.EndsWith("\""))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < sorted.Count; j++)
                    {
                        var p2 = sorted[j];
                        if (p2.IsBypassed || string.IsNullOrWhiteSpace(p2.TextWithPlaceholders)) continue;

                        // Check same column / horizontal overlap > 60%
                        double minWidth = Math.Min(p1.Width, p2.Width);
                        if (minWidth <= 0) continue;

                        double overlap = Math.Min(p1.X1, p2.X1) - Math.Max(p1.X0, p2.X0);
                        if (overlap / minWidth <= 0.6) continue;

                        // Check vertical gap
                        double gap = p1.Y0 - p2.Y1;

                        // Allow a vertical gap of up to 6 pt (tightened from 14 pt to prevent paragraph merging)
                        if (gap > 6 || gap < -10) continue;

                        // Ensure p2 does not start a new list item, reference, or heading
                        if (StartsNewParagraphOrSection(p2.TextWithPlaceholders)) continue;

                        // Merge p2 into p1
                        p1.MergeWith(p2);

                        // Remove p2 from the lists
                        paragraphs.Remove(p2);
                        mergedAny = true;
                        break;
                    }
                    if (mergedAny) break;
                }
            }
        }

        private static bool IsHeadingParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            if (txt.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("Keyword", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("關鍵字", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("关键字", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Section numbering like "1. Introduction" or "3.4.1 Projection before Fusion" or "3.2.1 資料收集"
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2}){0,4}\.?(?:\s+[^a-z]|$)")) return true;

            // Uppercase section headers like "REFERENCES", "ABSTRACT", "APPENDIX"
            if (txt.Length < 30 && txt.Any(char.IsLetter) && txt.All(c => !char.IsLower(c))) return true;

            return false;
        }

        private static bool IsReferenceParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\[\d+\]") ||
                   txt.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                   txt.Contains("doi:", StringComparison.OrdinalIgnoreCase) ||
                   txt.Contains("www.", StringComparison.OrdinalIgnoreCase);
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

        private static void MarkTableParagraphs(List<PdfParagraph> pageList, double pageWidth)
        {
            var candidates = new List<PdfParagraph>();
            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (string.IsNullOrEmpty(txt)) continue;

                if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsEquationParagraph(para)) continue;

                // Exclude citations, references, and links from becoming table candidates
                if (txt.StartsWith("[") ||
                    txt.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.IndexOf("doi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.IndexOf("www.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"\b10\.\d{4,}/"))
                {
                    continue;
                }

                // Exclude list labels (e.g. "1.", "2.", "a.", "(1)")
                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^(?:\d+|[a-zA-Z])\.$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\((?:\d+|[a-zA-Z])\)$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(txt, @"^(?:\d+\.\s*)+$"))
                {
                    continue;
                }

                // Exclude section numbering headings (e.g. "3.2", "3.2.1", "10. WORK DIVISION")
                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+(?:\.\d+)*\.?\s+[A-Z]"))
                {
                    continue;
                }

                // Exclude single character / punctuation-only paragraphs
                if (txt.Length <= 2 && !System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[0-9✓xX-]$"))
                {
                    continue;
                }

                string[] allWords = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (allWords.Length > 10) continue;

                if (para.Width < pageWidth * 0.45 && para.Height < 60)
                {
                    int rowAlignedCount = 0;
                    int colAlignedCount = 0;

                    foreach (var other in pageList)
                    {
                        if (other == para) continue;

                        double overlapY = Math.Min(para.Y1, other.Y1) - Math.Max(para.Y0, other.Y0);
                        double minHeight = Math.Min(para.Height, other.Height);
                        if (overlapY > minHeight * 0.5)
                        {
                            rowAlignedCount++;
                        }

                        double overlapX = Math.Min(para.X1, other.X1) - Math.Max(para.X0, other.X0);
                        double minWidth = Math.Min(para.Width, other.Width);
                        if (overlapX > minWidth * 0.5)
                        {
                            colAlignedCount++;
                        }
                    }

                    if (rowAlignedCount >= 1 && colAlignedCount >= 1)
                    {
                        candidates.Add(para);
                    }
                }
            }

            if (candidates.Count < 4) return;

            var groups = new List<List<PdfParagraph>>();
            foreach (var cand in candidates)
            {
                bool added = false;
                foreach (var group in groups)
                {
                    bool close = false;
                    foreach (var member in group)
                    {
                        double center = pageWidth / 2;
                        bool candIsLeft = cand.X1 <= center + 5;
                        bool memberIsLeft = member.X1 <= center + 5;
                        if (candIsLeft != memberIsLeft) continue;
                        double verticalDist = 0;
                        if (cand.Y1 < member.Y0)
                        {
                            verticalDist = member.Y0 - cand.Y1;
                        }
                        else if (member.Y1 < cand.Y0)
                        {
                            verticalDist = cand.Y0 - member.Y1;
                        }
                        else
                        {
                            verticalDist = 0;
                        }

                        // Tightened threshold from 80 to 45 to prevent multi-column chaining
                        if (verticalDist < 45)
                        {
                            close = true;
                            break;
                        }
                    }
                    if (close)
                    {
                        group.Add(cand);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<PdfParagraph> { cand });
                }
            }

            foreach (var group in groups)
            {
                if (group.Count < 4) continue;

                // Enforce that a table group must have at least one pair of horizontally adjacent cells
                bool hasHorizontalPair = false;
                for (int i = 0; i < group.Count; i++)
                {
                    for (int j = i + 1; j < group.Count; j++)
                    {
                        var p1 = group[i];
                        var p2 = group[j];
                        double overlapY = Math.Min(p1.Y1, p2.Y1) - Math.Max(p1.Y0, p2.Y0);
                        double minH = Math.Min(p1.Height, p2.Height);
                        if (overlapY > minH * 0.5)
                        {
                            double overlapX = Math.Min(p1.X1, p2.X1) - Math.Max(p1.X0, p2.X0);
                            if (overlapX <= 0) // No horizontal overlap means they are side-by-side
                            {
                                hasHorizontalPair = true;
                                break;
                            }
                        }
                    }
                    if (hasHorizontalPair) break;
                }
                if (!hasHorizontalPair) continue;

                foreach (var member in group)
                {
                    member.IsTable = true;
                }

                double minY = group.Min(p => p.Y0);
                double maxY = group.Max(p => p.Y1);
                double minX = group.Min(p => p.X0);
                double maxX = group.Max(p => p.X1);

                minY -= 15;
                maxY += 15;
                minX -= 15;
                maxX += 15;

                foreach (var para in pageList)
                {
                    string txt = para.TextWithPlaceholders.Trim();
                    if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double centerX = para.X0 + para.Width / 2;
                    double centerY = para.Y0 + para.Height / 2;

                    if (centerX >= minX && centerX <= maxX && centerY >= minY && centerY <= maxY)
                    {
                        string[] words = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        // Increased word count limit to 150 to allow long cell descriptions (like work division) to be bypassed
                        if (words.Length <= 150)
                        {
                            para.IsTable = true;
                        }
                    }
                }
            }
        }

        private static bool IsTableParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return true;

            int letterCount = txt.Count(char.IsLetter);
            if (letterCount == 0) return true;

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
            // StripFormXObjects(resources, fontsToStrip);

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
            else if (para.IsBypassed)
            {
                if (text.Any(IsCjkCharacter))
                {
                    fontNameForPara = targetFontName;
                }
                else
                {
                    fontNameForPara = "Times New Roman";
                    if (para.AllLetters.Count > 0)
                    {
                        string fn = para.AllLetters[0].FontName.ToLowerInvariant();
                        if (fn.Contains("times") || fn.Contains("serif") || fn.Contains("liberation"))
                            fontNameForPara = "Times New Roman";
                        else if (fn.Contains("arial") || fn.Contains("helvetica") || fn.Contains("sans"))
                            fontNameForPara = "Arial";
                        else if (fn.Contains("courier") || fn.Contains("mono") || fn.Contains("consolas"))
                            fontNameForPara = "Courier New";
                    }
                }
            }
            XFontStyleEx fontStyle = XFontStyleEx.Regular;
            if (para.IsBold || IsHeadingParagraph(para))
            {
                fontStyle = para.IsItalic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold;
            }
            else
            {
                fontStyle = para.IsItalic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
            }
            XFont mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
            XBrush brush = XBrushes.Black;

            // Handle rotations (90, 180, 270)
            bool isRotated = false;
            double layoutWidth = paragraphWidth;
            if (!isRotated && IsHeadingParagraph(para))
            {
                double pageCenter = gfx.PageSize.Width / 2.0;
                double maxBoundary = gfx.PageSize.Width - 54.0; // Default right margin
                
                // If it's in the left column, limit expansion to the middle of the page
                if (para.OriginalX1 <= pageCenter + 10.0)
                {
                    maxBoundary = pageCenter - 10.0;
                }

                double remainingWidth = maxBoundary - paragraphX;
                if (remainingWidth > layoutWidth)
                {
                    layoutWidth = remainingWidth;
                }
            }
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
            if (IsReferenceParagraph(para))
            {
                lineSpacingMultiplier = 1.15;
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
            var renderedChars = new List<RenderedChar>();

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
                            double my = currentY - avgY * formulaScale - (fontSize * 0.15);

                            string normText = NormalizeMathValue(mergedText.Normalize(NormalizationForm.FormKD));
                            gfx.DrawString(normText, mathFont, brush, currentX, my);
                            
                            double offset = 0;
                            for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                            {
                                char ch = normText[cIdx];
                                double mChW = gfx.MeasureString(ch.ToString(), mathFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = ch,
                                    Left = currentX + offset,
                                    Right = currentX + offset + mChW,
                                    Bottom = pageHeight - my - fSize * 0.15,
                                    Top = pageHeight - my + fSize * 0.85
                                });
                                offset += mChW;
                            }
                        }
                        else
                        {
                            foreach (var ml in formula.Letters)
                            {
                                double fSize = ml.FontSize * formulaScale;
                                XFont mathFont = GetMathFont(ml.FontName, fSize);

                                double mx = currentX + ml.RelativeX * formulaScale;
                                // Align math letter baseline with CJK baseline by shifting up slightly instead of down
                                double my = currentY - ml.RelativeY * formulaScale - (fontSize * 0.15);

                                string drawVal = NormalizeMathValue(ml.Value.Normalize(NormalizationForm.FormKD));
                                if (drawVal.Length == 1 && IsMathOrGreekCharacter(drawVal[0]))
                                {
                                    mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                                }

                                gfx.DrawString(drawVal, mathFont, brush, mx, my);
                                
                                double offset = 0;
                                for (int cIdx = 0; cIdx < drawVal.Length; cIdx++)
                                {
                                    char ch = drawVal[cIdx];
                                    double mlChW = gfx.MeasureString(ch.ToString(), mathFont).Width;
                                    renderedChars.Add(new RenderedChar
                                    {
                                        Character = ch,
                                        Left = mx + offset,
                                        Right = mx + offset + mlChW,
                                        Bottom = pageHeight - my - fSize * 0.15,
                                        Top = pageHeight - my + fSize * 0.85
                                    });
                                    offset += mlChW;
                                }
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
                            if (elem.Text.Length == 1 && IsLatinExtendedOrSymbol(elem.Text[0]))
                            {
                                if (sbMerged.Length > 0)
                                {
                                    string normText = NormalizeMathValue(sbMerged.ToString().Normalize(NormalizationForm.FormKD));
                                    gfx.DrawString(normText, mainFont, brush, textStartX, currentY);
                                    
                                    double offset = 0;
                                    for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                                    {
                                        char ch = normText[cIdx];
                                        double tChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                                        renderedChars.Add(new RenderedChar
                                        {
                                            Character = ch,
                                            Left = textStartX + offset,
                                            Right = textStartX + offset + tChW,
                                            Bottom = pageHeight - currentY - fontSize * 0.15,
                                            Top = pageHeight - currentY + fontSize * 0.85
                                        });
                                        offset += tChW;
                                    }
                                    sbMerged.Clear();
                                }
                                char c = elem.Text[0];
                                string fallbackFontName;
                                if (c >= 0x0080 && c <= 0x024F)
                                {
                                    fallbackFontName = mainFont.FontFamily.Name.Contains("Courier") ? "Courier New" : "Arial";
                                }
                                else
                                {
                                    fallbackFontName = "Segoe UI Symbol";
                                }
                                XFont fallbackFont = new XFont(fallbackFontName, mainFont.Size, mainFont.Style);
                                string normChar = NormalizeMathValue(elem.Text.Normalize(NormalizationForm.FormKD));
                                gfx.DrawString(normChar, fallbackFont, brush, currentX, currentY);
                                
                                double fChW = gfx.MeasureString(normChar, fallbackFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = normChar[0],
                                    Left = currentX,
                                    Right = currentX + fChW,
                                    Bottom = pageHeight - currentY - fontSize * 0.15,
                                    Top = pageHeight - currentY + fontSize * 0.85
                                });
                                
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
                            string normText = NormalizeMathValue(sbMerged.ToString().Normalize(NormalizationForm.FormKD));
                            gfx.DrawString(normText, mainFont, brush, textStartX, currentY);
                            
                            double offset = 0;
                            for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                            {
                                char ch = normText[cIdx];
                                double eChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = ch,
                                    Left = textStartX + offset,
                                    Right = textStartX + offset + eChW,
                                    Bottom = pageHeight - currentY - fontSize * 0.15,
                                    Top = pageHeight - currentY + fontSize * 0.85
                                });
                                offset += eChW;
                            }
                        }
                    }
                }
                currentY += lineHeight;
            }

            if (state != null)
            {
                gfx.Restore(state);
            }

            // Align annotations
            if (!isRotated && para.Annotations.Count > 0 && renderedChars.Count > 0)
            {
                foreach (var annotInfo in para.Annotations)
                {
                    try
                    {
                        var matched = FindAnnotationCharacters(renderedChars, annotInfo.Text, annotInfo.OccurrenceIndex);
                        if (matched != null && matched.Count > 0)
                        {
                            double minLeft = matched.Min(rc => rc.Left);
                            double maxRight = matched.Max(rc => rc.Right);
                            double minBottom = matched.Min(rc => rc.Bottom);
                            double maxTop = matched.Max(rc => rc.Top);

                            double paddingX = 1.0;
                            double paddingY = 1.5;

                            annotInfo.PdfAnnotation.Rectangle = new PdfRectangle(
                                new XPoint(minLeft - paddingX, minBottom - paddingY),
                                new XPoint(maxRight + paddingX, maxTop + paddingY)
                            );
                        }
                    }
                    catch { }
                }
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
                if (c == '\n' || c == '\r')
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add("\n");
                    if (c == '\r' && i + 1 < len && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    i++;
                    continue;
                }

                if (IsCjkCharacter(c) || IsLatinExtendedOrSymbol(c))
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
                if (token == "\n")
                {
                    rows.Add(currentRow);
                    currentRow = new LayoutRow();
                    currentX = 0;
                    continue;
                }

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
                    else if (token.Length == 1 && IsLatinExtendedOrSymbol(token[0]))
                    {
                        char c = token[0];
                        string fontName;
                        if (c >= 0x0080 && c <= 0x024F)
                        {
                            fontName = font.FontFamily.Name.Contains("Courier") ? "Courier New" : "Arial";
                        }
                        else
                        {
                            fontName = "Segoe UI Symbol";
                        }
                        XFont fallbackFont = new XFont(fontName, font.Size, font.Style);
                        width = gfx.MeasureString(NormalizeMathValue(token), fallbackFont).Width;
                    }
                    else
                    {
                        width = gfx.MeasureString(NormalizeMathValue(token), font).Width;
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
                            double subWidth = gfx.MeasureString(NormalizeMathValue(sub), font).Width;
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

        private static bool HasColumnGap(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line, double minGap = 20.0)
        {
            if (line == null || line.Words.Count <= 1) return false;
            var sortedWords = line.Words.OrderBy(w => w.BoundingBox.Left).ToList();
            for (int i = 0; i < sortedWords.Count - 1; i++)
            {
                double gap = sortedWords[i + 1].BoundingBox.Left - sortedWords[i].BoundingBox.Right;
                if (gap >= minGap) return true;
            }
            return false;
        }

        private static bool CharEqualsNormalized(char c1, char c2)
        {
            if (c1 == c2) return true;
            if (char.ToLowerInvariant(c1) == char.ToLowerInvariant(c2)) return true;
            if ((c1 == '-' || c1 == '–' || c1 == '—') && (c2 == '-' || c2 == '–' || c2 == '—')) return true;
            return false;
        }

        private static int GetOccurrenceIndex(List<PdfLetter> allLetters, List<PdfLetter> targetLetters, string searchText)
        {
            if (allLetters == null || targetLetters == null || string.IsNullOrEmpty(searchText)) return 0;
            
            var occurrences = new List<int>();
            for (int i = 0; i <= allLetters.Count - searchText.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < searchText.Length; j++)
                {
                    if (allLetters[i + j].Value.Length == 0 || !CharEqualsNormalized(allLetters[i + j].Value[0], searchText[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    occurrences.Add(i);
                }
            }
            
            if (occurrences.Count <= 1) return 0;
            
            double targetAvgIndex = targetLetters.Average(tl => allLetters.IndexOf(tl));
            int bestIdx = 0;
            double minDist = double.MaxValue;
            for (int k = 0; k < occurrences.Count; k++)
            {
                double occurrenceAvgIndex = occurrences[k] + (searchText.Length - 1) / 2.0;
                double dist = Math.Abs(occurrenceAvgIndex - targetAvgIndex);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIdx = k;
                }
            }
            return bestIdx;
        }

        private static List<RenderedChar> FindAnnotationCharacters(List<RenderedChar> renderedChars, string searchText, int occurrenceIdx)
        {
            string cleanSearch = new string(searchText.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (cleanSearch.Length == 0) return null;

            var cleanRendered = renderedChars.Where(rc => !char.IsWhiteSpace(rc.Character)).ToList();
            
            var occurrences = new List<List<RenderedChar>>();
            for (int i = 0; i <= cleanRendered.Count - cleanSearch.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < cleanSearch.Length; j++)
                {
                    if (!CharEqualsNormalized(cleanRendered[i + j].Character, cleanSearch[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    occurrences.Add(cleanRendered.GetRange(i, cleanSearch.Length));
                }
            }

            if (occurrences.Count > 0)
            {
                int index = Math.Min(occurrenceIdx, occurrences.Count - 1);
                return occurrences[index];
            }

            // Fallback: search for numbers/roman numerals
            var numbers = new string(cleanSearch.Where(c => char.IsDigit(c) || "IVXLCDMivxlcdm".Contains(c)).ToArray());
            if (numbers.Length > 0)
            {
                var numOccurrences = new List<List<RenderedChar>>();
                for (int i = 0; i <= cleanRendered.Count - numbers.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < numbers.Length; j++)
                    {
                        if (!CharEqualsNormalized(cleanRendered[i + j].Character, numbers[j]))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        numOccurrences.Add(cleanRendered.GetRange(i, numbers.Length));
                    }
                }
                if (numOccurrences.Count > 0)
                {
                    int index = Math.Min(occurrenceIdx, numOccurrences.Count - 1);
                    return numOccurrences[index];
                }
            }

            return null;
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
            @"^(?:CMMI|CMSY|CMEX|CMIB|CMBSY|cmmi|cmsy|cmex|cmib|cmbsy|lasy|rsfs|txsy|wasy|stmary|XY|bbld|line\d*|lcircle\d*|TeX-|MS[AB]|MT(?:MI|SY|EX|2)|EU[RSF])|" +
            @"(?:Mono|Code|Math|Sym|Wingdings|Webdings|Dingbats|Courier|Console|Inconsolata|Typewriter|NimbusMon|MonL|cmtt|ectt|sftt|\btt\d+|Teletype)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
        );

        public string TextWithPlaceholders { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double OriginalX0 { get; private set; }
        public double OriginalY0 { get; private set; }
        public double OriginalX1 { get; private set; }
        public double OriginalY1 { get; private set; }
        public double AverageFontSize { get; set; }
        public bool IsOnlyMath { get; set; }
        public bool IsCode { get; set; }
        public bool IsBypassed { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsTable { get; set; }
        public bool brk { get; set; } // Paragraph line-break marker
        public List<MathFormula> Formulas { get; set; } = new List<MathFormula>();
        public object TextDirection { get; set; } = "Rotate0";
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public List<PdfLetter> AllLetters { get; set; } = new List<PdfLetter>();
        public List<ParagraphAnnotationInfo> Annotations { get; set; } = new();

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

        public PdfParagraph(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                X0 = 0; Y0 = 0; X1 = 0; Y1 = 0;
                return;
            }

            X0 = lines.Min(line => Math.Min(line.BoundingBox.Left, line.BoundingBox.Right));
            Y0 = lines.Min(line => Math.Min(line.BoundingBox.Bottom, line.BoundingBox.Top));
            X1 = lines.Max(line => Math.Max(line.BoundingBox.Left, line.BoundingBox.Right));
            Y1 = lines.Max(line => Math.Max(line.BoundingBox.Bottom, line.BoundingBox.Top));

            OriginalX0 = X0;
            OriginalY0 = Y0;
            OriginalX1 = X1;
            OriginalY1 = Y1;

            // Determine dominant text direction
            var directions = new Dictionary<object, int>();
            foreach (var line in lines)
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

            AnalyzeLines(lines);

            // Detect alignment
            double totalLeftGap = 0;
            double totalRightGap = 0;
            int lineCountWithGaps = 0;
            foreach (var line in lines)
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

            // Reference alignment force-left rule
            string trimmedText = TextWithPlaceholders.Trim();
            bool isReference = System.Text.RegularExpressions.Regex.IsMatch(trimmedText, @"^\[\d+\]") ||
                              trimmedText.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                              trimmedText.Contains("doi:", StringComparison.OrdinalIgnoreCase) ||
                              trimmedText.Contains("www.", StringComparison.OrdinalIgnoreCase);

            if (isReference)
            {
                Alignment = TextAlignment.Left;
            }
        }

        private void AnalyzeLines(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> lines)
        {
            var sb = new StringBuilder();
            var currentFormula = new List<UglyToad.PdfPig.Content.Letter>();
            int bracketsCount = 0;

            double totalFontSize = 0;
            int letterCount = 0;

            int boldCount = 0;
            int italicCount = 0;
            int totalCount = 0;
            // Compute average font size first
            foreach (var line in lines)
            {
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        totalFontSize += letter.PointSize;
                        letterCount++;

                        totalCount++;
                        if (letter.FontName != null)
                        {
                            if (letter.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("Medi", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("bx", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("bf", StringComparison.OrdinalIgnoreCase))
                            {
                                boldCount++;
                            }
                            if (letter.FontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("it", StringComparison.OrdinalIgnoreCase) ||
                                letter.FontName.Contains("ob", StringComparison.OrdinalIgnoreCase))
                            {
                                italicCount++;
                            }
                        }

                        AllLetters.Add(new PdfLetter
                        {
                            Value = letter.Value ?? "",
                            FontName = letter.FontName ?? "Times New Roman",
                            FontSize = letter.PointSize,
                            X = letter.Location.X,
                            Y = letter.Location.Y,
                            Left = letter.GlyphRectangle.Left,
                            Bottom = letter.GlyphRectangle.Bottom,
                            Right = letter.GlyphRectangle.Right,
                            Top = letter.GlyphRectangle.Top
                        });
                    }
                }
            }
            AverageFontSize = letterCount > 0 ? totalFontSize / letterCount : 10;
            IsBold = totalCount > 0 && ((double)boldCount / totalCount) > 0.5;
            IsItalic = totalCount > 0 && ((double)italicCount / totalCount) > 0.5;

            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var line = lines[lineIdx];
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
                if (lineIdx < lines.Count - 1)
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
            IsCode = IsCodeBlock(TextWithPlaceholders) || IsMonospaceBlock(lines);
        }

        private bool IsMonospaceBlock(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> lines)
        {
            int monoCount = 0;
            int totalCount = 0;
            foreach (var line in lines)
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

        private static bool IsMathCodepoint(int codepoint)
        {
            if ((codepoint >= 0x0370 && codepoint <= 0x03FF) || (codepoint >= 0x1F00 && codepoint <= 0x1FFF)) return true; // Greek & Coptic
            if (codepoint >= 0x2200 && codepoint <= 0x22FF) return true; // Math Operators
            if (codepoint >= 0x2A00 && codepoint <= 0x2AFF) return true; // Supp Math Operators
            if (codepoint >= 0x2100 && codepoint <= 0x214F) return true; // Letterlike Symbols
            if (codepoint >= 0x2190 && codepoint <= 0x21FF) return true; // Arrows
            if (codepoint >= 0x27F0 && codepoint <= 0x27FF) return true; // Supp Arrows A
            if (codepoint >= 0x2900 && codepoint <= 0x297F) return true; // Supp Arrows B
            if ((codepoint >= 0x27C0 && codepoint <= 0x27EF) || (codepoint >= 0x2980 && codepoint <= 0x29FF)) return true; // Misc Math
            if (codepoint >= 0x1D400 && codepoint <= 0x1D7FF) return true; // Math Alphanumeric Symbols (Plane 1)
            return false;
        }

        private static System.Collections.Generic.IEnumerable<int> GetCodepoints(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            for (int i = 0; i < s.Length; i++)
            {
                if (i < s.Length - 1 && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                {
                    yield return char.ConvertToUtf32(s[i], s[i + 1]);
                    i++;
                }
                else
                {
                    yield return s[i];
                }
            }
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
                    foreach (int cp in GetCodepoints(letter.Value))
                    {
                        if (IsMathCodepoint(cp)) return true;
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

            if (letter.Value != null)
            {
                foreach (int cp in GetCodepoints(letter.Value))
                {
                    if (IsMathCodepoint(cp)) return true;
                }

                // Subscript/Superscript ratios (ONLY if the word is a math/code word!)
                if (isMathWord && letter.PointSize < AverageFontSize * 0.79) return true;
            }

            return false;
        }

        public static bool IsMathLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line.Text.Trim(), @"\(\d+\)\s*$")) return true;

            // If it is a list item number, bullet point, or reference number, it's NOT a math line.
            if (System.Text.RegularExpressions.Regex.IsMatch(line.Text.Trim(), @"^\s*(?:[•\-*]|\d+[\.\)]|[a-zA-Z][\.\)]|\[\d+\])\s*$")) return false;

            int proseLetters = 0;
            foreach (var word in line.Words)
            {
                bool isProseWord = true;
                if (word.Letters.Count <= 1)
                {
                    isProseWord = false;
                }
                else
                {
                    int nonAlphaCount = word.Letters.Count(l => l.Value.Length > 0 && !char.IsLetter(l.Value[0]));
                    if ((double)nonAlphaCount / word.Letters.Count > 0.3)
                    {
                        isProseWord = false;
                    }
                    else
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
                                    isProseWord = false;
                                    break;
                                }
                            }
                            if (letter.Value != null)
                            {
                                if (letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase))
                                {
                                    isProseWord = false;
                                    break;
                                }
                                foreach (int cp in GetCodepoints(letter.Value))
                                {
                                    if (IsMathCodepoint(cp))
                                    {
                                        isProseWord = false;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (isProseWord)
                {
                    proseLetters += word.Letters.Count;
                }
            }

            return proseLetters <= 2;
        }

        public static IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> MergeHorizontalLines(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> initialLines)
        {
            if (initialLines == null || initialLines.Count <= 1) return initialLines ?? new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();

            // Group lines by their Centroid Y coordinate (within 3.5 pt)
            var groups = new List<List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>>();
            foreach (var line in initialLines.OrderByDescending(l => l.BoundingBox.Centroid.Y))
            {
                bool added = false;
                foreach (var g in groups)
                {
                    double avgY = g.Average(l => l.BoundingBox.Centroid.Y);
                    if (Math.Abs(line.BoundingBox.Centroid.Y - avgY) < 3.5)
                    {
                        g.Add(line);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line });
                }
            }

            var result = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
            foreach (var g in groups)
            {
                if (g.Count == 1)
                {
                    result.Add(g[0]);
                }
                else
                {
                    // Merge multiple lines on the same level
                    var sortedGroup = g.OrderBy(l => l.BoundingBox.Left).ToList();
                    var allWords = sortedGroup.SelectMany(l => l.Words).OrderBy(w => w.BoundingBox.Left).ToList();
                    var mergedLine = new UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine(allWords, " ");
                    result.Add(mergedLine);
                }
            }

            return result.OrderByDescending(l => l.BoundingBox.Centroid.Y).ToList();
        }

        public void MergeWith(PdfParagraph other)
        {
            if (other == null) return;

            int formulaIdOffset = this.Formulas.Count;
            foreach (var formula in other.Formulas)
            {
                var newFormula = new MathFormula
                {
                    Id = formula.Id + formulaIdOffset,
                    Letters = formula.Letters,
                    Width = formula.Width
                };
                this.Formulas.Add(newFormula);
            }

            // Adjust placeholders in other.TextWithPlaceholders
            string otherText = other.TextWithPlaceholders;
            if (formulaIdOffset > 0)
            {
                otherText = System.Text.RegularExpressions.Regex.Replace(otherText, @"\{v(\d+)\}", m =>
                {
                    int oldId = int.Parse(m.Groups[1].Value);
                    return $"{{v{oldId + formulaIdOffset}}}";
                });
            }

            if (string.IsNullOrWhiteSpace(this.TextWithPlaceholders))
            {
                this.TextWithPlaceholders = otherText;
            }
            else
            {
                this.TextWithPlaceholders = this.TextWithPlaceholders + " " + otherText;
            }

            this.AllLetters.AddRange(other.AllLetters);

            this.X0 = Math.Min(this.X0, other.X0);
            this.Y0 = Math.Min(this.Y0, other.Y0);
            this.X1 = Math.Max(this.X1, other.X1);
            this.Y1 = Math.Max(this.Y1, other.Y1);

            this.OriginalX0 = Math.Min(this.OriginalX0, other.OriginalX0);
            this.OriginalY0 = Math.Min(this.OriginalY0, other.OriginalY0);
            this.OriginalX1 = Math.Max(this.OriginalX1, other.OriginalX1);
            this.OriginalY1 = Math.Max(this.OriginalY1, other.OriginalY1);

            if (this.AllLetters.Count > 0)
            {
                this.AverageFontSize = this.AllLetters.Average(l => l.FontSize);
            }

            this.brk = true;
            this.IsBold = this.IsBold || other.IsBold;
            this.IsOnlyMath = this.Formulas.Count == 1 && this.TextWithPlaceholders.Trim() == "{v0}";
            this.IsCode = this.IsCode || other.IsCode;
        }
    }

    public class MathFormula
    {
        public int Id { get; set; }
        public List<MathLetter> Letters { get; set; } = new List<MathLetter>();
        public double Width { get; set; }

        public MathFormula() { }

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
                    FontSize = l.PointSize,
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
        public double Left { get; set; }
        public double Bottom { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
    }

    public class ParagraphAnnotationInfo
    {
        public PdfAnnotation PdfAnnotation { get; set; } = null!;
        public string Text { get; set; } = "";
        public int OccurrenceIndex { get; set; }
    }

    public class RenderedChar
    {
        public char Character { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
        public double Top { get; set; }
    }
}
