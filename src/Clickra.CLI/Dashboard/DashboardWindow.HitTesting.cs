using System;
using System.Drawing;
using System.Linq;
using Clickra.Core;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static bool IsHoveringHistoryRow(IntPtr hwnd)
        {
            if (_activeTab != 2) return false;
            var pt = new Point();
            if (GetCursorPos(out pt))
            {
                ScreenToClient(hwnd, ref pt);
                int mouseX = (int)(pt.X / _dpiScale);
                int mouseY = (int)(pt.Y / _dpiScale);
                float logW = GetLogicalWidth(hwnd);
                float sidebarW = GetSidebarWidth(logW);
                float contentX = GetContentX(logW);
                int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
                int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;
                float virtLogW = Math.Max(760f, logW);
                if (adjMouseX >= contentX && adjMouseX < virtLogW - 40)
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
                    for (int i = 0; i < _historyEntries.Count; i++)
                    {
                        bool isExpanded = (i == _expandedHistoryIndex);
                        int rowH = isExpanded ? 160 : 44;
                        if (adjMouseY >= currentY && adjMouseY < currentY + rowH)
                        {
                            return true;
                        }
                        currentY += rowH + 8;
                    }
                }
            }
            return false;
        }

        private static readonly (int Start, int End, int TabIndex)[] SidebarTabRanges = new[]
        {
            (120, 160, 0),
            (168, 208, 1),
            (216, 256, 2),
            (264, 304, 3),
            (312, 352, 4)
        };

        private static int? HitSidebarTab(int x, int y, float sidebarW)
        {
            if (x >= 0 && x < sidebarW)
            {
                foreach (var range in SidebarTabRanges)
                {
                    if (y >= range.Start && y < range.End)
                        return range.TabIndex;
                }
            }
            return null;
        }

        static int HitTest(IntPtr hwnd, int x, int y)
        {
            float rawLogW = GetLogicalWidth(hwnd);
            float rawLogH = GetLogicalHeight(hwnd);
            float logW = Math.Max(760f, rawLogW);
            float logH = Math.Max(460f, rawLogH);

            float sidebarW = GetSidebarWidth(logW);
            float contentX = GetContentX(logW);

            // Sidebar tabs (always active)
            var sidebarTab = HitSidebarTab(x, y, sidebarW);
            if (sidebarTab.HasValue)
            {
                return sidebarTab.Value;
            }

            if (_activeTab == 1) // Convert
            {
                int zoneW = (int)logW - (int)contentX - 50;
                int zoneH = 120;
                int clearX = (int)logW - 110;

                int groupGap = 14;
                int groupW = (zoneW - 2 * groupGap) / 3;
                int groupTop = 230;
                int headerH = 24;
                int cardH = 38;
                int cardGap = 8;
                int buttonY = groupTop + headerH + ConvertCommandGroupSizes.Max() * (cardH + cardGap) + 16;

                int commandIndex = 0;
                for (int group = 0; group < ConvertCommandGroupSizes.Length; group++)
                {
                    for (int local = 0; local < ConvertCommandGroupSizes[group]; local++)
                    {
                        int cardX = (int)contentX + group * (groupW + groupGap);
                        int cardY = groupTop + headerH + local * (cardH + cardGap);
                        if (x >= cardX && x < cardX + groupW && y >= cardY && y < cardY + cardH
                            && ValidateConvertFiles(ConvertCommands[commandIndex], _selectedFiles, out _))
                        {
                            return 50 + commandIndex;
                        }
                        commandIndex++;
                    }
                }

                if (_selectedFiles.Count > 0 && x >= clearX && x < clearX + 48 && y >= 107 && y < 107 + 22) return 25; // Clear button
                if (x >= contentX && x < contentX + zoneW && y >= 95 && y < 95 + zoneH) return 18; // Drag & Drop zone
                if (_selectedFiles.Count > 0 && _convertCommandIndex != -1 && x >= contentX && x < contentX + zoneW && y >= buttonY && y < buttonY + 36) return 19; // Start button
            }
            else if (_activeTab == 2) // History
            {
                // Clear history button
                int clearX = (int)logW - 130;
                if (x >= clearX && x < clearX + 90 && y >= 38 && y < 66) return 22;
            }
            else if (_activeTab == 3) // Settings
            {
                foreach (var item in _settingsHitRects)
                {
                    if (item.Value.Contains(x, y))
                    {
                        return item.Key;
                    }
                }

                // Language dropdown button
                if (x >= contentX && x < contentX + 240 && y >= _langDropdownY && y < _langDropdownY + 30) return 10;

                // PDF Translation dropdown buttons
                if (x >= contentX && x < contentX + 240 && y >= _pdfLangDropdownY && y < _pdfLangDropdownY + 30) return 31;
            }
            else if (_activeTab == 4) // About
            {
                float wGit = _wGit;
                float wGmail = _wGmail;

                // GitHub Button: x from contentX to contentX + wGit
                if (x >= contentX && x < contentX + wGit && y >= _githubBtnY && y < _githubBtnY + 32) return 23;

                // Gmail button: x from contentX to contentX + wGmail
                if (x >= contentX && x < contentX + wGmail && y >= _aboutBtnY && y < _aboutBtnY + 32) return 24;
            }

            return -1;
        }
    }
}
