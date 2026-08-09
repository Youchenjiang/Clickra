using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

public partial class ProgressWindow
{
    /// <summary>True while the visual splitter is active (the password prompt is suppressed).</summary>
    private volatile bool _isPromptingVisualSplitter = false; // skipcq: CS-R1137
    /// <summary>Total page count of the document being split.</summary>
    private int _visualSplitTotalPages = 1;
    /// <summary>Split mode: 0 = custom segments, 1 = split every page, 2 = fixed pages per segment.</summary>
    private int _visualSplitMode = 0;
    /// <summary>Pages per segment in fixed-page mode.</summary>
    private int _visualSplitNPages = 5;
    /// <summary>The segments currently displayed/used for the split.</summary>
    private List<(int Start, int End)> _visualSplitSegments = new List<(int, int)>();
    /// <summary>User-defined segments (editable in custom mode).</summary>
    private readonly List<(int Start, int End)> _visualSplitCustomSegments = new List<(int, int)>();
    /// <summary>Index of the segment currently selected in the list.</summary>
    private int _visualSplitSelectedSegmentIndex = 0;
    /// <summary>Index of the page being previewed inside the selected segment.</summary>
    private int _visualSplitCurrentPreviewPageIndex = 0;
    /// <summary>True while the zoom lightbox is open.</summary>
    private bool _visualSplitIsZoomed = false;

    /// <summary>Cached page thumbnails keyed by 1-based page number.</summary>
    private readonly Dictionary<int, Bitmap> _visualSplitPageThumbnails = new Dictionary<int, Bitmap>();

    /// <summary>Path of the PDF being split.</summary>
    private string _visualSplitFilePath = "";
    /// <summary>High-resolution render currently shown in the zoom lightbox, or null.</summary>
    private Bitmap? _visualSplitZoomBmp = null;
    /// <summary>Page number the current zoom render belongs to (-1 when none).</summary>
    private int _visualSplitZoomPageNum = -1;
    /// <summary>Monotonic sequence used to discard stale background renders.</summary>
    private int _visualSplitZoomRenderSeq = 0;

    // Zoom lightbox view state (factor 1.0 = fit; pan in logical px).
    /// <summary>Zoom factor of the lightbox (1.0 = fit, clamped to 8x).</summary>
    private float _visualSplitZoomFactor = 1f;
    /// <summary>Horizontal pan offset of the zoomed page (logical px).</summary>
    private float _visualSplitZoomPanX = 0f;
    /// <summary>Vertical pan offset of the zoomed page (logical px).</summary>
    private float _visualSplitZoomPanY = 0f;
    /// <summary>True while the user is dragging to pan the zoomed page.</summary>
    private bool _visualSplitZoomDragging = false;
    /// <summary>Last mouse X captured during a pan drag.</summary>
    private int _visualSplitZoomDragLastX = 0; // skipcq: CS-R1137
    /// <summary>Last mouse Y captured during a pan drag.</summary>
    private int _visualSplitZoomDragLastY = 0; // skipcq: CS-R1137

    // Zoom lightbox geometry (logical px, matches the paint layout).
    /// <summary>Modal left edge in logical px.</summary>
    private const float ZoomModalLeft = 24f;
    /// <summary>Modal top edge in logical px.</summary>
    private const float ZoomModalTop = 20f;
    /// <summary>Modal width in logical px.</summary>
    private const float ZoomModalW = 472f;
    /// <summary>Modal height in logical px.</summary>
    private const float ZoomModalH = 380f;
    /// <summary>Image area left edge in logical px.</summary>
    private const float ZoomImgLeft = 40f;
    /// <summary>Image area top edge in logical px.</summary>
    private const float ZoomImgTop = 58f;
    /// <summary>Image area width in logical px.</summary>
    private const float ZoomImgW = 440f;
    /// <summary>Image area height in logical px.</summary>
    private const float ZoomImgH = 328f;

