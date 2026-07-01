using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Clickra.Core;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static readonly WndProcDelegate _wndProc = WndProc;
        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case WM_USER_DASHBOARD_ACTION:
                    while (_uiActions.TryDequeue(out var action))
                    {
                        try { action(); } catch { }
                    }
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case 0x0005: // WM_SIZE
                    {
                        int clientW = GetClientWidth(hwnd);
                        int clientH = GetClientHeight(hwnd);
                        float logW = clientW / _dpiScale;
                        float logH = clientH / _dpiScale;
                        float contentH = GetContentHeight(hwnd);
                        int maxScrollX = Math.Max(0, 760 - (int)logW);
                        int maxScrollY = Math.Max(0, (int)contentH - (int)logH);
                        _contentScrollX = Math.Max(0, Math.Min(_contentScrollX, maxScrollX));
                        _contentScrollY = Math.Max(0, Math.Min(_contentScrollY, maxScrollY));
                        RecreateBuffer(clientW, clientH);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x02E0: // WM_DPICHANGED
                    {
                        uint newDpi = (uint)(w.ToInt64() & 0xFFFF);
                        _dpiScale = newDpi / 96.0f;
                        RecreateScaledFonts();
                        var rect = Marshal.PtrToStructure<RECT>(l);
                        SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0010 | 0x0004);
                    }
                    return IntPtr.Zero;
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    var hdc = BeginPaint(hwnd, out ps);
                    Paint(hwnd, hdc);
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                case 0x0200: // WM_MOUSEMOVE
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        float logW = GetLogicalWidth(hwnd);
                        float logH = GetLogicalHeight(hwnd);
                        float sidebarW = GetSidebarWidth(logW);
                        float contentX = GetContentX(logW);
                        
                        if (_isDraggingScrollY)
                        {
                            float deltaY = mouseY - _dragStartMouseY;
                            float contentH = GetContentHeight(hwnd);

                            float trackH = logH - 8;
                            if (logW < 760) trackH = logH - 16;
                            float thumbH = Math.Max(20f, (logH / contentH) * trackH);
                            float scrollRange = contentH - logH;
                            float trackRange = trackH - thumbH;
                            if (trackRange > 0)
                            {
                                float deltaScrollY = (deltaY / trackRange) * scrollRange;
                                _contentScrollY = Math.Max(0, Math.Min(_dragStartScrollY + deltaScrollY, scrollRange));
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (_isDraggingScrollX)
                        {
                            float deltaX = mouseX - _dragStartMouseX;
                            float trackX = sidebarW + 4;
                            float trackW_sb = (logW - sidebarW) - 8;
                            float contentH = GetContentHeight(hwnd);
                            bool showV = logH < contentH;
                            if (showV) trackW_sb = (logW - sidebarW) - 16;
                            if (trackW_sb > 0)
                            {
                                float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW_sb);
                                float scrollRange = 760f - logW;
                                float trackRange = trackW_sb - thumbW;
                                if (trackRange > 0)
                                {
                                    float deltaScrollX = (deltaX / trackRange) * scrollRange;
                                    _contentScrollX = Math.Max(0, Math.Min(_dragStartScrollX + deltaScrollX, scrollRange));
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                            }
                        }
                        else if (_isDraggingDetailScroll)
                        {
                            if (_draggingDetailRowIndex >= 0 && _draggingDetailRowIndex < _historyEntries.Count)
                            {
                                var entry = _historyEntries[_draggingDetailRowIndex];
                                string textToScroll = "";
                                if (_draggingDetailFieldIndex == 0)
                                {
                                    textToScroll = entry.InputPaths ?? "";
                                    textToScroll = textToScroll.Replace(";", ", ");
                                }
                                else if (_draggingDetailFieldIndex == 1)
                                {
                                    textToScroll = entry.OutputPath ?? "";
                                }
                                else if (_draggingDetailFieldIndex == 2)
                                {
                                    textToScroll = !string.IsNullOrEmpty(entry.ErrorMessage) ? entry.ErrorMessage : "";
                                    if (textToScroll.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
                                    {
                                        textToScroll = GetText("error_user_aborted");
                                    }
                                }

                                if (!string.IsNullOrEmpty(textToScroll))
                                {
                                    float textW;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        textW = tempG.MeasureString(textToScroll, _subFont!).Width / _dpiScale;
                                    }
                                    float maxValW = GetMaxValW(logW);
                                    float maxScroll = Math.Max(0f, textW - maxValW);

                                    if (maxScroll > 0)
                                    {
                                        float thumbW = Math.Max(15f, (maxValW / textW) * maxValW);
                                        float travelRange = maxValW - thumbW;
                                        if (travelRange > 0)
                                        {
                                            float deltaX = mouseX - _dragDetailStartMouseX;
                                            float deltaOffset = (deltaX / travelRange) * maxScroll;
                                            float newOffset = Math.Max(0f, Math.Min(_dragDetailStartOffset + deltaOffset, maxScroll));
                                            DetailScrollOffsets[(_draggingDetailRowIndex, _draggingDetailFieldIndex)] = newOffset;
                                            InvalidateRect(hwnd, IntPtr.Zero, false);
                                        }
                                    }
                                }
                            }
                        }
                        else if (_isDraggingPdfSlider)
                        {
                            // Drag on compress slider: real-time snap
                            float sliderMouseX = mouseX >= sidebarW ? mouseX + _contentScrollX : mouseX;
                            float relX = sliderMouseX - _pdfSliderTrackX;
                            float fraction = Math.Max(0f, Math.Min(1f, relX / _pdfSliderTrackW));
                            int newLevel = (int)Math.Round(fraction * 3);
                            string current = ClickraStorage.GetSetting("PdfCompressImageLevel");
                            if (current != newLevel.ToString())
                            {
                                ApplyPdfCompressLevel(hwnd, newLevel);
                            }
                        }

                        int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
                        int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

                        if (_langDropdownOpen)
                        {
                            int popupY = _langDropdownY - 180;
                            if (adjMouseX >= contentX && adjMouseX <= contentX + 240 && adjMouseY >= popupY + 38 && adjMouseY < _langDropdownY)
                            {
                                int idx = _langScrollOffset + (adjMouseY - (popupY + 38)) / 26;
                                var filtered = GetFilteredLanguages();
                                if (idx >= 0 && idx < filtered.Count && idx != _langHoveredIndex)
                                {
                                    _langHoveredIndex = idx;
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                            }
                        }


                        if (_pdfLangDropdownOpen)
                        {
                            int popupHeight = PdfLangs.Length * 26 + 8;
                            int popupY = _pdfLangDropdownY - popupHeight;
                            if (adjMouseX >= contentX && adjMouseX <= contentX + 240 && adjMouseY >= popupY + 4 && adjMouseY < _pdfLangDropdownY - 4)
                            {
                                int idx = (adjMouseY - (popupY + 4)) / 26;
                                if (idx >= 0 && idx < PdfLangs.Length && idx != _pdfLangHoveredIndex)
                                {
                                    _pdfLangHoveredIndex = idx;
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                            }
                        }

                        int prevHovered = _hoveredElement;
                        _hoveredElement = HitTest(hwnd, adjMouseX, adjMouseY);
                        if (_hoveredElement != prevHovered)
                        {
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x0201: // WM_LBUTTONDOWN
                    HandleLButtonDown(hwnd, w, l);
                    return IntPtr.Zero;
                case 0x0233: // WM_DROPFILES
                    {
                        IntPtr hDrop = w;
                        uint fileCount = DragQueryFileW(hDrop, 0xFFFFFFFF, IntPtr.Zero, 0);
                        var droppedFiles = new List<string>();
                        for (uint i = 0; i < fileCount; i++)
                        {
                            uint length = DragQueryFileW(hDrop, i, IntPtr.Zero, 0);
                            if (length > 0)
                            {
                                IntPtr buffer = Marshal.AllocHGlobal((int)(length + 1) * 2);
                                if (DragQueryFileW(hDrop, i, buffer, length + 1) > 0)
                                {
                                    string? file = Marshal.PtrToStringUni(buffer);
                                    if (!string.IsNullOrEmpty(file))
                                    {
                                        droppedFiles.Add(file);
                                    }
                                }
                                Marshal.FreeHGlobal(buffer);
                            }
                        }
                        DragFinish(hDrop);

                        if (droppedFiles.Count > 0)
                        {
                            HandleDroppedFiles(droppedFiles);
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x0102: // WM_CHAR
                    if (_langDropdownOpen)
                    {
                        char c = (char)w.ToInt32();
                        if (c == '\b') // Backspace
                        {
                            if (_langSearchQuery.Length > 0)
                            {
                                _langSearchQuery = _langSearchQuery.Substring(0, _langSearchQuery.Length - 1);
                                _langHoveredIndex = 0;
                                _langScrollOffset = 0;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (c == 27) // Escape
                        {
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (c == '\r') // Enter
                        {
                            var filtered = GetFilteredLanguages();
                            if (filtered.Count > 0 && _langHoveredIndex >= 0 && _langHoveredIndex < filtered.Count)
                            {
                                SelectLanguage(filtered[_langHoveredIndex].Code);
                            }
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (!char.IsControl(c))
                        {
                            _langSearchQuery += c;
                            _langHoveredIndex = 0;
                            _langScrollOffset = 0;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        return IntPtr.Zero;
                    }
                    break;
                case 0x0100: // WM_KEYDOWN
                    if (_langDropdownOpen)
                    {
                        int key = w.ToInt32();
                        if (key == 0x1B) // VK_ESCAPE
                        {
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return IntPtr.Zero;
                        }
                        else if (key == 0x26) // VK_UP
                        {
                            var filtered = GetFilteredLanguages();
                            if (filtered.Count > 0)
                            {
                                _langHoveredIndex = (_langHoveredIndex - 1 + filtered.Count) % filtered.Count;
                                if (_langHoveredIndex < _langScrollOffset)
                                {
                                    _langScrollOffset = _langHoveredIndex;
                                }
                                else if (_langHoveredIndex >= _langScrollOffset + 5)
                                {
                                    _langScrollOffset = _langHoveredIndex - 4;
                                }
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            return IntPtr.Zero;
                        }
                        else if (key == 0x28) // VK_DOWN
                        {
                            var filtered = GetFilteredLanguages();
                            if (filtered.Count > 0)
                            {
                                _langHoveredIndex = (_langHoveredIndex + 1) % filtered.Count;
                                if (_langHoveredIndex < _langScrollOffset)
                                {
                                    _langScrollOffset = _langHoveredIndex;
                                }
                                else if (_langHoveredIndex >= _langScrollOffset + 5)
                                {
                                    _langScrollOffset = _langHoveredIndex - 4;
                                }
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            return IntPtr.Zero;
                        }
                    }
                    break;
                case 0x0202: // WM_LBUTTONUP
                    if (_isDraggingScrollX || _isDraggingScrollY || _isDraggingDetailScroll || _isDraggingPdfSlider)
                    {
                        _isDraggingScrollX = false;
                        _isDraggingScrollY = false;
                        _isDraggingDetailScroll = false;
                        _isDraggingPdfSlider = false;
                        ReleaseCapture();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x0020: // WM_SETCURSOR
                    {
                        var pt = new Point();
                        if (GetCursorPos(out pt))
                        {
                            ScreenToClient(hwnd, ref pt);
                            int mouseX = (int)(pt.X / _dpiScale);
                            int mouseY = (int)(pt.Y / _dpiScale);
                            float logW = GetLogicalWidth(hwnd);
                            float logH = GetLogicalHeight(hwnd);
                            float contentH = GetContentHeight(hwnd);
                            bool showV = logH < contentH;
                            bool showH = logW < 760;
                             if ((showV && mouseX >= logW - 8 && mouseX < logW) ||
                                 (showH && mouseY >= logH - 8 && mouseY < logH && mouseX >= GetSidebarWidth(logW)))
                            {
                                SetCursor(LoadCursorW(IntPtr.Zero, IDC_HAND));
                                return (IntPtr)1;
                            }
                        }
                        if (_hoveredElement != -1 || _langDropdownOpen || IsHoveringHistoryRow(hwnd) || _isDraggingScrollX || _isDraggingScrollY || _isDraggingDetailScroll)
                        {
                            SetCursor(LoadCursorW(IntPtr.Zero, IDC_HAND));
                            return (IntPtr)1; // Handled
                        }
                    }
                    break;
                case 0x0113: // WM_TIMER
                    if (w == TIMER_ID_REFRESH)
                    {
                        RefreshHistoryData();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x020A: // WM_MOUSEWHEEL
                    {
                        short delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
                        int scrollDir = delta > 0 ? -1 : 1;
                        if (_langDropdownOpen)
                        {
                            var filtered = GetFilteredLanguages();
                            _langScrollOffset = Math.Max(0, Math.Min(_langScrollOffset + scrollDir, filtered.Count - 5));
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else
                        {
                            bool handledDetailScroll = false;
                            if (_activeTab == 2)
                            {
                                int screenX = (short)(l.ToInt64() & 0xFFFF);
                                int screenY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                                var pt = new Point(screenX, screenY);
                                ScreenToClient(hwnd, ref pt);
                                int mouseX = (int)(pt.X / _dpiScale);
                                int mouseY = (int)(pt.Y / _dpiScale);

                                float logW = GetLogicalWidth(hwnd);
                                float sidebarW = GetSidebarWidth(logW);
                                int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
                                int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

                                var activeEntry = ClickraStorage.GetActiveEntry();
                                int activeCount = 0;
                                if (activeEntry.HasValue)
                                {
                                    var ae = activeEntry.Value;
                                    var activeFiles = !string.IsNullOrEmpty(ae.InputPaths)
                                        ? ae.InputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        : Array.Empty<string>();
                                    activeCount = activeFiles.Length > 0 ? activeFiles.Length : 1;
                                }
                                int startY = 90 + activeCount * 52;
                                int currentY = startY;

                                for (int i = 0; i < _historyEntries.Count; i++)
                                {
                                    bool isExpanded = (i == _expandedHistoryIndex);
                                    int rowH = isExpanded ? 160 : 44;
                                    if (isExpanded && adjMouseY >= currentY && adjMouseY < currentY + rowH)
                                    {
                                        float contentX = GetContentX(logW);
                                        int rowW = (int)logW - (int)contentX - 40;

                                        float inputLabelW, outputLabelW, timeLabelW, errorLabelW;
                                        using (var tempBmp = new Bitmap(1, 1))
                                        using (var tempG = Graphics.FromImage(tempBmp))
                                        {
                                            inputLabelW = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont!).Width / _dpiScale;
                                            outputLabelW = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont!).Width / _dpiScale;
                                            timeLabelW = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont!).Width / _dpiScale;
                                            errorLabelW = tempG.MeasureString(GetText(_historyEntries[i].IsSuccess ? "history_detail_elapsed" : "history_detail_error") + ":", _subFont!).Width / _dpiScale;
                                        }
                                        float maxLabelW = Math.Max(inputLabelW, Math.Max(outputLabelW, Math.Max(timeLabelW, errorLabelW)));
                                        float valX = contentX + 12 + maxLabelW + 16;
                                        float maxValW = contentX + rowW - 12 - valX;

                                        if (adjMouseX >= contentX + 12 && adjMouseX < contentX + rowW - 12)
                                        {
                                            int fieldIndex = -1;
                                            string textToScroll = "";

                                            if (adjMouseY >= currentY + 50 && adjMouseY < currentY + 72)
                                            {
                                                fieldIndex = 0;
                                                textToScroll = _historyEntries[i].InputPaths ?? "";
                                                textToScroll = textToScroll.Replace(";", ", ");
                                            }
                                            else if (adjMouseY >= currentY + 76 && adjMouseY < currentY + 98)
                                            {
                                                fieldIndex = 1;
                                                textToScroll = _historyEntries[i].OutputPath ?? "";
                                            }
                                            else if (adjMouseY >= currentY + 128 && adjMouseY < currentY + 150)
                                            {
                                                fieldIndex = 2;
                                                textToScroll = _historyEntries[i].IsSuccess
                                                    ? (_historyEntries[i].ElapsedMs >= 0 ? $"{(_historyEntries[i].ElapsedMs / 1000.0):F2} s ({_historyEntries[i].ElapsedMs} ms)" : "N/A")
                                                    : (_historyEntries[i].ErrorMessage ?? "");
                                            }

                                            if (fieldIndex != -1 && !string.IsNullOrEmpty(textToScroll))
                                            {
                                                float textW;
                                                using (var tempBmp = new Bitmap(1, 1))
                                                using (var tempG = Graphics.FromImage(tempBmp))
                                                {
                                                    textW = tempG.MeasureString(textToScroll, _subFont!).Width / _dpiScale;
                                                }
                                                float maxScroll = Math.Max(0f, textW - maxValW);

                                                if (maxScroll > 0)
                                                {
                                                    var key = (i, fieldIndex);
                                                    float currentOffset = 0;
                                                    DetailScrollOffsets.TryGetValue(key, out currentOffset);
                                                    float nextOffset = Math.Max(0f, Math.Min(currentOffset + scrollDir * 30, maxScroll));
                                                    DetailScrollOffsets[key] = nextOffset;
                                                    handledDetailScroll = true;
                                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                                }
                                            }
                                        }
                                        break;
                                    }
                                    currentY += rowH + 8;
                                }
                            }

                            if (!handledDetailScroll)
                            {
                                float logH = GetLogicalHeight(hwnd);
                                float contentH = GetContentHeight(hwnd);
                                if (logH < contentH)
                                {
                                    int maxScrollY = (int)contentH - (int)logH;
                                    _contentScrollY = Math.Max(0, Math.Min(_contentScrollY + scrollDir * 20, maxScrollY));
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    try
                    {
                        KillTimer(hwnd, TIMER_ID_REFRESH);
                        CleanupResources();
                    }
                    finally
                    {
                        if (_mutex != null)
                        {
                            try
                            {
                                _mutex.ReleaseMutex();
                            }
                            catch { }
                            finally
                            {
                                _mutex.Dispose();
                                _mutex = null;
                            }
                        }
                    }
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }
    }
}
