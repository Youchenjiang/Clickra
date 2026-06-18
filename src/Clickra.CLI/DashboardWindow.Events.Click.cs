using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Clickra.Core;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static void HandleLButtonDown(IntPtr hwnd, IntPtr w, IntPtr l)
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
                    return;
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
                        return;
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
                    return;
                }
                _pdfLangDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }

            if (_langDropdownOpen)
            {
                int popupY = _langDropdownY - 180;
                if (adjMouseX >= GetContentX(logW) && adjMouseX <= GetContentX(logW) + 240)
                {
                    if (adjMouseY >= popupY && adjMouseY < popupY + 38)
                    {
                        return;
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
                        return;
                    }
                }

                _langDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
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
                            return;
                        }
                        else
                        {
                            if (_expandedHistoryIndex == clickedIndex)
                            {
                                _expandedHistoryIndex = -1;
                            }
                            else
                            {
                                _expandedHistoryIndex = clickedIndex;
                            }
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return;
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
            else if (element == 22)
            {
                if (MessageBox(hwnd, GetText("history_clear_confirm"), "Clickra", 0x24) == 6)
                {
                    ClickraStorage.ClearHistory();
                    _expandedHistoryIndex = -1;
                    RefreshHistoryData();
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
            else if (element == 5)
            {
                bool current = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
                ClickraStorage.SaveSetting("QuietMode", current ? "false" : "true");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 6)
            {
                bool current = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
                ClickraStorage.SaveSetting("Notification", current ? "false" : "true");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 7)
            {
                ClickraStorage.SaveSetting("OutputDir", "source");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 8)
            {
                ClickraStorage.SaveSetting("OutputDir", "desktop");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 9)
            {
                ClickraStorage.SaveSetting("OutputDir", "downloads");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 20)
            {
                string title = GetText("setting_output_browse_title");
                string folder = BrowseForFolder(hwnd, title);
                if (!string.IsNullOrEmpty(folder))
                {
                    ClickraStorage.SaveSetting("OutputDir", folder);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
            else if (element == 10)
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
            else if (element == 31)
            {
                _pdfLangDropdownOpen = !_pdfLangDropdownOpen;
                _langDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element >= 50 && element <= 57)
            {
                ChangeConvertCommand(element - 50);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 18)
            {
                string title = GetText("convert_drag_drop_hint");
                const string allFilter = "Supported Files (*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp)\0*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0";
                var chosen = OpenFiles(hwnd, allFilter, title);
                if (chosen.Count > 0)
                {
                    _selectedFiles = chosen;
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
            else if (element == 19)
            {
                RunConversion(hwnd);
            }
            else if (element == 25)
            {
                _selectedFiles.Clear();
                _convertCommandIndex = -1;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 23)
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
            else if (element == 24)
            {
                try
                {
                    string dataDir = ClickraStorage.GetDataDir();
                    string logPath = Path.Combine(dataDir, "history.log");

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
    }
}