    /// <summary>
    /// Initializes the visual splitter for <paramref name="filePath"/>: reads the page
    /// count, caches page thumbnails, seeds the initial custom segments (halves of the
    /// document) and resets preview/zoom state.
    /// </summary>
    private void InitializeVisualSplitter(string filePath)
    {
        int totalPages = FileProcessor.GetPdfPageCount(filePath);
        if (totalPages <= 0) totalPages = 1;

        _visualSplitTotalPages = totalPages;
        _visualSplitFilePath = filePath;
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
        _visualSplitZoomFactor = 1f;
        _visualSplitZoomPanX = 0f;
        _visualSplitZoomPanY = 0f;
        _visualSplitZoomDragging = false;
    }

    /// <summary>
    /// Renders up to 20 page thumbnails (660 px wide) for <paramref name="filePath"/> and
    /// stores them in the thumbnail cache keyed by 1-based page number, disposing the
    /// previous cache first.
    /// </summary>
    private void CachePdfPageThumbnails(string filePath)
    {
        foreach (var kvp in _visualSplitPageThumbnails)
            try { kvp.Value.Dispose(); } catch { /* Ignored: disposal must not abort the cache rebuild. */ }
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
                    var pageBmp = BuildPageThumbnail(pigPage, 660);
                    if (pageBmp != null)
                        _visualSplitPageThumbnails[p] = pageBmp;
                }
                catch { /* Ignored: a single unreadable page must not abort the cache build. */ }
            }
        }
        catch { /* Ignored: an unreadable PDF must not crash the splitter window. */ }
    }

    /// <summary>
    /// Renders a thumbnail at the page's true aspect ratio by drawing embedded images at
    /// their page coordinates and overlaying vector text (with original colors). This fixes
    /// previews that previously dropped vector text and were distorted by image-only sizing.
    /// </summary>
    /// <param name="targetWidth">Pixel width of the rendered bitmap. Larger values give
    /// crisper results when the bitmap is downscaled onto the screen.</param>
    private static Bitmap? BuildPageThumbnail(UglyToad.PdfPig.Content.Page page, int targetWidth)
    {
        double pW = page.Width > 0 ? page.Width : 595;
        double pH = page.Height > 0 ? page.Height : 842;

        // Render at high resolution so the preview is always downscaled to fit the
        // card / zoom lightbox — upscaling a small bitmap is what made text and images
        // look blurry.
        int w = targetWidth;
        int h = Math.Max(120, (int)Math.Round(w * pH / pW));

        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        try
        {
            DrawPageImages(g, page, pW, pH, w, h);
            DrawPageWords(g, page, pW, pH, w, h);
        }
        catch { /* Ignored: unrenderable page content falls back to a blank sheet. */ }

        return bmp;
    }

    /// <summary>Draws the page's embedded images onto the thumbnail at their page coordinates.
    /// The largest image is stretched as a full-page background when it covers most of the page.</summary>
    private static void DrawPageImages(Graphics g, UglyToad.PdfPig.Content.Page page, double pW, double pH, int w, int h)
    {
        var images = page.GetImages().ToList();
        if (images.Count == 0) return;

        var largest = images
            .OrderByDescending(img => img.BoundingBox.Width * img.BoundingBox.Height)
            .First();

        using var imgBmp = TryDecodeEmbeddedImage(largest);
        if (imgBmp == null) return;

        var bb = largest.BoundingBox;
        if (bb.Width > pW * 0.7 && bb.Height > pH * 0.7)
        {
            // Full-page background: stretch to the whole thumbnail.
            g.DrawImage(imgBmp, 0, 0, w, h);
            return;
        }

        // Local image: draw at its page coordinates.
        float x = (float)(bb.Left / pW * w);
        float y = (float)((1.0 - bb.Top / pH) * h);
        float iw = (float)(bb.Width / pW * w);
        float ih = (float)(bb.Height / pH * h);
        if (iw > 2 && ih > 2)
            g.DrawImage(imgBmp, x, y, iw, ih);
    }

    /// <summary>Decodes an embedded image to a bitmap (PNG first, then raw bytes), or null
    /// when the stream is unsupported or malformed.</summary>
    private static Bitmap? TryDecodeEmbeddedImage(UglyToad.PdfPig.Content.IPdfImage image)
    {
        try
        {
            if (image.TryGetPng(out var pngBytes) && pngBytes.Length > 100)
            {
                using var ms = new MemoryStream(pngBytes);
                return new Bitmap(ms);
            }

            var raw = image.RawBytes.ToArray();
            if (raw.Length > 100)
            {
                using var ms = new MemoryStream(raw);
                try { return new Bitmap(ms); } catch { /* Ignored: a malformed bitmap stream is skipped. */ }
            }
        }
        catch { /* Ignored: an undecodable embedded image is skipped. */ }
        return null;
    }

    /// <summary>Overlays the page's vector words (up to 200, with original colors and
    /// positions) onto the thumbnail.</summary>
    private static void DrawPageWords(Graphics g, UglyToad.PdfPig.Content.Page page, double pW, double pH, int w, int h)
    {
        var words = page.GetWords().ToList();
        int drawn = 0;
        foreach (var word in words)
        {
            if (drawn >= 200) break;

            var rect = word.BoundingBox;
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            float fh = (float)(rect.Height / pH * h);
            if (fh < 2.5f) continue;

            float bx = (float)(rect.Left / pW * w);
            float by = (float)((1.0 - rect.Top / pH) * h);

            float fontSize = Math.Max(3f, Math.Min(fh * 1.1f, 18f * w / 220f));
            if (TryDrawWord(g, word.Text, ResolveWordColor(word), bx, by, fontSize))
                drawn++;
        }
    }

    /// <summary>Returns the word's original color, or the default ink color when unset.</summary>
    private static Color ResolveWordColor(UglyToad.PdfPig.Content.Word word)
    {
        if (word.Letters.Count > 0 && word.Letters[0].Color != null)
        {
            try
            {
                var (r, gg, b) = word.Letters[0].Color.ToRGBValues();
                return Color.FromArgb(
                    (int)Math.Clamp(r * 255.0, 0, 255),
                    (int)Math.Clamp(gg * 255.0, 0, 255),
                    (int)Math.Clamp(b * 255.0, 0, 255));
            }
            catch { /* Ignored: an invalid color value must not abort the word overlay. */ }
        }
        return Color.FromArgb(30, 35, 45);
    }

    /// <summary>Draws one word at the given position; returns false when drawing failed.</summary>
    private static bool TryDrawWord(Graphics g, string text, Color color, float x, float y, float fontSize)
    {
        try
        {
            using var brush = new SolidBrush(color);
            using var font = new Font("Segoe UI", fontSize, GraphicsUnit.Pixel);
            g.DrawString(text, font, brush, x, y);
            return true;
        }
        catch { /* Ignored: a malformed word must not abort the overlay. */ }
        return false;
    }

    /// <summary>
    /// Starts a background render of the given page at high resolution for the zoom
    /// lightbox. The cached thumbnail keeps the lightbox populated until the render
    /// finishes, then the high-res bitmap replaces it (progressive refinement).
    /// </summary>
    private void StartVisualSplitZoomRender(int pageNum)
    {
        lock (_stateLock)
        {
            _visualSplitZoomBmp?.Dispose();
            _visualSplitZoomBmp = null;
            _visualSplitZoomPageNum = pageNum;
        }

        int seq = ++_visualSplitZoomRenderSeq;
        string filePath = _visualSplitFilePath;
        // 2x the zoom lightbox image area (440 logical px), clamped to stay sane.
        int targetW = (int)Math.Clamp(880 * _dpiScale, 660, 1600);

        Task.Run(() =>
        {
            try
            {
                using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(filePath);
                if (pageNum < 1 || pageNum > pigDoc.NumberOfPages) return;
                var pigPage = pigDoc.GetPage(pageNum);
                var bmp = BuildPageThumbnail(pigPage, targetW);
                if (bmp == null) return;

                lock (_stateLock)
                {
                    if (seq != _visualSplitZoomRenderSeq)
                    {
                        // stale: zoom closed or another page requested meanwhile
                        bmp.Dispose();
                        return;
                    }
                    _visualSplitZoomBmp?.Dispose();
                    _visualSplitZoomBmp = bmp;
                    _visualSplitZoomPageNum = pageNum;
                }

                if (_hwnd != IntPtr.Zero)
                    PostMessageW(_hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
            }
            catch { /* Ignored: a failed background render simply leaves the cached thumbnail. */ }
        }, _cts.Token);
    }

    /// <summary>
    /// Drops any cached zoom render and cancels in-flight renders (e.g. when the
    /// lightbox closes).
    /// </summary>
    private void CancelVisualSplitZoomRender()
    {
        _visualSplitZoomRenderSeq++;
        lock (_stateLock)
        {
            _visualSplitZoomBmp?.Dispose();
            _visualSplitZoomBmp = null;
            _visualSplitZoomPageNum = -1;
        }
    }

    /// <summary>Returns the 1-based page number currently previewed for the selected segment.</summary>
    private int GetCurrentZoomPage()
    {
        if (_visualSplitSelectedSegmentIndex >= 0 && _visualSplitSelectedSegmentIndex < _visualSplitSegments.Count)
        {
            var seg = _visualSplitSegments[_visualSplitSelectedSegmentIndex];
            return seg.Start + _visualSplitCurrentPreviewPageIndex;
        }
        return 1;
    }

    /// <summary>Bitmap currently shown in the zoom lightbox: the high-res render if
    /// ready, otherwise the cached thumbnail.</summary>
    private Bitmap? GetCurrentZoomBitmap()
    {
        int page = _visualSplitZoomPageNum >= 1 ? _visualSplitZoomPageNum : GetCurrentZoomPage();
        lock (_stateLock)
        {
            if (_visualSplitZoomBmp != null && _visualSplitZoomPageNum == page)
                return _visualSplitZoomBmp;
            _visualSplitPageThumbnails.TryGetValue(page, out var bmp);
            return bmp;
        }
    }

    /// <summary>Opens the zoom lightbox for the current preview page and starts an
    /// on-demand high-resolution render of it.</summary>
    private void OpenVisualSplitZoom(IntPtr hwnd)
    {
        _visualSplitIsZoomed = true;
        _visualSplitZoomFactor = 1f;
        _visualSplitZoomPanX = 0f;
        _visualSplitZoomPanY = 0f;
        _visualSplitZoomDragging = false;
        StartVisualSplitZoomRender(GetCurrentZoomPage());
        InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    /// <summary>Closes the zoom lightbox, cancels in-flight renders and resets zoom/pan state.</summary>
    private void CloseVisualSplitZoom(IntPtr hwnd)
    {
        _visualSplitIsZoomed = false;
        CancelVisualSplitZoomRender();
        _visualSplitZoomFactor = 1f;
        _visualSplitZoomPanX = 0f;
        _visualSplitZoomPanY = 0f;
        _visualSplitZoomDragging = false;
        InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    /// <summary>Clamps the pan offsets so the zoomed page never leaves the lightbox viewport.</summary>
    private void ClampVisualSplitZoomPan()
    {
        float fitW = ZoomImgW, fitH = ZoomImgH;
        var bmp = GetCurrentZoomBitmap();
        if (bmp != null)
        {
            float aspect = (float)bmp.Width / bmp.Height;
            if (aspect > fitW / fitH) fitH = fitW / aspect;
            else fitW = fitH * aspect;
        }
        float scaledW = fitW * _visualSplitZoomFactor;
        float scaledH = fitH * _visualSplitZoomFactor;
        float maxPanX = Math.Max(0f, (scaledW - ZoomImgW) / 2f);
        float maxPanY = Math.Max(0f, (scaledH - ZoomImgH) / 2f);
        _visualSplitZoomPanX = Math.Clamp(_visualSplitZoomPanX, -maxPanX, maxPanX);
        _visualSplitZoomPanY = Math.Clamp(_visualSplitZoomPanY, -maxPanY, maxPanY);
    }

    /// <summary>Draw rect (logical px) of the page bitmap inside the zoom lightbox,
    /// honoring the current zoom factor and pan.</summary>
    private bool GetVisualSplitZoomImageRect(out float drawX, out float drawY, out float drawW, out float drawH)
    {
        drawX = drawY = drawW = drawH = 0f;
        var bmp = GetCurrentZoomBitmap();
        if (bmp == null) return false;

        float fitW = ZoomImgW, fitH = ZoomImgH;
        float aspect = (float)bmp.Width / bmp.Height;
        if (aspect > fitW / fitH) fitH = fitW / aspect;
        else fitW = fitH * aspect;

        float scaledW = fitW * _visualSplitZoomFactor;
        float scaledH = fitH * _visualSplitZoomFactor;
        drawW = scaledW;
        drawH = scaledH;
        drawX = ZoomImgLeft + (ZoomImgW - scaledW) / 2f + _visualSplitZoomPanX;
        drawY = ZoomImgTop + (ZoomImgH - scaledH) / 2f + _visualSplitZoomPanY;
        return true;
    }

    /// <summary>Sets the zoom factor (clamped 1x–8x) keeping the point under
    /// (anchorX, anchorY) stationary where possible.</summary>
    private void SetVisualSplitZoomFactor(float newFactor, float anchorX, float anchorY)
    {
        newFactor = Math.Clamp(newFactor, 1f, 8f);
        float oldFactor = Math.Max(1f, _visualSplitZoomFactor);

        if (GetVisualSplitZoomImageRect(out var dx, out var dy, out _, out _))
        {
            float ux = (anchorX - dx) / oldFactor;
            float uy = (anchorY - dy) / oldFactor;
            _visualSplitZoomFactor = newFactor;
            ClampVisualSplitZoomPan();
            if (GetVisualSplitZoomImageRect(out _, out _, out var dw2, out var dh2))
            {
                _visualSplitZoomPanX = (anchorX - ux * newFactor) - (ZoomImgLeft + (ZoomImgW - dw2) / 2f);
                _visualSplitZoomPanY = (anchorY - uy * newFactor) - (ZoomImgTop + (ZoomImgH - dh2) / 2f);
                ClampVisualSplitZoomPan();
            }
        }
        else
        {
            _visualSplitZoomFactor = newFactor;
        }
    }

    /// <summary>Adds the first page gap not covered by any custom segment as a new segment
    /// and selects it (switching to custom mode).</summary>
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

    /// <summary>Removes the selected custom segment (keeping at least one) and selects a
    /// neighboring segment.</summary>
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

    /// <summary>Clears all custom segments and switches to custom mode.</summary>
    private void ClearVisualSplitSegments()
    {
        _visualSplitMode = 0;
        _visualSplitCustomSegments.Clear();
        _visualSplitSegments.Clear();
        _visualSplitSelectedSegmentIndex = -1;
        _visualSplitCurrentPreviewPageIndex = 0;
    }

    /// <summary>Splits the selected segment at the currently previewed page into two
    /// adjacent segments.</summary>
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

    /// <summary>Rebuilds the segment list from the active split mode (custom segments,
    /// split-every-page, or fixed pages per segment).</summary>
    private void ApplyVisualSplitMode()
    {
        _visualSplitCurrentPreviewPageIndex = 0;
        if (_visualSplitMode < 0 || _visualSplitMode > 2) _visualSplitMode = 0;
        switch (_visualSplitMode)
        {
            case 0:
                ApplyCustomSegmentsMode();
                break;
            case 1:
                ApplyEveryPageMode();
                break;
            case 2:
                ApplyFixedPageMode();
                break;
            default:
                // Unreachable: the mode is normalized to 0-2 before the switch.
                break;
        }
    }

    /// <summary>Rebuilds the segment list from the user's custom segments (mode 0).</summary>
    private void ApplyCustomSegmentsMode()
    {
        _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
        if (_visualSplitSegments.Count == 0 && _visualSplitTotalPages > 0)
        {
            _visualSplitCustomSegments.Add((1, _visualSplitTotalPages));
            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
        }
        _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
    }

    /// <summary>Rebuilds the segment list as one segment per page (mode 1).</summary>
    private void ApplyEveryPageMode()
    {
        _visualSplitSegments.Clear();
        _visualSplitCustomSegments.Clear();
        for (int p = 1; p <= _visualSplitTotalPages; p++)
        {
            _visualSplitSegments.Add((p, p));
            _visualSplitCustomSegments.Add((p, p));
        }
        _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
    }

    /// <summary>Rebuilds the segment list as fixed-size page chunks (mode 2).</summary>
    private void ApplyFixedPageMode()
    {
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
    }

    /// <summary>Builds the page-range spec string for the active split mode via
    /// <see cref="PdfSplitProcessor.BuildSegmentSpec"/>.</summary>
    private string BuildVisualSplitSpec() =>
        PdfSplitProcessor.BuildSegmentSpec(_visualSplitMode, _visualSplitNPages, _visualSplitTotalPages, _visualSplitSegments);

    /// <summary>Paints the entire splitter UI: mode bar, page-count selector, segment list,
    /// live page preview, bottom action buttons and the zoom lightbox overlay.</summary>
    private void PaintVisualSplitter(Graphics g, float s)
    {
        if (_linePen != null)
            g.DrawLine(_linePen, 36 * s, 96 * s, 484 * s, 96 * s);

        PaintSplitterModeBar(g, s);
        float nSelectorHeight = PaintSplitterNSelector(g, s);
        PaintSplitterBody(g, s, nSelectorHeight);
        PaintSplitterButtons(g, s);
        PaintSplitterZoomOverlay(g, s);
    }

    /// <summary>Paints the three mode buttons (custom segments, split each page, fixed pages).</summary>
    private void PaintSplitterModeBar(Graphics g, float s)
    {
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
    }

    /// <summary>Paints the pages-per-segment selector (mode 2 only) and returns its height.</summary>
    private float PaintSplitterNSelector(Graphics g, float s)
    {
        if (_visualSplitMode != 2) return 0f;

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
        return 22 * s;
    }

    /// <summary>Paints the dual-panel background frames and delegates to the segment-card and
    /// preview-panel painters.</summary>
    private void PaintSplitterBody(Graphics g, float s, float nSelectorHeight)
    {
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

        PaintSegmentCards(g, s, bodyY, leftX, leftW);

        g.FillRectangle(panelBg, rightX, bodyY, rightW, panelH);
        g.DrawRectangle(panelPen, rightX, bodyY, rightW, panelH);

        PaintPreviewPanel(g, s, bodyY, rightX, rightW, panelH);
    }

    /// <summary>Paints the scrollable list of segment cards in the left panel.</summary>
    private void PaintSegmentCards(Graphics g, float s, float bodyY, float leftX, float leftW)
    {
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
    }

    /// <summary>Paints the right panel: the output badge, page navigation bar and the live
    /// preview of the selected segment's current page.</summary>
    private void PaintPreviewPanel(Graphics g, float s, float bodyY, float rightX, float rightW, float panelH)
    {
        if (_tipFont == null) return;
        Font tipFont = _tipFont;
        Font labelFont = _msgFont ?? tipFont;

        using var headerBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
        g.DrawString("[ 即時頁面縮圖預覽 ]", tipFont, headerBrush, rightX + 8 * s, bodyY + 4 * s);

        if (_visualSplitSelectedSegmentIndex < 0 || _visualSplitSelectedSegmentIndex >= _visualSplitSegments.Count)
        {
            using var tipBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
            g.DrawString("點擊左側區段卡片以檢視預覽", tipFont, tipBrush, rightX + 8 * s, bodyY + 42 * s);
            return;
        }

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
        string truncOutName = UIHelper.TruncateText(g, outName, tipFont, badgeW - 55 * s, s);
        g.DrawString($"[PDF] {truncOutName} ({cnt}頁)", tipFont, badgeTextBrush, badgeX + 4 * s, badgeY + 2 * s);

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
        g.DrawString("<", tipFont, navTextBrush, prevBtnX + 7 * s, navY + 2 * s);

        float nextBtnX = badgeX + badgeW - 24 * s;
        float nextBtnW = 24 * s;
        g.FillRectangle(navBtnBg, nextBtnX, navY, nextBtnW, navH);
        g.DrawRectangle(navBtnPen, nextBtnX, navY, nextBtnW, navH);
        g.DrawString(">", tipFont, navTextBrush, nextBtnX + 7 * s, navY + 2 * s);

        // Split-at-current-page button (between the page label and the ">" button)
        float splitBtnW = 40 * s;
        float splitBtnX = nextBtnX - splitBtnW - 4 * s;
        g.FillRectangle(navBtnBg, splitBtnX, navY, splitBtnW, navH);
        g.DrawRectangle(navBtnPen, splitBtnX, navY, splitBtnW, navH);
        g.DrawString("切開", tipFont, navTextBrush, splitBtnX + 8 * s, navY + 2 * s);

        float pageLabelX = prevBtnX + prevBtnW + 4 * s;
        float pageLabelW = splitBtnX - 4 * s - pageLabelX;
        using var pageInfoBrush = new SolidBrush(Color.FromArgb(200, 220, 255));
        string pageLabelStr = $"P.{currentPageNum} (第 {_visualSplitCurrentPreviewPageIndex + 1}/{cnt} 頁)";
        var pageLabelSz = g.MeasureString(pageLabelStr, tipFont);
        g.DrawString(pageLabelStr, tipFont, pageInfoBrush,
            pageLabelX + (pageLabelW - pageLabelSz.Width) / 2f,
            navY + 2 * s);

        // Large Preview Box
        float cardAreaY = navY + navH + 4 * s;
        float cardAreaW = badgeW;
        float cardAreaH = (bodyY + panelH) - cardAreaY - 8 * s;

        using var shadowBg = new SolidBrush(Color.FromArgb(20, 20, 20));
        g.FillRectangle(shadowBg, badgeX + 2 * s, cardAreaY + 2 * s, cardAreaW, cardAreaH);

        PaintPreviewPage(g, s, tipFont, labelFont, currentPageNum, badgeX, cardAreaY, cardAreaW, cardAreaH);
    }

    /// <summary>Draws the selected page thumbnail (or a paper placeholder) inside the preview box.</summary>
    private void PaintPreviewPage(Graphics g, float s, Font tipFont, Font labelFont, int currentPageNum, float badgeX, float cardAreaY, float cardAreaW, float cardAreaH)
    {
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
            g.DrawString("[放大]", tipFont, zoomTagText, zoomTagX + 4 * s, zoomTagY + 1 * s);
        }
        else
        {
            using var paperBg = new SolidBrush(Color.FromArgb(245, 247, 250));
            using var paperPen = new Pen(Color.FromArgb(170, 180, 195));
            g.FillRectangle(paperBg, badgeX, cardAreaY, cardAreaW, cardAreaH);
            g.DrawRectangle(paperPen, badgeX, cardAreaY, cardAreaW, cardAreaH);

            using var pageNumBrush = new SolidBrush(Color.FromArgb(0, 100, 210));
            g.DrawString($"P.{currentPageNum}", labelFont, pageNumBrush, badgeX + 10 * s, cardAreaY + 10 * s);
        }
    }

    /// <summary>Paints the four bottom action buttons (add, delete, clear, confirm, cancel).</summary>
    private void PaintSplitterButtons(Graphics g, float s)
    {
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
    }

    /// <summary>Paints the zoom lightbox overlay: dimmed backdrop, modal frame, title,
    /// close button, the zoomed page image and the zoom control buttons.</summary>
    private void PaintSplitterZoomOverlay(Graphics g, float s)
    {
        if (!_visualSplitIsZoomed) return;

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
        int zoomPct = (int)(_visualSplitZoomFactor * 100);
        g.DrawString($"頁面 P.{currentPg} 放大預覽 · {zoomPct}%", _msgFont ?? _tipFont!, titleBrush, modalX + 16 * s, modalY + 12 * s);

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

        // Prefer the high-res on-demand render; fall back to the cached thumbnail
        // while the background render is in flight. The whole draw happens under
        // _stateLock so a completed render can never dispose a bitmap mid-draw.
        lock (_stateLock)
        {
            if (GetVisualSplitZoomImageRect(out float drawX, out float drawY, out float drawW, out float drawH))
            {
                var zoomBmp = GetCurrentZoomBitmap();
                if (zoomBmp != null)
                {
                    using var paperWhite = new SolidBrush(Color.White);
                    g.FillRectangle(paperWhite, drawX, drawY, drawW, drawH);

                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(zoomBmp, drawX, drawY, drawW, drawH);

                    using var imgPen = new Pen(Color.FromArgb(160, 175, 195));
                    g.DrawRectangle(imgPen, drawX, drawY, drawW, drawH);
                }
            }
            else
            {
                using var paperBg = new SolidBrush(Color.FromArgb(245, 247, 250));
                g.FillRectangle(paperBg, imgAreaX, imgAreaY, imgAreaW, imgAreaH);
            }
        }

        // Bottom controls: zoom in/out, reset-to-fit, and a usage hint.
        float zoomBtnY = modalY + modalH - 34 * s;
        float zoomBtnH = 22 * s;
        float zoomBtnInX = modalX + modalW - 120 * s;   // −
        float zoomBtnOutX = modalX + modalW - 86 * s;   // ＋
        float zoomBtnFitX = modalX + modalW - 52 * s;   // 適配
        float zoomBtnW = 28 * s;
        float zoomBtnFitW = 44 * s;

        using var zoomBtnBg = new SolidBrush(Color.FromArgb(48, 48, 48));
        using var zoomBtnPen = new Pen(Color.FromArgb(80, 80, 80));
        using var zoomBtnText = new SolidBrush(Color.FromArgb(220, 220, 220));
        g.FillRectangle(zoomBtnBg, zoomBtnInX, zoomBtnY, zoomBtnW, zoomBtnH);
        g.DrawRectangle(zoomBtnPen, zoomBtnInX, zoomBtnY, zoomBtnW, zoomBtnH);
        g.DrawString("−", _tipFont!, zoomBtnText, zoomBtnInX + 9 * s, zoomBtnY + 2 * s);
        g.FillRectangle(zoomBtnBg, zoomBtnOutX, zoomBtnY, zoomBtnW, zoomBtnH);
        g.DrawRectangle(zoomBtnPen, zoomBtnOutX, zoomBtnY, zoomBtnW, zoomBtnH);
        g.DrawString("＋", _tipFont!, zoomBtnText, zoomBtnOutX + 9 * s, zoomBtnY + 2 * s);
        g.FillRectangle(zoomBtnBg, zoomBtnFitX, zoomBtnY, zoomBtnFitW, zoomBtnH);
        g.DrawRectangle(zoomBtnPen, zoomBtnFitX, zoomBtnY, zoomBtnFitW, zoomBtnH);
        g.DrawString("適配", _tipFont!, zoomBtnText, zoomBtnFitX + 8 * s, zoomBtnY + 2 * s);

        using var zoomHintBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
        g.DrawString("滾輪縮放 · 拖曳平移 · 空白鍵/Enter 切換", _tipFont!, zoomHintBrush, modalX + 16 * s, zoomBtnY + 3 * s);
    }

    /// <summary>Resizes the window between the compact password-prompt height and the
    /// expanded splitter layout, recreating the back buffer for the new client size.</summary>
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
