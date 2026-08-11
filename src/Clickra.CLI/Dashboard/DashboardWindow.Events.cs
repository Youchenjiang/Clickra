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
                case WM_USER_DASHBOARD_ACTION: return HandleUserDashboardAction(hwnd);
                case 0x0005: return HandleSize(hwnd); // WM_SIZE
                case 0x02E0: return HandleDpiChanged(hwnd, w, l); // WM_DPICHANGED
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x000F: return HandlePaint(hwnd); // WM_PAINT
                case 0x0200: return HandleMouseMove(hwnd, l); // WM_MOUSEMOVE
                case 0x0201: // WM_LBUTTONDOWN
                    HandleLButtonDown(hwnd, w, l);
                    return IntPtr.Zero;
                case 0x0233: return HandleDropFiles(hwnd, w); // WM_DROPFILES
                case 0x0102: // WM_CHAR
                    {
                        IntPtr? charResult = HandleChar(hwnd, w);
                        if (charResult != null) return charResult.Value;
                    }
                    break;
                case 0x0100: // WM_KEYDOWN
                    {
                        IntPtr? keyResult = HandleKeyDown(hwnd, w);
                        if (keyResult != null) return keyResult.Value;
                    }
                    break;
                case 0x0202: return HandleLButtonUp(hwnd); // WM_LBUTTONUP
                case 0x0020: // WM_SETCURSOR
                    {
                        IntPtr? cursorResult = HandleSetCursor(hwnd);
                        if (cursorResult != null) return cursorResult.Value;
                    }
                    break;
                case 0x0113: return HandleTimer(hwnd, w); // WM_TIMER
                case 0x020A: return HandleMouseWheel(hwnd, w, l); // WM_MOUSEWHEEL
                case 0x0002: return HandleDestroy(hwnd); // WM_DESTROY
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

        static IntPtr HandleUserDashboardAction(IntPtr hwnd)
        {
            while (_uiActions.TryDequeue(out var action))
            {
                try { action(); } catch { }
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return IntPtr.Zero;
        }

        static IntPtr HandleSize(IntPtr hwnd)
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
            return IntPtr.Zero;
        }

        static IntPtr HandleDpiChanged(IntPtr hwnd, IntPtr w, IntPtr l)
        {
            uint newDpi = (uint)(w.ToInt64() & 0xFFFF);
            _dpiScale = newDpi / 96.0f;
            RecreateScaledFonts();
            var rect = Marshal.PtrToStructure<RECT>(l);
            SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0010 | 0x0004);
            return IntPtr.Zero;
        }

        static IntPtr HandlePaint(IntPtr hwnd)
        {
            var ps = new PAINTSTRUCT();
            var hdc = BeginPaint(hwnd, out ps);
            Paint(hwnd, hdc);
            EndPaint(hwnd, ref ps);
            return IntPtr.Zero;
        }

        static IntPtr HandleMouseMove(IntPtr hwnd, IntPtr l)
        {
            int rawX = (short)(l.ToInt64() & 0xFFFF);
            int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
            int mouseX = (int)(rawX / _dpiScale);
            int mouseY = (int)(rawY / _dpiScale);

            float logW = GetLogicalWidth(hwnd);
            float logH = GetLogicalHeight(hwnd);
            float sidebarW = GetSidebarWidth(logW);

            if (_isDraggingScrollY) UpdateScrollYDrag(hwnd, mouseY, logW, logH);
            else if (_isDraggingScrollX) UpdateScrollXDrag(hwnd, mouseX, logW, logH, sidebarW);
            else if (_isDraggingDetailScroll) UpdateDetailScrollDrag(hwnd, mouseX, logW);
            else if (_isDraggingPdfSlider) UpdatePdfSliderDrag(hwnd, mouseX, sidebarW);

            int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
            int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

            TrackDropdownHover(hwnd, adjMouseX, adjMouseY, logW);

            int prevHovered = _hoveredElement;
            _hoveredElement = HitTest(hwnd, adjMouseX, adjMouseY);
            if (_hoveredElement != prevHovered)
            {
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return IntPtr.Zero;
        }

        /// <summary>Moves the vertical scrollbar thumb while it is being dragged.</summary>
        static void UpdateScrollYDrag(IntPtr hwnd, int mouseY, float logW, float logH)
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

        /// <summary>Moves the horizontal scrollbar thumb while it is being dragged.</summary>
        static void UpdateScrollXDrag(IntPtr hwnd, int mouseX, float logW, float logH, float sidebarW)
        {
            float deltaX = mouseX - _dragStartMouseX;
            float trackX = sidebarW + 4;
            float trackW = (logW - sidebarW) - 8;
            float contentH = GetContentHeight(hwnd);
            bool showV = logH < contentH;
            if (showV) trackW = (logW - sidebarW) - 16;
            if (trackW <= 0) return;

            float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW);
            float scrollRange = 760f - logW;
            float trackRange = trackW - thumbW;
            if (trackRange <= 0) return;

            float deltaScrollX = (deltaX / trackRange) * scrollRange;
            _contentScrollX = Math.Max(0, Math.Min(_dragStartScrollX + deltaScrollX, scrollRange));
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Moves the history detail-field scrollbar thumb while it is being dragged.</summary>
        static void UpdateDetailScrollDrag(IntPtr hwnd, int mouseX, float logW)
        {
            if (_draggingDetailRowIndex < 0 || _draggingDetailRowIndex >= _historyEntries.Count) return;
            string textToScroll = GetHistoryDetailText(_historyEntries[_draggingDetailRowIndex], _draggingDetailFieldIndex);
            if (string.IsNullOrEmpty(textToScroll)) return;

            float textW;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                textW = tempG.MeasureString(textToScroll, _subFont!).Width / _dpiScale;
            }
            float maxValW = GetMaxValW(logW);
            float maxScroll = Math.Max(0f, textW - maxValW);
            if (maxScroll <= 0) return;

            float thumbW = Math.Max(15f, (maxValW / textW) * maxValW);
            float travelRange = maxValW - thumbW;
            if (travelRange <= 0) return;

            float deltaX = mouseX - _dragDetailStartMouseX;
            float deltaOffset = (deltaX / travelRange) * maxScroll;
            float newOffset = Math.Max(0f, Math.Min(_dragDetailStartOffset + deltaOffset, maxScroll));
            DetailScrollOffsets[(_draggingDetailRowIndex, _draggingDetailFieldIndex)] = newOffset;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Snaps the PDF compression slider to the nearest level while it is dragged.</summary>
        static void UpdatePdfSliderDrag(IntPtr hwnd, int mouseX, float sidebarW)
        {
            // Drag on compress slider: real-time snap with equal 25% interval widths
            float sliderMouseX = mouseX >= sidebarW ? mouseX + _contentScrollX : mouseX;
            float relX = sliderMouseX - _pdfSliderTrackX;
            float fraction = Math.Max(0f, Math.Min(1f, relX / _pdfSliderTrackW));
            int newLevel = (int)Math.Max(0, Math.Min(3, Math.Floor(fraction * 4)));
            string current = ClickraStorage.GetSetting("PdfCompressImageLevel");
            if (current != newLevel.ToString())
            {
                ApplyPdfCompressLevel(hwnd, newLevel);
            }
        }

        /// <summary>Tracks the hovered row inside the open language dropdowns.</summary>
        static void TrackDropdownHover(IntPtr hwnd, int adjMouseX, int adjMouseY, float logW)
        {
            if (_langDropdownOpen)
            {
                int popupY = _langDropdownY - 180;
                float contentX = GetContentX(logW);
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
                float contentX = GetContentX(logW);
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
        }

        static IntPtr HandleDropFiles(IntPtr hwnd, IntPtr w)
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
            return IntPtr.Zero;
        }

        static IntPtr? HandleChar(IntPtr hwnd, IntPtr w)
        {
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
            return null;
        }

        static IntPtr? HandleKeyDown(IntPtr hwnd, IntPtr w)
        {
            if (!_langDropdownOpen) return null;

            int key = w.ToInt32();
            if (key == 0x1B) // VK_ESCAPE
            {
                _langDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }
            if (key == 0x26) // VK_UP
            {
                MoveLangDropdownSelection(hwnd, -1);
                return IntPtr.Zero;
            }
            if (key == 0x28) // VK_DOWN
            {
                MoveLangDropdownSelection(hwnd, +1);
                return IntPtr.Zero;
            }
            return null;
        }

        /// <summary>Moves the language-dropdown selection by one row, keeping the hovered
        /// item visible inside the 5-row viewport.</summary>
        static void MoveLangDropdownSelection(IntPtr hwnd, int delta)
        {
            var filtered = GetFilteredLanguages();
            if (filtered.Count == 0) return;

            _langHoveredIndex = (_langHoveredIndex + delta + filtered.Count) % filtered.Count;
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

        static IntPtr HandleLButtonUp(IntPtr hwnd)
        {
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
        }

        static IntPtr? HandleSetCursor(IntPtr hwnd)
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
            return null;
        }

        static IntPtr HandleTimer(IntPtr hwnd, IntPtr w)
        {
            if (w == TIMER_ID_REFRESH)
            {
                RefreshHistoryData();
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return IntPtr.Zero;
        }

        static IntPtr HandleMouseWheel(IntPtr hwnd, IntPtr w, IntPtr l)
        {
            short delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
            int scrollDir = delta > 0 ? -1 : 1;
            if (_langDropdownOpen)
            {
                var filtered = GetFilteredLanguages();
                _langScrollOffset = Math.Max(0, Math.Min(_langScrollOffset + scrollDir, filtered.Count - 5));
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (_activeTab == 2 && HandleHistoryDetailWheel(hwnd, l, scrollDir))
            {
                // Detail-field scroll consumed the wheel.
            }
            else
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
            return IntPtr.Zero;
        }

        /// <summary>Scrolls a history detail field when the wheel is over an expanded row's
        /// detail area; returns true when the wheel was consumed.</summary>
        static bool HandleHistoryDetailWheel(IntPtr hwnd, IntPtr l, int scrollDir)
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

            int currentY = 90 + GetActiveHistoryCount() * 52;
            for (int i = 0; i < _historyEntries.Count; i++)
            {
                bool isExpanded = (i == _expandedHistoryIndex);
                int rowH = isExpanded ? 160 : 44;
                if (isExpanded && adjMouseY >= currentY && adjMouseY < currentY + rowH)
                {
                    return ScrollHistoryDetailField(hwnd, i, adjMouseX, adjMouseY, currentY, logW, scrollDir);
                }
                currentY += rowH + 8;
            }
            return false;
        }

        /// <summary>Scrolls the detail field under the wheel cursor by one step.</summary>
        static bool ScrollHistoryDetailField(IntPtr hwnd, int rowIndex, int adjMouseX, int adjMouseY, int currentY, float logW, int scrollDir)
        {
            var entry = _historyEntries[rowIndex];
            float contentX = GetContentX(logW);
            int rowW = (int)logW - (int)contentX - 40;

            float inputLabelW, outputLabelW, timeLabelW, errorLabelW;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                inputLabelW = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont!).Width / _dpiScale;
                outputLabelW = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont!).Width / _dpiScale;
                timeLabelW = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont!).Width / _dpiScale;
                errorLabelW = tempG.MeasureString(GetText(entry.IsSuccess ? "history_detail_elapsed" : "history_detail_error") + ":", _subFont!).Width / _dpiScale;
            }
            float maxLabelW = Math.Max(inputLabelW, Math.Max(outputLabelW, Math.Max(timeLabelW, errorLabelW)));
            float valX = contentX + 12 + maxLabelW + 16;
            float maxValW = contentX + rowW - 12 - valX;

            if (adjMouseX < contentX + 12 || adjMouseX >= contentX + rowW - 12) return false;

            int fieldIndex = GetHistoryDetailWheelFieldIndex(adjMouseY, currentY);
            if (fieldIndex == -1) return false;

            string textToScroll = GetHistoryDetailWheelText(entry, fieldIndex);
            if (string.IsNullOrEmpty(textToScroll)) return false;

            float textW;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                textW = tempG.MeasureString(textToScroll, _subFont!).Width / _dpiScale;
            }
            float maxScroll = Math.Max(0f, textW - maxValW);
            if (maxScroll <= 0) return false;

            var key = (rowIndex, fieldIndex);
            float currentOffset = 0;
            DetailScrollOffsets.TryGetValue(key, out currentOffset);
            float nextOffset = Math.Max(0f, Math.Min(currentOffset + scrollDir * 30, maxScroll));
            DetailScrollOffsets[key] = nextOffset;
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        /// <summary>Maps a detail-row Y offset to its field index (-1 when between fields).</summary>
        static int GetHistoryDetailWheelFieldIndex(int adjMouseY, int currentY)
        {
            if (adjMouseY >= currentY + 50 && adjMouseY < currentY + 72) return 0;
            if (adjMouseY >= currentY + 76 && adjMouseY < currentY + 98) return 1;
            if (adjMouseY >= currentY + 128 && adjMouseY < currentY + 150) return 2;
            return -1;
        }

        /// <summary>Returns the text of a history detail field as shown in the expanded row.</summary>
        static string GetHistoryDetailWheelText(ClickraStorage.HistoryEntry entry, int fieldIndex)
        {
            if (fieldIndex == 0)
            {
                return (entry.InputPaths ?? "").Replace(";", ", ");
            }
            if (fieldIndex == 1)
            {
                return entry.OutputPath ?? "";
            }
            if (entry.IsSuccess)
            {
                return entry.ElapsedMs >= 0 ? $"{entry.ElapsedMs / 1000.0:F2} s ({entry.ElapsedMs} ms)" : "N/A";
            }
            return entry.ErrorMessage ?? "";
        }

        static IntPtr HandleDestroy(IntPtr hwnd)
        {
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
    }
}
