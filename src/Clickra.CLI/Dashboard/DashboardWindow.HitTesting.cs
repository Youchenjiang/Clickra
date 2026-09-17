using System;
using System.Drawing;
using System.Linq;
using Clickra.Core;
using Clickra.Core.Layout;
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
                    // 列的位置由堆疊提供，hover 不會因為上方多一列而落在別列。
                    return GetHistoryBlock(HistoryBlockKind.History).RowAt(adjMouseY) >= 0;
                }
            }
            return false;
        }

        static int HitTest(IntPtr hwnd, int x, int y)
        {
            float rawLogW = GetLogicalWidth(hwnd);
            float logW = Math.Max(760f, rawLogW);

            float sidebarW = GetSidebarWidth(logW);
            float contentX = GetContentX(logW);

            // Sidebar tabs (always active)：列的 Y 與高度來自版面表，與繪製共用同一個算式。
            if (x >= 0 && x < sidebarW)
            {
                int tab = DashboardLayout.SidebarTabAt(y);
                if (tab >= 0) return tab;
            }

            if (_activeTab == 1) // Convert
            {
                // 畫什麼、點什麼都由版面表的同一個矩形決定。
                LayoutRect zone = DashboardLayout.ConvertZoneRect((int)contentX, (int)logW);

                int commandIndex = 0;
                for (int group = 0; group < ConvertCommandGroupSizes.Length; group++)
                {
                    for (int local = 0; local < ConvertCommandGroupSizes[group]; local++)
                    {
                        if (DashboardLayout.ConvertCardRect(group, local, zone.X, zone.Width).Contains(x, y)
                            && ConvertCommands[commandIndex].ValidateFiles(_selectedFiles, out _))
                        {
                            return 50 + commandIndex;
                        }
                        commandIndex++;
                    }
                }

                if (_selectedFiles.Count > 0 && DashboardLayout.ConvertClearButtonRect((int)logW).Contains(x, y)) return 25; // Clear button
                if (zone.Contains(x, y)) return 18; // Drag & Drop zone
                if (_selectedFiles.Count > 0 && _convertCommandIndex != -1 &&
                    DashboardLayout.ConvertStartButtonRect(zone.X, zone.Width, ConvertCommandGroupSizes.Max()).Contains(x, y)) return 19; // Start button
            }
            else if (_activeTab == 2) // History
            {
                // Clear history button
                if (DashboardLayout.HistoryClearButtonRect((int)logW).Contains(x, y)) return 22;

                // 待繼續任務列的期限微調鈕：矩形來自版面表，與繪製端逐像素相同。
                HistoryBlock parked = GetHistoryBlock(HistoryBlockKind.Parked);
                if (!parked.IsEmpty)
                {
                    int rowW = (int)logW - (int)contentX - 40;
                    var parkedItems = ParkedItems;
                    for (int row = 0; row < parkedItems.Count; row++)
                    {
                        for (int action = 0; action < ParkedActionCount; action++)
                        {
                            if (DashboardLayout.ParkedRowActionRect((int)contentX, rowW, parked.RowTop(row), parked.RowHeight(row), action).Contains(x, y))
                            {
                                return ParkedActionElement(row, action);
                            }
                        }
                    }
                }
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

                // Language dropdown button（與設定頁記錄的矩形共用同一份寬高）
                if (DashboardLayout.DropdownButtonRect((int)contentX, _langDropdownY).Contains(x, y)) return 10;

                // PDF Translation dropdown buttons
                if (DashboardLayout.DropdownButtonRect((int)contentX, _pdfLangDropdownY).Contains(x, y)) return 31;
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
