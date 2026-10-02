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

            int sidebarTarget = HitTestSidebar(x, y, sidebarW);
            if (sidebarTarget >= 0) return sidebarTarget;

            return _activeTab switch
            {
                1 => HitTestConvert(x, y, logW, contentX),
                2 => HitTestHistory(x, y, logW, contentX),
                3 => HitTestSettings(x, y, contentX),
                4 => HitTestAbout(x, y, contentX),
                _ => -1
            };
        }

        static int HitTestSidebar(int x, int y, float sidebarW)
        {
            if (x < 0 || x >= sidebarW) return -1;
            return DashboardLayout.SidebarTabAt(y);
        }

        static int HitTestConvert(int x, int y, float logW, float contentX)
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

            if (_selectedFiles.Count > 0 && DashboardLayout.ConvertClearButtonRect((int)logW).Contains(x, y)) return 25;
            if (zone.Contains(x, y)) return 18;
            if (_selectedFiles.Count > 0 && _convertCommandIndex != -1 &&
                DashboardLayout.ConvertStartButtonRect(zone.X, zone.Width, ConvertCommandGroupSizes.Max()).Contains(x, y)) return 19;
            return -1;
        }

        static int HitTestHistory(int x, int y, float logW, float contentX)
        {
            if (DashboardLayout.HistoryClearButtonRect((int)logW).Contains(x, y)) return 22;

            HistoryBlock parked = GetHistoryBlock(HistoryBlockKind.Parked);
            if (parked.IsEmpty) return -1;

            int rowW = (int)logW - (int)contentX - 40;
            var parkedItems = ParkedItems;
            for (int row = 0; row < parkedItems.Count; row++)
            {
                for (int action = 0; action < ParkedActionCount; action++)
                {
                    LayoutRect actionRect = DashboardLayout.ParkedRowActionRect(
                        (int)contentX, rowW, parked.RowTop(row), parked.RowHeight(row), action);
                    if (actionRect.Contains(x, y)) return ParkedActionElement(row, action);
                }
            }
            return -1;
        }

        static int HitTestSettings(int x, int y, float contentX)
        {
            foreach (var item in _settingsHitRects)
            {
                if (item.Value.Contains(x, y)) return item.Key;
            }

            if (DashboardLayout.DropdownButtonRect((int)contentX, _langDropdownY).Contains(x, y)) return 10;
            if (DashboardLayout.DropdownButtonRect((int)contentX, _pdfLangDropdownY).Contains(x, y)) return 31;
            return -1;
        }

        static int HitTestAbout(int x, int y, float contentX)
        {
            float wGit = _wGit;
            float wGmail = _wGmail;

            if (x >= contentX && x < contentX + wGit && y >= _githubBtnY && y < _githubBtnY + 32) return 23;
            if (x >= contentX && x < contentX + wGmail && y >= _aboutBtnY && y < _aboutBtnY + 32) return 24;
            return -1;
        }
    }
}
