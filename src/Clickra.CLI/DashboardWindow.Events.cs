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

                        float logW = LogicalWidth(hwnd);
                        float logH = LogicalHeight(hwnd);
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
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        float logW = LogicalWidth(hwnd);
                        float logH = LogicalHeight(hwnd);
                        float contentH = GetContentHeight(hwnd);
                        bool showV = logH < contentH;
                        bool showH = logW < 760;
                        float sidebarW = GetSidebarWidth(logW);
                        float contentX = GetContentX(logW);

                        if (showV && mouseX >= logW - 8 && mouseX < logW)
                        {
                            float trackY = 4;
                            float trackH = logH - 8;
                            if (showH) trackH = logH - 16;
                            float thumbH = Math.Max(20f, (logH / contentH) * trackH);
                            float thumbY = trackY + (_contentScrollY / (contentH - logH)) * (trackH - thumbH);

                            if (mouseY >= trackY && mouseY < trackY + trackH)
                            {
                                if (mouseY >= thumbY && mouseY < thumbY + thumbH)
                                {
                                    _isDraggingScrollY = true;
                                    _dragStartMouseY = mouseY;
                                    _dragStartScrollY = _contentScrollY;
                                    SetCapture(hwnd);
                                }
                                else
                                {
                                    float relativePos = (mouseY - trackY - thumbH / 2f) / (trackH - thumbH);
                                    _contentScrollY = Math.Max(0, Math.Min(relativePos * (contentH - logH), contentH - logH));
                                    _isDraggingScrollY = true;
                                    _dragStartMouseY = mouseY;
                                    _dragStartScrollY = _contentScrollY;
                                    SetCapture(hwnd);
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                                return IntPtr.Zero;
                            }
                        }

                        if (showH && mouseY >= logH - 8 && mouseY < logH && mouseX >= sidebarW)
                        {
                            float trackX = sidebarW + 4;
                            float trackW_sb = (logW - sidebarW) - 8;
                            if (showV) trackW_sb = (logW - sidebarW) - 16;
                            if (trackW_sb > 0)
                            {
                                float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW_sb);
                                float thumbX = trackX + (_contentScrollX / (760f - logW)) * (trackW_sb - thumbW);

                                if (mouseX >= trackX && mouseX < trackX + trackW_sb)
                                {
                                    if (mouseX >= thumbX && mouseX < thumbX + thumbW)
                                    {
                                        _isDraggingScrollX = true;
                                        _dragStartMouseX = mouseX;
                                        _dragStartScrollX = _contentScrollX;
                                        SetCapture(hwnd);
                                    }
                                    else
                                    {
                                        float trackRange = trackW_sb - thumbW;
                                        float relativePos = trackRange > 0 ? (mouseX - trackX - thumbW / 2f) / trackRange : 0f;
                                        _contentScrollX = Math.Max(0, Math.Min(relativePos * (760f - logW), 760f - logW));
                                        _isDraggingScrollX = true;
                                        _dragStartMouseX = mouseX;
                                        _dragStartScrollX = _contentScrollX;
                                        SetCapture(hwnd);
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                    return IntPtr.Zero;
                                }
                            }
                        }

                        int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
                        int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;



                        if (_pdfLangDropdownOpen)
                        {
                            int popupHeight = PdfLangs.Length * 26 + 8;
                            int popupY = _pdfLangDropdownY - popupHeight;
                            if (adjMouseX >= contentX && adjMouseX <= contentX + 240 && adjMouseY >= popupY && adjMouseY < _pdfLangDropdownY)
                            {
                                if (adjMouseY >= popupY + 4 && adjMouseY < _pdfLangDropdownY - 4)
                                {
                                    int clickedIdx = (adjMouseY - (popupY + 4)) / 26;
                                    if (clickedIdx >= 0 && clickedIdx < PdfLangs.Length)
                                    {
                                        ClickraStorage.SaveSetting("TranslateTargetLang", PdfLangs[clickedIdx].Code);
                                    }
                                }
                                _pdfLangDropdownOpen = false;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                                return IntPtr.Zero;
                            }
                            _pdfLangDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return IntPtr.Zero;
                        }

                        if (_langDropdownOpen)
                        {
                            int popupY = _langDropdownY - 180;
                            if (adjMouseX >= GetContentX(logW) && adjMouseX <= GetContentX(logW) + 240)
                            {
                                if (adjMouseY >= popupY && adjMouseY < popupY + 38)
                                {
                                    // Clicked search box or container top, do nothing but keep open
                                    return IntPtr.Zero;
                                }
                                else if (adjMouseY >= popupY + 38 && adjMouseY < _langDropdownY)
                                {
                                    int clickedIdx = _langScrollOffset + (adjMouseY - (popupY + 38)) / 26;
                                    var filtered = GetFilteredLanguages();
                                    if (clickedIdx >= 0 && clickedIdx < filtered.Count)
                                    {
                                        SelectLanguage(filtered[clickedIdx].Code);
                                    }
                                    _langDropdownOpen = false;
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                    return IntPtr.Zero;
                                }
                            }
                            
                            // Clicked outside dropdown
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return IntPtr.Zero;
                        }

                        if (_activeTab == 2)
                        {
                            float virtLogW = Math.Max(760f, logW);
                            if (adjMouseX >= GetContentX(logW) && adjMouseX < virtLogW - 40)
                            {
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
                                int clickedIndex = -1;
                                bool clickedDetails = false;
                                int detailFieldIndex = -1;
                                for (int i = 0; i < _historyEntries.Count; i++)
                                {
                                    bool isExpanded = (i == _expandedHistoryIndex);
                                    int rowH = isExpanded ? 160 : 44;
                                    if (adjMouseY >= currentY && adjMouseY < currentY + rowH)
                                    {
                                        if (isExpanded && adjMouseY >= currentY + 44)
                                        {
                                            clickedDetails = true;
                                            clickedIndex = i;
                                            int relY = adjMouseY - currentY;
                                            if (relY >= 50 && relY < 76) detailFieldIndex = 0;
                                            else if (relY >= 76 && relY < 102) detailFieldIndex = 1;
                                            else if (relY >= 128 && relY < 156) detailFieldIndex = 2;
                                        }
                                        else
                                        {
                                            clickedIndex = i;
                                        }
                                        break;
                                    }
                                    currentY += rowH + 8;
                                }

                                if (clickedIndex != -1)
                                {
                                    if (clickedDetails)
                                    {
                                        if (detailFieldIndex != -1)
                                        {
                                            string textToScroll = "";
                                            if (detailFieldIndex == 0)
                                            {
                                                textToScroll = _historyEntries[clickedIndex].InputPaths.Replace(";", ", ");
                                            }
                                            else if (detailFieldIndex == 1)
                                            {
                                                textToScroll = _historyEntries[clickedIndex].OutputPath;
                                            }
                                            else if (detailFieldIndex == 2)
                                            {
                                                textToScroll = !string.IsNullOrEmpty(_historyEntries[clickedIndex].ErrorMessage) ? _historyEntries[clickedIndex].ErrorMessage : "";
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
                                                    // contentX is already defined in the outer scope
                                                    float w1, w2, w3, w4;
                                                    using (var tempBmp = new Bitmap(1, 1))
                                                    using (var tempG = Graphics.FromImage(tempBmp))
                                                    {
                                                        w1 = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont!).Width / _dpiScale;
                                                        w2 = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont!).Width / _dpiScale;
                                                        w3 = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont!).Width / _dpiScale;
                                                        w4 = tempG.MeasureString(GetText("history_detail_error") + ":", _subFont!).Width / _dpiScale;
                                                    }
                                                    float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
                                                    float valX = contentX + 12 + maxLabelW + 16;
                                                    float rowWLocal = virtLogW - 40 - contentX;

                                                    if (adjMouseX >= valX && adjMouseX <= contentX + rowWLocal - 12)
                                                    {
                                                        float clickX = adjMouseX - valX;
                                                        float thumbW = Math.Max(15f, (maxValW / textW) * maxValW);
                                                        float currentOffset = 0;
                                                        DetailScrollOffsets.TryGetValue((clickedIndex, detailFieldIndex), out currentOffset);

                                                        float thumbX = (currentOffset / textW) * maxValW;
                                                        if (thumbX + thumbW > maxValW) thumbX = maxValW - thumbW;

                                                        float travelRange = maxValW - thumbW;
                                                        float relativePos = travelRange > 0 ? (clickX - thumbW / 2f) / travelRange : 0f;
                                                        float newOffset = Math.Max(0f, Math.Min(relativePos * maxScroll, maxScroll));

                                                        DetailScrollOffsets[(clickedIndex, detailFieldIndex)] = newOffset;

                                                        _isDraggingDetailScroll = true;
                                                        _draggingDetailRowIndex = clickedIndex;
                                                        _draggingDetailFieldIndex = detailFieldIndex;
                                                        _dragDetailStartMouseX = mouseX;
                                                        _dragDetailStartOffset = newOffset;
                                                        SetCapture(hwnd);
                                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                                    }
                                                }
                                            }
                                        }
                                        return IntPtr.Zero;
                                    }
                                    else
                                    {
                                        if (_expandedHistoryIndex == clickedIndex)
                                        {
                                            _expandedHistoryIndex = -1; // Collapse
                                        }
                                        else
                                        {
                                            _expandedHistoryIndex = clickedIndex; // Expand
                                        }
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                        return IntPtr.Zero;
                                    }
                                }
                            }
                        }

                        int element = HitTest(hwnd, adjMouseX, adjMouseY);
                        if (element >= 0 && element <= 4)
                        {
                            _activeTab = element;
                            if (_activeTab == 0 || _activeTab == 2)
                            {
                                RefreshHistoryData();
                            }

                            _langScrollOffset = 0;
                            _contentScrollX = 0;
                            _contentScrollY = 0;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 22) // Clear history
                        {
                            if (MessageBox(hwnd, GetText("history_clear_confirm"), "Clickra", 0x24) == 6) // MB_YESNO | MB_ICONQUESTION, 6 is IDYES
                            {
                                ClickraStorage.ClearHistory();
                                _expandedHistoryIndex = -1;
                                RefreshHistoryData();
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (element == 5) // Toggle Quiet Mode
                        {
                            bool current = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
                            ClickraStorage.SaveSetting("QuietMode", current ? "false" : "true");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 6) // Toggle Notification
                        {
                            bool current = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
                            ClickraStorage.SaveSetting("Notification", current ? "false" : "true");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 7) // OutputDir: source
                        {
                            ClickraStorage.SaveSetting("OutputDir", "source");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 8) // OutputDir: desktop
                        {
                            ClickraStorage.SaveSetting("OutputDir", "desktop");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 9) // OutputDir: downloads
                        {
                            ClickraStorage.SaveSetting("OutputDir", "downloads");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 20) // OutputDir: custom
                        {
                            string title = GetText("setting_output_browse_title");
                            string folder = BrowseForFolder(hwnd, title);
                            if (!string.IsNullOrEmpty(folder))
                            {
                                ClickraStorage.SaveSetting("OutputDir", folder);
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (element == 10) // Language dropdown button
                        {
                            _langDropdownOpen = !_langDropdownOpen;
                            if (_langDropdownOpen)
                            {
                                _langSearchQuery = "";
                                _langHoveredIndex = 0;
                                _langScrollOffset = 0;
                            }
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 31) // Target language dropdown button
                        {
                            _pdfLangDropdownOpen = !_pdfLangDropdownOpen;
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element >= 50 && element <= 57) // Change convert tool
                        {
                            ChangeConvertCommand(element - 50);
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 18) // Drag & Drop Zone clicked (Browse files)
                        {
                            string title = GetText("convert_drag_drop_hint");
                            const string allFilter = "Supported Files (*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp)\0*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0";
                            var chosen = OpenFiles(hwnd, allFilter, title);
                            if (chosen.Count > 0)
                            {
                                _selectedFiles = chosen;
                                // Auto-select first enabled command for the chosen files
                                _convertCommandIndex = -1;
                                for (int i = 0; i < 8; i++)
                                {
                                    if (ValidateConvertFiles(ConvertCommands[i], _selectedFiles, out _))
                                    {
                                        _convertCommandIndex = i;
                                        break;
                                    }
                                }
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (element == 19) // Start conversion button
                        {
                            RunConversion(hwnd);
                        }
                        else if (element == 25) // Clear files button
                        {
                            _selectedFiles.Clear();
                            _convertCommandIndex = -1;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }

                        else if (element == 23) // Open GitHub project URL
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "https://github.com/Youchenjiang/Clickra",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                MessageBox(hwnd, $"Cannot open browser: {ex.Message}", "Clickra", 0x10);
                            }
                        }
                        else if (element == 24) // One-click Gmail Diagnostics & highlight history.log in Explorer
                        {
                            try
                            {
                                string dataDir = ClickraStorage.GetDataDir();
                                string logPath = Path.Combine(dataDir, "history.log");

                                // Open Explorer and highlight history.log
                                if (File.Exists(logPath))
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logPath}\"");
                                }
                                else
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = dataDir,
                                        UseShellExecute = true
                                    });
                                }

                                // Open Gmail composer link
                                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
                                string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                string subject = Uri.EscapeDataString("Clickra Diagnostics Report");
                                string body = Uri.EscapeDataString(
                                    "感謝您提交 Clickra 診斷回報！\r\n\r\n" +
                                    "請直接將已為您選取好的「history.log」拖曳到此郵件中作為附件。\r\n\r\n" +
                                    $"[系統資訊]\r\n" +
                                    $"作業系統: Windows\r\n" +
                                    $"Clickra 版本: {verStr}\r\n" +
                                    $"時間: {timeStr}\r\n\r\n" +
                                    "[問題描述]\r\n" +
                                    "（請在此處填寫您遇到的問題...）"
                                );
                                string gmailUrl = $"https://mail.google.com/mail/?view=cm&fs=1&to=jiangyouchen%40gmail.com&su={subject}&body={body}";
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = gmailUrl,
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                MessageBox(hwnd, $"Cannot start feedback: {ex.Message}", "Clickra", 0x10);
                            }
                        }
                    }
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
                    if (_isDraggingScrollX || _isDraggingScrollY || _isDraggingDetailScroll)
                    {
                        _isDraggingScrollX = false;
                        _isDraggingScrollY = false;
                        _isDraggingDetailScroll = false;
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
                            float logW = LogicalWidth(hwnd);
                            float logH = LogicalHeight(hwnd);
                            float contentH = GetContentHeight(hwnd);
                            bool showV = logH < contentH;
                            bool showH = logW < 760;
                             if ((showV && mouseX >= logW - 8 && mouseX < logW) ||
                                 (showH && mouseY >= logH - 8 && mouseY < logH && mouseX >= GetSidebarWidth(logW)))
                            {
                                SetCursor(LoadCursorW(IntPtr.Zero, 32649)); // IDC_HAND = 32649
                                return (IntPtr)1;
                            }
                        }
                        if (_hoveredElement != -1 || _langDropdownOpen || IsHoveringHistoryRow(hwnd) || _isDraggingScrollX || _isDraggingScrollY || _isDraggingDetailScroll)
                        {
                            SetCursor(LoadCursorW(IntPtr.Zero, 32649)); // IDC_HAND = 32649
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

                                float logW = LogicalWidth(hwnd);
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

                                        float w1, w2, w3, w4;
                                        using (var tempBmp = new Bitmap(1, 1))
                                        using (var tempG = Graphics.FromImage(tempBmp))
                                        {
                                            w1 = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont!).Width / _dpiScale;
                                            w2 = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont!).Width / _dpiScale;
                                            w3 = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont!).Width / _dpiScale;
                                            w4 = tempG.MeasureString(GetText(_historyEntries[i].IsSuccess ? "history_detail_elapsed" : "history_detail_error") + ":", _subFont!).Width / _dpiScale;
                                        }
                                        float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
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
                                float logH = LogicalHeight(hwnd);
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
