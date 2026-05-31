using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
#pragma warning disable CA1416 // Validate platform compatibility
using System.Drawing;

namespace Clickra.Core
{
    public static class FileProcessor
    {
        private static readonly System.Text.RegularExpressions.Regex CaptionRegex = new(
            @"^[ 	]*(listing|figure|fig\.|table|algorithm)\s+(\d+|[ivxlcdm]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
        );

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
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
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
