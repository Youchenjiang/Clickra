using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Clickra.Core;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public partial class ProgressWindow
    {
        private volatile bool _isPromptingVisualSplitter = false;
        private int _visualSplitTotalPages = 1;
        private int _visualSplitMode = 0; // 0 = 自訂分段, 1 = 全拆單頁, 2 = 固定頁數
        private int _visualSplitNPages = 5;
        private List<(int Start, int End)> _visualSplitSegments = new List<(int, int)>();
        private List<(int Start, int End)> _visualSplitCustomSegments = new List<(int, int)>();
        private int _visualSplitSelectedSegmentIndex = 0;
        private int _visualSplitCurrentPreviewPageIndex = 0;
        private bool _visualSplitIsZoomed = false;

        private Dictionary<int, Bitmap> _visualSplitPageThumbnails = new Dictionary<int, Bitmap>();

        private void InitializeVisualSplitter(string filePath)
        {
            int totalPages = FileProcessor.GetPdfPageCount(filePath);
            if (totalPages <= 0) totalPages = 1;

            _visualSplitTotalPages = totalPages;
            _visualSplitMode = 0;
            _visualSplitNPages = Math.Min(5, totalPages);
            _visualSplitSegments.Clear();
            _visualSplitCustomSegments.Clear();

            CachePdfPageThumbnails(filePath);

            if (totalPages == 1)
            {
                _visualSplitCustomSegments.Add((1, 1));
            }
            else
            {
                int half = totalPages / 2;
                _visualSplitCustomSegments.Add((1, half));
                _visualSplitCustomSegments.Add((half + 1, totalPages));
            }

            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            _visualSplitSelectedSegmentIndex = 0;
            _visualSplitCurrentPreviewPageIndex = 0;
            _visualSplitIsZoomed = false;
        }

        private void CachePdfPageThumbnails(string filePath)
        {
            foreach (var kvp in _visualSplitPageThumbnails)
                try { kvp.Value.Dispose(); } catch { }
            _visualSplitPageThumbnails.Clear();

            try
            {
                using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(filePath);
                int maxP = Math.Min(pigDoc.NumberOfPages, 20);
                for (int p = 1; p <= maxP; p++)
                {
                    try
                    {
                        var pigPage = pigDoc.GetPage(p);
                        Bitmap? pageBmp = null;

                        var images = pigPage.GetImages().ToList();
                        foreach (var img in images)
                        {
                            try
                            {
                                if (img.TryGetPng(out var pngBytes) && pngBytes != null && pngBytes.Length > 100)
                                {
                                    using var ms = new MemoryStream(pngBytes);
                                    pageBmp = new Bitmap(ms);
                                    break;
                                }

                                var raw = img.RawBytes.ToArray();
                                if (raw.Length > 100)
                                {
                                    using var ms = new MemoryStream(raw);
                                    try { pageBmp = new Bitmap(ms); break; } catch { }
                                }
                            }
                            catch { }
                        }

                        if (pageBmp == null)
                            pageBmp = RenderSyntheticPageThumbnail(pigPage, p);

                        _visualSplitPageThumbnails[p] = pageBmp;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private Bitmap RenderSyntheticPageThumbnail(UglyToad.PdfPig.Content.Page page, int pageNum)
        {
            int w = 200, h = 260;
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(245, 247, 250));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var borderPen = new Pen(Color.FromArgb(170, 180, 195));
            g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

            using var bannerBrush = new SolidBrush(Color.FromArgb(215, 55, 45));
            g.FillRectangle(bannerBrush, 4, 4, w - 8, 5);

            try
            {
                double pW = page.Width > 0 ? page.Width : 595;
                double pH = page.Height > 0 ? page.Height : 842;
                var words = page.GetWords().ToList();

                if (words.Count > 0)
                {
                    using var textBrush = new SolidBrush(Color.FromArgb(30, 35, 45));
                    using var font = new Font("Segoe UI", 5.5f);

                    foreach (var word in words.Take(120))
                    {
                        float bx = (float)(word.BoundingBox.Left / pW * (w - 16) + 8);
                        float by = (float)((1.0 - word.BoundingBox.Top / pH) * (h - 30) + 16);
                        float fh = Math.Max(4.5f, (float)(word.BoundingBox.Height / pH * (h - 30)));

                        if (bx >= 4 && by >= 10 && bx < w - 4 && by + fh < h - 14)
                        {
                            float fontSize = Math.Max(3.5f, Math.Min(fh * 0.75f, 9f));
                            try
                            {
                                using var wf = new Font("Segoe UI", fontSize);
                                g.DrawString(word.Text, wf, textBrush, bx, by);
                            }
                            catch { }
                        }
                    }
                }
                else
                {
                    using var barBrush = new SolidBrush(Color.FromArgb(190, 195, 205));
                    int[] barWidths = { w - 30, w - 50, w - 20, w - 60, w - 35, w - 45, w - 25, w - 55 };
                    for (int li = 0; li < barWidths.Length; li++)
                    {
                        int bby = 22 + li * 22;
                        g.FillRectangle(barBrush, 8, bby, barWidths[li], 7);
                    }

                    using var scanBrush = new SolidBrush(Color.FromArgb(140, 145, 155));
                    using var scanFont = new Font("Segoe UI", 6.5f);
                    g.DrawString("[掃描頁]", scanFont, scanBrush, 8, h - 26);
                }
            }
            catch { }

            return bmp;
        }

        private void AddVisualSplitSegment()
        {
            _visualSplitMode = 0;

            if (_visualSplitCustomSegments.Count == 0)
            {
                _visualSplitCustomSegments.Add((1, _visualSplitTotalPages));
                _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
                _visualSplitSelectedSegmentIndex = 0;
                _visualSplitCurrentPreviewPageIndex = 0;
                return;
            }

            var covered = new HashSet<int>();
            foreach (var s in _visualSplitCustomSegments)
                for (int p = s.Start; p <= s.End; p++)
                    covered.Add(p);

            int gapStart = -1, gapEnd = -1;
            for (int p = 1; p <= _visualSplitTotalPages; p++)
            {
                if (!covered.Contains(p))
                {
                    if (gapStart < 0) gapStart = p;
                    gapEnd = p;
                }
                else if (gapStart > 0)
                {
                    break;
                }
            }

            if (gapStart < 0) return;

            _visualSplitCustomSegments.Add((gapStart, gapEnd));
            _visualSplitCustomSegments.Sort((a, b) => a.Start.CompareTo(b.Start));
            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            _visualSplitSelectedSegmentIndex = _visualSplitSegments.FindIndex(s => s.Start == gapStart && s.End == gapEnd);
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        private void DeleteVisualSplitSegment()
        {
            if (_visualSplitCustomSegments.Count <= 1) return;
            if (_visualSplitSelectedSegmentIndex < 0 || _visualSplitSelectedSegmentIndex >= _visualSplitCustomSegments.Count)
                return;

            _visualSplitMode = 0;
            _visualSplitCustomSegments.RemoveAt(_visualSplitSelectedSegmentIndex);
            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            if (_visualSplitSelectedSegmentIndex >= _visualSplitSegments.Count)
                _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count - 1;
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        private void ClearVisualSplitSegments()
        {
            _visualSplitMode = 0;
            _visualSplitCustomSegments.Clear();
            _visualSplitSegments.Clear();
            _visualSplitSelectedSegmentIndex = -1;
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        private void SplitVisualSegmentAtCurrentPage()
        {
            _visualSplitMode = 0;
            if (_visualSplitSelectedSegmentIndex < 0 || _visualSplitSelectedSegmentIndex >= _visualSplitCustomSegments.Count)
                return;

            var seg = _visualSplitCustomSegments[_visualSplitSelectedSegmentIndex];
            int pageCnt = seg.End - seg.Start + 1;
            if (pageCnt <= 1) return;

            int previewIdx = Math.Max(0, Math.Min(_visualSplitCurrentPreviewPageIndex, pageCnt - 1));
            int splitPage = seg.Start + previewIdx;

            var first = (seg.Start, splitPage);
            var second = (splitPage + 1, seg.End);

            _visualSplitCustomSegments.RemoveAt(_visualSplitSelectedSegmentIndex);
            _visualSplitCustomSegments.Insert(_visualSplitSelectedSegmentIndex, second);
            _visualSplitCustomSegments.Insert(_visualSplitSelectedSegmentIndex, first);
            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        private void ApplyVisualSplitMode()
        {
            _visualSplitCurrentPreviewPageIndex = 0;
            switch (_visualSplitMode)
            {
                case 0:
                    _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
                    if (_visualSplitSegments.Count == 0 && _visualSplitTotalPages > 0)
                    {
                        _visualSplitCustomSegments.Add((1, _visualSplitTotalPages));
                        _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
                    }
                    _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
                    break;

                case 1:
                    _visualSplitSegments.Clear();
                    _visualSplitCustomSegments.Clear();
                    for (int p = 1; p <= _visualSplitTotalPages; p++)
                    {
                        _visualSplitSegments.Add((p, p));
                        _visualSplitCustomSegments.Add((p, p));
                    }
                    _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
                    break;

                case 2:
                    _visualSplitSegments.Clear();
                    _visualSplitCustomSegments.Clear();
                    int n = Math.Max(1, _visualSplitNPages);
                    for (int p = 1; p <= _visualSplitTotalPages; p += n)
                    {
                        int end = Math.Min(p + n - 1, _visualSplitTotalPages);
                        _visualSplitSegments.Add((p, end));
                        _visualSplitCustomSegments.Add((p, end));
                    }
                    _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
                    break;
            }
        }

        private string BuildVisualSplitSpec() =>
            PdfSplitProcessor.BuildSegmentSpec(_visualSplitMode, _visualSplitNPages, _visualSplitTotalPages, _visualSplitSegments);

        private void PaintVisualSplitter(Graphics g, float s)
        {
            if (_linePen != null)
                g.DrawLine(_linePen, 36 * s, 96 * s, 484 * s, 96 * s);

            // 1. Top Mode Switcher Bar
            float modeY = 102 * s;
            float modeWidth = 140 * s;
            float modeHeight = 26 * s;

            string[] modeTitles = { "自訂分段", "全拆單頁", $"固定頁數: {_visualSplitNPages}頁" };
            for (int i = 0; i < 3; i++)
            {
                float modeX = (36 + i * 148) * s;
                if (i == 2) modeWidth = 152 * s;

                bool isSelected = (_visualSplitMode == i);

                Color btnBg = isSelected ? Color.FromArgb(0, 120, 215) : Color.FromArgb(45, 45, 45);
                using var btnBrush = new SolidBrush(btnBg);
                using var borderPen = new Pen(isSelected ? Color.FromArgb(0, 150, 255) : Color.FromArgb(70, 70, 70));
                
                g.FillRectangle(btnBrush, modeX, modeY, modeWidth, modeHeight);
                g.DrawRectangle(borderPen, modeX, modeY, modeWidth, modeHeight);

                if (_msgFont != null)
                {
                    using var textBrush = new SolidBrush(isSelected ? Color.White : Color.FromArgb(200, 200, 200));
                    g.DrawString(modeTitles[i], _msgFont, textBrush, modeX + 10 * s, modeY + 4 * s);
                }
            }

            // 2. N Pages Selector (only visible in Mode 2)
            float nSelectorHeight = 0;
            if (_visualSplitMode == 2)
            {
                nSelectorHeight = 22 * s;
                float nSelY = 130 * s;
                float nSelH = 18 * s;

                float minusX = 36 * s;
                float minusW = 24 * s;
                using var nBtnBg = new SolidBrush(Color.FromArgb(55, 55, 55));
                using var nBtnPen = new Pen(Color.FromArgb(90, 90, 90));
                g.FillRectangle(nBtnBg, minusX, nSelY, minusW, nSelH);
                g.DrawRectangle(nBtnPen, minusX, nSelY, minusW, nSelH);
                using var nTextBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                g.DrawString("-", _msgFont ?? _tipFont!, nTextBrush, minusX + 7 * s, nSelY + 1 * s);

                float nLabelX = minusX + minusW + 6 * s;
                using var nLabelBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                g.DrawString($"每 {_visualSplitNPages} 頁", _msgFont ?? _tipFont!, nLabelBrush, nLabelX, nSelY + 1 * s);

                float plusX = nLabelX + 70 * s;
                float plusW = 24 * s;
                g.FillRectangle(nBtnBg, plusX, nSelY, plusW, nSelH);
                g.DrawRectangle(nBtnPen, plusX, nSelY, plusW, nSelH);
                g.DrawString("+", _msgFont ?? _tipFont!, nTextBrush, plusX + 7 * s, nSelY + 1 * s);
            }

            // 3. Dual-Panel Body
            float bodyY = 134 * s + nSelectorHeight;
            float leftX = 36 * s;
            float leftW = 216 * s;
            float rightX = 260 * s;
            float rightW = 224 * s;
            float panelH = (380 * s) - bodyY;

            using var panelBg = new SolidBrush(Color.FromArgb(32, 32, 32));
            using var panelPen = new Pen(Color.FromArgb(60, 60, 60));
            g.FillRectangle(panelBg, leftX, bodyY, leftW, panelH);
            g.DrawRectangle(panelPen, leftX, bodyY, leftW, panelH);

            if (_msgFont != null)
            {
                using var headerBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                g.DrawString("[ 分段列表 ]", _msgFont, headerBrush, leftX + 8 * s, bodyY + 4 * s);
            }

            float cardY = bodyY + 24 * s;
            float cardH = 20 * s;
            int maxVisibleCards = 8;

            for (int i = 0; i < Math.Min(maxVisibleCards, _visualSplitSegments.Count); i++)
            {
                var seg = _visualSplitSegments[i];
                bool isFocused = (i == _visualSplitSelectedSegmentIndex);

                Color cardBgColor = isFocused ? Color.FromArgb(0, 90, 180) : Color.FromArgb(48, 48, 48);
                using var cardBrush = new SolidBrush(cardBgColor);
                using var cardPen = new Pen(isFocused ? Color.FromArgb(0, 140, 240) : Color.FromArgb(70, 70, 70));

                g.FillRectangle(cardBrush, leftX + 6 * s, cardY + i * (cardH + 3 * s), leftW - 12 * s, cardH);
                g.DrawRectangle(cardPen, leftX + 6 * s, cardY + i * (cardH + 3 * s), leftW - 12 * s, cardH);

                if (_tipFont != null)
                {
                    using var textBrush = new SolidBrush(isFocused ? Color.White : Color.FromArgb(210, 210, 210));
                    int pageCnt = seg.End - seg.Start + 1;
                    string pageLabel = seg.Start == seg.End ? $"P.{seg.Start}" : $"P.{seg.Start}-{seg.End}";
                    g.DrawString($"區段 {i + 1}: {pageLabel} ({pageCnt}頁)", _tipFont, textBrush, leftX + 10 * s, cardY + i * (cardH + 3 * s) + 2 * s);
                }
            }

            // Right Panel
            g.FillRectangle(panelBg, rightX, bodyY, rightW, panelH);
            g.DrawRectangle(panelPen, rightX, bodyY, rightW, panelH);

            if (_tipFont != null)
            {
                using var headerBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                g.DrawString("[ 即時頁面縮圖預覽 ]", _tipFont, headerBrush, rightX + 8 * s, bodyY + 4 * s);

                if (_visualSplitSelectedSegmentIndex >= 0 && _visualSplitSelectedSegmentIndex < _visualSplitSegments.Count)
                {
                    var activeSeg = _visualSplitSegments[_visualSplitSelectedSegmentIndex];
                    int cnt = activeSeg.End - activeSeg.Start + 1;
                    string outName = Path.GetFileNameWithoutExtension(_passwordPromptFilename);

                    float badgeX = rightX + 6 * s;
                    float badgeY = bodyY + 20 * s;
                    float badgeW = rightW - 12 * s;
                    float badgeH = 18 * s;

                    using var badgeBg = new SolidBrush(Color.FromArgb(25, 60, 35));
                    using var badgePen = new Pen(Color.FromArgb(50, 140, 70));
                    g.FillRectangle(badgeBg, badgeX, badgeY, badgeW, badgeH);
                    g.DrawRectangle(badgePen, badgeX, badgeY, badgeW, badgeH);

                    using var badgeTextBrush = new SolidBrush(Color.FromArgb(120, 240, 140));
                    string truncOutName = UIHelper.TruncateText(g, outName, _tipFont, badgeW - 55 * s, s);
                    g.DrawString($"[PDF] {truncOutName} ({cnt}頁)", _tipFont, badgeTextBrush, badgeX + 4 * s, badgeY + 2 * s);

                    if (_visualSplitCurrentPreviewPageIndex < 0) _visualSplitCurrentPreviewPageIndex = 0;
                    if (_visualSplitCurrentPreviewPageIndex >= cnt) _visualSplitCurrentPreviewPageIndex = cnt - 1;
                    int currentPageNum = activeSeg.Start + _visualSplitCurrentPreviewPageIndex;

                    // Page Navigation Bar
                    float navY = bodyY + 41 * s;
                    float navH = 20 * s;

                    float prevBtnX = badgeX;
                    float prevBtnW = 24 * s;
                    using var navBtnBg = new SolidBrush(Color.FromArgb(45, 50, 60));
                    using var navBtnPen = new Pen(Color.FromArgb(80, 90, 105));
                    g.FillRectangle(navBtnBg, prevBtnX, navY, prevBtnW, navH);
                    g.DrawRectangle(navBtnPen, prevBtnX, navY, prevBtnW, navH);
                    using var navTextBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    g.DrawString("<", _tipFont, navTextBrush, prevBtnX + 7 * s, navY + 2 * s);

                    float nextBtnX = badgeX + badgeW - 24 * s;
                    float nextBtnW = 24 * s;
                    g.FillRectangle(navBtnBg, nextBtnX, navY, nextBtnW, navH);
                    g.DrawRectangle(navBtnPen, nextBtnX, navY, nextBtnW, navH);
                    g.DrawString(">", _tipFont, navTextBrush, nextBtnX + 7 * s, navY + 2 * s);

                    // Split-at-current-page button (between the page label and the ">" button)
                    float splitBtnW = 40 * s;
                    float splitBtnX = nextBtnX - splitBtnW - 4 * s;
                    g.FillRectangle(navBtnBg, splitBtnX, navY, splitBtnW, navH);
                    g.DrawRectangle(navBtnPen, splitBtnX, navY, splitBtnW, navH);
                    g.DrawString("切開", _tipFont, navTextBrush, splitBtnX + 8 * s, navY + 2 * s);

                    float pageLabelX = prevBtnX + prevBtnW + 4 * s;
                    float pageLabelW = splitBtnX - 4 * s - pageLabelX;
                    using var pageInfoBrush = new SolidBrush(Color.FromArgb(200, 220, 255));
                    string pageLabelStr = $"P.{currentPageNum} (第 {_visualSplitCurrentPreviewPageIndex + 1}/{cnt} 頁)";
                    var pageLabelSz = g.MeasureString(pageLabelStr, _tipFont);
                    g.DrawString(pageLabelStr, _tipFont, pageInfoBrush,
                        pageLabelX + (pageLabelW - pageLabelSz.Width) / 2f,
                        navY + 2 * s);

                    // Large Preview Box
                    float cardAreaY = navY + navH + 4 * s;
                    float cardAreaW = badgeW;
                    float cardAreaH = (bodyY + panelH) - cardAreaY - 8 * s;

                    using var shadowBg = new SolidBrush(Color.FromArgb(20, 20, 20));
                    g.FillRectangle(shadowBg, badgeX + 2 * s, cardAreaY + 2 * s, cardAreaW, cardAreaH);

                    if (_visualSplitPageThumbnails.TryGetValue(currentPageNum, out var pageBmp) && pageBmp != null)
                    {
                        float fitW = cardAreaW;
                        float fitH = cardAreaH;
                        float imgAspect = (float)pageBmp.Width / pageBmp.Height;
                        float boxAspect = fitW / fitH;

                        float drawW, drawH;
                        if (imgAspect > boxAspect) { drawW = fitW; drawH = fitW / imgAspect; }
                        else { drawH = fitH; drawW = fitH * imgAspect; }

                        float drawX = badgeX + (fitW - drawW) / 2f;
                        float drawY = cardAreaY + (fitH - drawH) / 2f;

                        using var paperWhite = new SolidBrush(Color.White);
                        g.FillRectangle(paperWhite, drawX, drawY, drawW, drawH);

                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(pageBmp, drawX, drawY, drawW, drawH);

                        using var borderPen = new Pen(Color.FromArgb(160, 175, 195), 1.5f * s);
                        g.DrawRectangle(borderPen, drawX, drawY, drawW, drawH);

                        float zoomTagW = 80 * s;
                        float zoomTagH = 16 * s;
                        float zoomTagX = drawX + drawW - zoomTagW - 4 * s;
                        float zoomTagY = drawY + drawH - zoomTagH - 4 * s;
                        using var zoomTagBg = new SolidBrush(Color.FromArgb(180, 20, 25, 35));
                        using var zoomTagText = new SolidBrush(Color.FromArgb(240, 240, 240));
                        g.FillRectangle(zoomTagBg, zoomTagX, zoomTagY, zoomTagW, zoomTagH);
                        g.DrawString("[放大]", _tipFont, zoomTagText, zoomTagX + 4 * s, zoomTagY + 1 * s);
                    }
                    else
                    {
                        using var paperBg = new SolidBrush(Color.FromArgb(245, 247, 250));
                        using var paperPen = new Pen(Color.FromArgb(170, 180, 195));
                        g.FillRectangle(paperBg, badgeX, cardAreaY, cardAreaW, cardAreaH);
                        g.DrawRectangle(paperPen, badgeX, cardAreaY, cardAreaW, cardAreaH);

                        using var pageNumBrush = new SolidBrush(Color.FromArgb(0, 100, 210));
                        g.DrawString($"P.{currentPageNum}", _msgFont ?? _tipFont!, pageNumBrush, badgeX + 10 * s, cardAreaY + 10 * s);
                    }
                }
                else
                {
                    using var tipBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString("點擊左側區段卡片以檢視預覽", _tipFont, tipBrush, rightX + 8 * s, bodyY + 42 * s);
                }
            }

            // 4. Bottom Action Buttons
            float btnY = 380 * s;
            float btnH = 26 * s;

            using var addBg = new SolidBrush(Color.FromArgb(48, 48, 48));
            using var addPen = new Pen(Color.FromArgb(80, 80, 80));

            g.FillRectangle(addBg, 36 * s, btnY, 96 * s, btnH);
            g.DrawRectangle(addPen, 36 * s, btnY, 96 * s, btnH);

            g.FillRectangle(addBg, 138 * s, btnY, 84 * s, btnH);
            g.DrawRectangle(addPen, 138 * s, btnY, 84 * s, btnH);

            g.FillRectangle(addBg, 228 * s, btnY, 84 * s, btnH);
            g.DrawRectangle(addPen, 228 * s, btnY, 84 * s, btnH);

            Color confirmBgColor = Color.FromArgb(0, 120, 215);
            using var confirmBg = new SolidBrush(confirmBgColor);
            using var confirmPen = new Pen(Color.FromArgb(0, 150, 255));
            g.FillRectangle(confirmBg, 336 * s, btnY, 74 * s, btnH);
            g.DrawRectangle(confirmPen, 336 * s, btnY, 74 * s, btnH);

            using var cancelBg = new SolidBrush(Color.FromArgb(60, 60, 60));
            using var cancelPen = new Pen(Color.FromArgb(90, 90, 90));
            g.FillRectangle(cancelBg, 416 * s, btnY, 68 * s, btnH);
            g.DrawRectangle(cancelPen, 416 * s, btnY, 68 * s, btnH);

            if (_tipFont != null)
            {
                using var whiteBrush = new SolidBrush(Color.White);
                using var grayBrush = new SolidBrush(Color.FromArgb(200, 200, 200));

                g.DrawString("＋ 新增區段", _tipFont, grayBrush, 44 * s, btnY + 4 * s);
                g.DrawString("刪除區段", _tipFont, grayBrush, 146 * s, btnY + 4 * s);
                g.DrawString("清空區段", _tipFont, grayBrush, 236 * s, btnY + 4 * s);
                g.DrawString("確定分割", _tipFont, whiteBrush, 344 * s, btnY + 4 * s);
                g.DrawString("取消", _tipFont, whiteBrush, 436 * s, btnY + 4 * s);
            }

            // 5. Zoom Lightbox Overlay
            if (_visualSplitIsZoomed)
            {
                int currentPg = 1;
                if (_visualSplitSelectedSegmentIndex >= 0 && _visualSplitSelectedSegmentIndex < _visualSplitSegments.Count)
                {
                    var seg = _visualSplitSegments[_visualSplitSelectedSegmentIndex];
                    currentPg = seg.Start + _visualSplitCurrentPreviewPageIndex;
                }

                using var overlayBg = new SolidBrush(Color.FromArgb(235, 15, 18, 24));
                g.FillRectangle(overlayBg, 0, 0, 520 * s, 420 * s);

                float modalX = 24 * s;
                float modalY = 20 * s;
                float modalW = 472 * s;
                float modalH = 380 * s;

                using var modalBg = new SolidBrush(Color.FromArgb(32, 35, 42));
                using var modalPen = new Pen(Color.FromArgb(80, 90, 110), 1.5f * s);
                g.FillRectangle(modalBg, modalX, modalY, modalW, modalH);
                g.DrawRectangle(modalPen, modalX, modalY, modalW, modalH);

                using var titleBrush = new SolidBrush(Color.FromArgb(230, 240, 255));
                g.DrawString($"頁面 P.{currentPg} 放大預覽", _msgFont ?? _tipFont!, titleBrush, modalX + 16 * s, modalY + 12 * s);

                float closeW = 70 * s;
                float closeH = 22 * s;
                float closeX = modalX + modalW - closeW - 12 * s;
                float closeY = modalY + 10 * s;
                using var closeBg = new SolidBrush(Color.FromArgb(180, 45, 40));
                g.FillRectangle(closeBg, closeX, closeY, closeW, closeH);
                using var closeTextBrush = new SolidBrush(Color.White);
                g.DrawString("X 關閉", _tipFont!, closeTextBrush, closeX + 12 * s, closeY + 3 * s);

                float imgAreaX = modalX + 16 * s;
                float imgAreaY = modalY + 38 * s;
                float imgAreaW = modalW - 32 * s;
                float imgAreaH = modalH - 52 * s;

                if (_visualSplitPageThumbnails.TryGetValue(currentPg, out var zoomBmp) && zoomBmp != null)
                {
                    float imgAspect = (float)zoomBmp.Width / zoomBmp.Height;
                    float boxAspect = imgAreaW / imgAreaH;

                    float drawW = (imgAspect > boxAspect) ? imgAreaW : (imgAreaH * imgAspect);
                    float drawH = (imgAspect > boxAspect) ? (imgAreaW / imgAspect) : imgAreaH;
                    float drawX = imgAreaX + (imgAreaW - drawW) / 2f;
                    float drawY = imgAreaY + (imgAreaH - drawH) / 2f;

                    using var paperWhite = new SolidBrush(Color.White);
                    g.FillRectangle(paperWhite, drawX, drawY, drawW, drawH);

                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(zoomBmp, drawX, drawY, drawW, drawH);

                    using var imgPen = new Pen(Color.FromArgb(160, 175, 195));
                    g.DrawRectangle(imgPen, drawX, drawY, drawW, drawH);
                }
            }
        }

        private void ResizeWindowForVisualSplitter(IntPtr hwnd, bool expand)
        {
            float s = _dpiScale;
            int clientW = (int)(520 * s);
            int clientH = expand ? (int)(420 * s) : (int)(280 * s);

            _bufferGraphics?.Dispose();
            _bufferBmp?.Dispose();
            _bufferBmp = new Bitmap(clientW, clientH);
            _bufferGraphics = Graphics.FromImage(_bufferBmp);
            _bufferGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            _bufferGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
            int winW = rect.right - rect.left;
            int winH = rect.bottom - rect.top;

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, winW, winH, 0x0002 | 0x0004);

            // Resizing only invalidates the newly exposed area, and the paint blit
            // is clipped to that region. Force a full-window repaint so the top of
            // the window does not keep stale progress-window pixels (the splitter
            // prompt pauses the animation timer, so nothing else refreshes it).
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }
}
