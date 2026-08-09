using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    /// <summary>
    /// 提供 CLI 執行階段專專用之 Win32 進度視窗。
    /// </summary>
    public partial class ProgressWindow
    {
        /// <summary>Static window procedure: resolves the ProgressWindow instance from the
        /// window's user data, routes messages to it and frees the GCHandle on destroy.</summary>
        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            ProgressWindow? window = null;
            if (msg == 0x0081) // WM_NCCREATE
            {
                IntPtr lpCreateParams = Marshal.ReadIntPtr(l);
                SetWindowLongPtr(hwnd, -21, lpCreateParams); // GWLP_USERDATA = -21
                if (lpCreateParams != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(lpCreateParams);
                    window = gcHandle.Target as ProgressWindow;
                    if (window != null) window._hwnd = hwnd;
                }
            }
            else
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        window = gcHandle.Target as ProgressWindow;
                    }
                }
            }

            IntPtr result = IntPtr.Zero;
            if (window != null)
            {
                result = window.InstanceWndProc(hwnd, msg, w, l);
            }
            else
            {
                result = DefWindowProcW(hwnd, msg, w, l);
            }

            if (msg == 0x0082) // WM_NCDESTROY
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        gcHandle.Free();
                    }
                    SetWindowLongPtr(hwnd, -21, IntPtr.Zero);
                }
            }

            return result;
        }

        /// <summary>Subclassed EDIT window procedure: maps Enter to OK and Escape to Cancel
        /// for the password prompt.</summary>
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static unsafe IntPtr EditSubclassProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            IntPtr oldProc = GetProp(hwnd, "ClickraOldWndProc");
            if (msg == 0x0100) // WM_KEYDOWN
            {
                int key = w.ToInt32();
                if (key == 0x0D) // VK_RETURN
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1001, IntPtr.Zero); // WM_COMMAND, ID = 1001 (OK)
                    return IntPtr.Zero;
                }
                if (key == 0x1B) // VK_ESCAPE
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1002, IntPtr.Zero); // WM_COMMAND, ID = 1002 (Cancel)
                    return IntPtr.Zero;
                }
            }
            if (msg == 0x0002) // WM_DESTROY
            {
                RemoveProp(hwnd, "ClickraOldWndProc");
            }
            return oldProc != IntPtr.Zero ? CallWindowProc(oldProc, hwnd, msg, w, l) : DefWindowProcW(hwnd, msg, w, l);
        }

        /// <summary>Instance window procedure handling the progress window messages:
        /// painting, timers, password prompt, tray icon, scrollbar, and the visual splitter's
        /// keyboard, mouse and zoom interactions.</summary>
        // skipcq: CS-R1140
        private unsafe IntPtr InstanceWndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case WM_USER_SHOW_PASSWORD_INPUT:
                    if (_isPromptingVisualSplitter)
                    {
                        ResizeWindowForVisualSplitter(hwnd, true);
                    }
                    ShowPasswordInputControls(hwnd);
                    return IntPtr.Zero;

                case WM_USER_HIDE_PASSWORD_INPUT:
                    HidePasswordInputControls(hwnd);
                    return IntPtr.Zero;

                case 0x0100: // WM_KEYDOWN
                    if (_isPromptingVisualSplitter)
                    {
                        int key = w.ToInt32();
                        if (_visualSplitIsZoomed)
                        {
                            if (key == 0x1B || key == 0x20 || key == 0x0D) // Esc / Space / Enter
                            {
                                CloseVisualSplitZoom(hwnd);
                            }
                            else if (key == 0x6B || key == 0xBB) // numpad + / =
                            {
                                SetVisualSplitZoomFactor(_visualSplitZoomFactor * 1.25f, ZoomImgLeft + ZoomImgW / 2f, ZoomImgTop + ZoomImgH / 2f);
                                InvalidateRect(hwnd, IntPtr.Zero, true);
                            }
                            else if (key == 0x6D || key == 0xBD) // numpad - / -
                            {
                                SetVisualSplitZoomFactor(_visualSplitZoomFactor / 1.25f, ZoomImgLeft + ZoomImgW / 2f, ZoomImgTop + ZoomImgH / 2f);
                                InvalidateRect(hwnd, IntPtr.Zero, true);
                            }
                            else if (key == 0x30 || key == 0x60) // 0 / numpad 0 → fit
                            {
                                _visualSplitZoomFactor = 1f;
                                _visualSplitZoomPanX = 0f;
                                _visualSplitZoomPanY = 0f;
                                InvalidateRect(hwnd, IntPtr.Zero, true);
                            }
                        }
                        else if (key == 0x20 || key == 0x0D) // Space / Enter → open zoom
                        {
                            OpenVisualSplitZoom(hwnd);
                        }
                        return IntPtr.Zero;
                    }
                    break;

                case 0x0133: // WM_CTLCOLOREDIT
                    {
                        IntPtr editHdc = w;
                        SetTextColor(editHdc, 0x00FFFFFF); // White
                        SetBkColor(editHdc, 0x002D2D2D); // Edit bg (45, 45, 45)
                        return _editBgBrush;
                    }

                case 0x0111: // WM_COMMAND
                    HandlePasswordInputCommand(hwnd, w);
                    return IntPtr.Zero;
                case 0x020A: // WM_MOUSEWHEEL
                    {
                        int delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
                        if (_isPromptingVisualSplitter && _visualSplitIsZoomed)
                        {
                            // Wheel zooms in the lightbox, anchored at the cursor.
                            int sx = (short)(l.ToInt64() & 0xFFFF);
                            int sy = (short)((l.ToInt64() >> 16) & 0xFFFF);
                            var pt = new Point(sx, sy);
                            ScreenToClient(hwnd, ref pt);
                            SetVisualSplitZoomFactor(_visualSplitZoomFactor * (delta > 0 ? 1.25f : 0.8f), pt.X / _dpiScale, pt.Y / _dpiScale);
                            InvalidateRect(hwnd, IntPtr.Zero, true);
                            return IntPtr.Zero;
                        }
                        int scrollDir = delta > 0 ? -1 : 1;
                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                _scrollOffset += scrollDir * 30;
                                if (_scrollOffset < 0) _scrollOffset = 0;
                            }
                        }
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case WM_USER_INVALIDATE:
                    InvalidateRect(hwnd, IntPtr.Zero, w != IntPtr.Zero);
                    return IntPtr.Zero;
                case 0x0200: // WM_MOUSEMOVE
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        if (_isPromptingVisualSplitter && _visualSplitIsZoomed && _visualSplitZoomDragging)
                        {
                            _visualSplitZoomPanX += mouseX - _visualSplitZoomDragLastX;
                            _visualSplitZoomPanY += mouseY - _visualSplitZoomDragLastY;
                            _visualSplitZoomDragLastX = mouseX;
                            _visualSplitZoomDragLastY = mouseY;
                            ClampVisualSplitZoomPan();
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return IntPtr.Zero;
                        }

                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                if (_isDraggingScroll)
                                {
                                    string statusMsg = _message;
                                    float logicalPctW = 0;
                                    string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
                                    if (_pctFont != null && _total > 0)
                                    {
                                        using var tempBmp = new Bitmap(1, 1);
                                        using var tempG = Graphics.FromImage(tempBmp);
                                        logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
                                    }
                                    float logicalMaxMsgW = 448f;
                                    if (logicalPctW > 0)
                                    {
                                        logicalMaxMsgW = 448f - logicalPctW - 10f;
                                    }

                                    float fullMsgW = 0f;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        if (_msgFont != null)
                                        {
                                            fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                                        }
                                    }

                                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
                                    if (maxLogicalScroll > 0)
                                    {
                                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                                        float travelRange = logicalMaxMsgW - thumbW;
                                        if (travelRange > 0)
                                        {
                                            float deltaX = mouseX - _dragStartMouseX;
                                            float deltaOffset = (deltaX / travelRange) * maxLogicalScroll;
                                            _scrollOffset = Math.Max(0f, Math.Min(_dragStartOffset + deltaOffset, maxLogicalScroll));
                                            InvalidateRect(hwnd, IntPtr.Zero, false);
                                        }
                                    }
                                }
                                else
                                {
                                    bool hovered = (mouseX >= 456 && mouseX <= 484 && mouseY >= 36 && mouseY <= 64);
                                    if (hovered != _isTrayBtnHovered)
                                    {
                                        _isTrayBtnHovered = hovered;
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0201: // WM_LBUTTONDOWN
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        if (_isPromptingVisualSplitter)
                        {
                            // Zoom Lightbox controls: buttons, drag-to-pan inside the image,
                            // click outside to close.
                            if (_visualSplitIsZoomed)
                            {
                                float zoomBtnY = ZoomModalTop + ZoomModalH - 34f;
                                float zoomBtnH = 22f;
                                if (mouseY >= zoomBtnY && mouseY <= zoomBtnY + zoomBtnH)
                                {
                                    float btnInX = ZoomModalLeft + ZoomModalW - 120f; // −
                                    float btnOutX = ZoomModalLeft + ZoomModalW - 86f; // ＋
                                    float btnFitX = ZoomModalLeft + ZoomModalW - 52f; // 適配
                                    float cx = ZoomImgLeft + ZoomImgW / 2f;
                                    float cy = ZoomImgTop + ZoomImgH / 2f;

                                    if (mouseX >= btnInX && mouseX <= btnInX + 28f)
                                    {
                                        SetVisualSplitZoomFactor(_visualSplitZoomFactor / 1.25f, cx, cy);
                                        InvalidateRect(hwnd, IntPtr.Zero, true);
                                        return IntPtr.Zero;
                                    }
                                    if (mouseX >= btnOutX && mouseX <= btnOutX + 28f)
                                    {
                                        SetVisualSplitZoomFactor(_visualSplitZoomFactor * 1.25f, cx, cy);
                                        InvalidateRect(hwnd, IntPtr.Zero, true);
                                        return IntPtr.Zero;
                                    }
                                    if (mouseX >= btnFitX && mouseX <= btnFitX + 44f)
                                    {
                                        _visualSplitZoomFactor = 1f;
                                        _visualSplitZoomPanX = 0f;
                                        _visualSplitZoomPanY = 0f;
                                        InvalidateRect(hwnd, IntPtr.Zero, true);
                                        return IntPtr.Zero;
                                    }
                                }

                                if (GetVisualSplitZoomImageRect(out var zx, out var zy, out var zw, out var zh) &&
                                    mouseX >= zx && mouseX <= zx + zw && mouseY >= zy && mouseY <= zy + zh)
                                {
                                    _visualSplitZoomDragging = true;
                                    _visualSplitZoomDragLastX = mouseX;
                                    _visualSplitZoomDragLastY = mouseY;
                                    SetCapture(hwnd);
                                    return IntPtr.Zero;
                                }

                                CloseVisualSplitZoom(hwnd);
                                return IntPtr.Zero;
                            }

                            // Mode Switcher Bar
                            if (mouseY >= 102 && mouseY <= 128)
                            {
                                if (mouseX >= 36 && mouseX <= 176) _visualSplitMode = 0;
                                else if (mouseX >= 184 && mouseX <= 324) _visualSplitMode = 1;
                                else if (mouseX >= 332 && mouseX <= 484) _visualSplitMode = 2;
                                ApplyVisualSplitMode();
                                InvalidateRect(hwnd, IntPtr.Zero, true);
                                return IntPtr.Zero;
                            }

                            // N Pages Selector (+/- buttons, only in Mode 2)
                            if (_visualSplitMode == 2 && mouseY >= 131 && mouseY <= 149)
                            {
                                if (mouseX >= 36 && mouseX <= 60) // [-]
                                {
                                    _visualSplitNPages = Math.Max(2, _visualSplitNPages - 1);
                                    ApplyVisualSplitMode();
                                    InvalidateRect(hwnd, IntPtr.Zero, true);
                                    return IntPtr.Zero;
                                }
                                if (mouseX >= 132 && mouseX <= 156) // [+]
                                {
                                    _visualSplitNPages = Math.Min(_visualSplitTotalPages, _visualSplitNPages + 1);
                                    ApplyVisualSplitMode();
                                    InvalidateRect(hwnd, IntPtr.Zero, true);
                                    return IntPtr.Zero;
                                }
                            }

                            // Left Segment Cards
                            int cardStartY = 158 + (_visualSplitMode == 2 ? 22 : 0);
                            if (mouseX >= 36 && mouseX <= 252 && mouseY >= cardStartY && mouseY <= 374)
                            {
                                int cardIdx = (mouseY - cardStartY) / 23;
                                if (cardIdx >= 0 && cardIdx < _visualSplitSegments.Count)
                                {
                                    _visualSplitSelectedSegmentIndex = cardIdx;
                                    _visualSplitCurrentPreviewPageIndex = 0;
                                    InvalidateRect(hwnd, IntPtr.Zero, true);
                                    return IntPtr.Zero;
                                }
                            }

                            // Right Panel Page Navigation Bar
                            int navOffset = (_visualSplitMode == 2 ? 22 : 0);
                            if (mouseX >= 260 && mouseX <= 484 && mouseY >= 170 + navOffset && mouseY <= 200 + navOffset)
                            {
                                int segCnt = 1;
                                if (_visualSplitSelectedSegmentIndex >= 0 && _visualSplitSelectedSegmentIndex < _visualSplitSegments.Count)
                                {
                                    var seg = _visualSplitSegments[_visualSplitSelectedSegmentIndex];
                                    segCnt = seg.End - seg.Start + 1;
                                }

                                if (mouseX >= 266 && mouseX <= 292) // <
                                {
                                    _visualSplitCurrentPreviewPageIndex = Math.Max(0, _visualSplitCurrentPreviewPageIndex - 1);
                                    InvalidateRect(hwnd, IntPtr.Zero, true);
                                    return IntPtr.Zero;
                                }
                                if (mouseX >= 452 && mouseX <= 478) // >
                                {
                                    _visualSplitCurrentPreviewPageIndex = Math.Min(segCnt - 1, _visualSplitCurrentPreviewPageIndex + 1);
                                    InvalidateRect(hwnd, IntPtr.Zero, true);
                                    return IntPtr.Zero;
                                }
                                if (mouseX >= 410 && mouseX <= 450) // 切開 (split at current preview page)
                                {
                                    SplitVisualSegmentAtCurrentPage();
                                    InvalidateRect(hwnd, IntPtr.Zero, true);
                                    return IntPtr.Zero;
                                }
                            }

                            // Right Panel Preview Image (Click to Zoom)
                            if (mouseX >= 266 && mouseX <= 478 && mouseY >= 200 + navOffset && mouseY <= 374)
                            {
                                OpenVisualSplitZoom(hwnd);
                                return IntPtr.Zero;
                            }

                            // Bottom Action Buttons
                            if (mouseY >= 380 && mouseY <= 406)
                            {
                                if (mouseX >= 36 && mouseX <= 132) // ＋ 新增區段
                                {
                                    AddVisualSplitSegment();
                                }
                                else if (mouseX >= 138 && mouseX <= 222) // 刪除區段
                                {
                                    DeleteVisualSplitSegment();
                                }
                                else if (mouseX >= 228 && mouseX <= 312) // 清空區段
                                {
                                    ClearVisualSplitSegments();
                                }
                                else if (mouseX >= 336 && mouseX <= 410) // 確定分割
                                {
                                    PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                                    ResizeWindowForVisualSplitter(hwnd, false);
                                    lock (_stateLock)
                                    {
                                        _passwordCancelled = false;
                                        _isPromptingVisualSplitter = false;
                                    }
                                    _passwordEvent.Set();
                                }
                                else if (mouseX >= 416 && mouseX <= 484) // 取消
                                {
                                    PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                                    ResizeWindowForVisualSplitter(hwnd, false);
                                    lock (_stateLock)
                                    {
                                        _passwordCancelled = true;
                                        _isPromptingVisualSplitter = false;
                                    }
                                    _passwordEvent.Set();
                                }
                                InvalidateRect(hwnd, IntPtr.Zero, true);
                                return IntPtr.Zero;
                            }
                        }

                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                if (mouseX >= 456 && mouseX <= 484 && mouseY >= 36 && mouseY <= 64)
                                {
                                    SetupTrayIcon(hwnd);
                                    ShowWindow(hwnd, 0); // SW_HIDE
                                    _isTrayBtnHovered = false;
                                    return IntPtr.Zero;
                                }

                                float logicalPctW = 0;
                                string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
                                if (_pctFont != null && _total > 0)
                                {
                                    using var tempBmp = new Bitmap(1, 1);
                                    using var tempG = Graphics.FromImage(tempBmp);
                                    logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
                                }
                                float logicalMaxMsgW = 448f;
                                if (logicalPctW > 0)
                                {
                                    logicalMaxMsgW = 448f - logicalPctW - 10f;
                                }

                                if (mouseX >= 36 && mouseX <= 36 + logicalMaxMsgW && mouseY >= 148 && mouseY <= 158)
                                {
                                    string statusMsg = _message;
                                    float fullMsgW = 0f;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        if (_msgFont != null)
                                        {
                                            fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                                        }
                                    }

                                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
                                    if (maxLogicalScroll > 0)
                                    {
                                        float clickX = mouseX - 36f;
                                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                                        float thumbX = (_scrollOffset / fullMsgW) * logicalMaxMsgW;
                                        if (thumbX + thumbW > logicalMaxMsgW) thumbX = logicalMaxMsgW - thumbW;

                                        float travelRange = logicalMaxMsgW - thumbW;
                                        float relativePos = travelRange > 0 ? (clickX - thumbW / 2f) / travelRange : 0f;
                                        float newOffset = Math.Max(0f, Math.Min(relativePos * maxLogicalScroll, maxLogicalScroll));

                                        _scrollOffset = newOffset;
                                        _isDraggingScroll = true;
                                        _dragStartMouseX = mouseX;
                                        _dragStartOffset = newOffset;
                                        SetCapture(hwnd);
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0202: // WM_LBUTTONUP
                    {
                        if (_visualSplitZoomDragging)
                        {
                            _visualSplitZoomDragging = false;
                            ReleaseCapture();
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return IntPtr.Zero;
                        }
                        lock (_stateLock)
                        {
                            if (_isDraggingScroll)
                            {
                                _isDraggingScroll = false;
                                ReleaseCapture();
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                    }
                    return IntPtr.Zero;
                case WM_TRAYICON:
                    if (l.ToInt64() == 0x0203) // WM_LBUTTONDBLCLK
                    {
                        ShowWindow(hwnd, 5); // SW_SHOW
                        ShowWindow(hwnd, 9); // SW_RESTORE
                        SetForegroundWindow(hwnd);
                        RemoveTrayIcon();
                    }
                    return IntPtr.Zero;
                case 0x0010: // WM_CLOSE
                    {
                        bool needsConfirmation = false;
                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                needsConfirmation = true;
                            }
                        }

                        if (needsConfirmation)
                        {
                            string text = Localization.T("progress_cancel_confirm", ClickraStorage.GetSetting("Language"));
                            string caption = "Clickra";
                            int btn = MessageBox(hwnd, text, caption, 0x24 | 0x30); // MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2
                            if (btn != 6) // 6 is IDYES
                            {
                                return IntPtr.Zero; // Ignore close
                            }

                            // User confirmed cancellation
                            try { _cts.Cancel(); } catch { }
                            lock (_stateLock)
                            {
                                _passwordCancelled = true;
                            }
                            _passwordEvent.Set(); // Wake up background thread if blocked on password prompt
                            return IntPtr.Zero; // Wait for background thread to handle cancellation and close the window
                        }

                        DestroyWindow(hwnd);
                    }
                    return IntPtr.Zero;
                case 0x02E0: // WM_DPICHANGED
                    {
                        uint newDpi = (uint)(w.ToInt64() & 0xFFFF);
                        _dpiScale = newDpi / 96.0f;
                        RecreateScaledFonts();
                        
                        int clientW = (int)(520 * _dpiScale);
                        int clientH = (int)(280 * _dpiScale);
                        
                        if (_bufferBmp != null)
                        {
                            _bufferGraphics?.Dispose();
                            _bufferBmp?.Dispose();
                            _bufferBmp = new Bitmap(clientW, clientH);
                            _bufferGraphics = Graphics.FromImage(_bufferBmp);
                            _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                        }

                        var rect = Marshal.PtrToStructure<RECT>(l);
                        SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0010 | 0x0004);
                    }
                    return IntPtr.Zero;
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x0113: // WM_TIMER
                    lock (_stateLock)
                    {
                        if (!_completed && !_hasError && !_isPromptingPassword)
                        {
                            if (_currentDispWidth < _targetWidth)
                            {
                                double diff = _targetWidth - _currentDispWidth;
                                double step = diff * 0.15;
                                if (step < 1.0) step = 1.0;
                                
                                _currentDispWidth += step;
                                if (_currentDispWidth >= _targetWidth) _currentDispWidth = _targetWidth;
                            }
                            
                            _shimmerOffset += 5.0f;
                            if (_shimmerOffset > 448) _shimmerOffset = -120;
                            
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    var hdc = BeginPaint(hwnd, out ps);
                    Paint(hdc);
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    KillTimer(hwnd, (IntPtr)1);
                    CleanupResources();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

    }
}
