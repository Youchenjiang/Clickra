using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Clickra.Core;
using Clickra.Core.Layout;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static int GetClientWidth(IntPtr hwnd)
        {
            if (GetClientRect(hwnd, out var rect))
                return rect.right - rect.left;
            return 760;
        }

        static int GetClientHeight(IntPtr hwnd)
        {
            if (GetClientRect(hwnd, out var rect))
                return rect.bottom - rect.top;
            return DashboardLayout.MinContentHeight;
        }

        static float GetLogicalWidth(IntPtr hwnd) => GetClientWidth(hwnd) / _dpiScale;
        static float GetLogicalHeight(IntPtr hwnd) => GetClientHeight(hwnd) / _dpiScale;

        public static float GetSidebarWidth(float logW)
        {
            return _sidebarWidth;
        }

        public static float GetContentX(float logW)
        {
            return _sidebarWidth + 30f;
        }

        private static float GetMaxValW(float logW)
        {
            float contentX = GetContentX(logW);
            float virtLogW = Math.Max(760f, logW);
            float rowW = virtLogW - contentX - 40;

            float inputLabelW, outputLabelW, timeLabelW, errorLabelW;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                inputLabelW = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont!).Width / _dpiScale;
                outputLabelW = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont!).Width / _dpiScale;
                timeLabelW = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont!).Width / _dpiScale;
                errorLabelW = tempG.MeasureString(GetText("history_detail_error") + ":", _subFont!).Width / _dpiScale;
            }
            float maxLabelW = Math.Max(inputLabelW, Math.Max(outputLabelW, Math.Max(timeLabelW, errorLabelW)));
            float valX = contentX + 12 + maxLabelW + 16;
            return contentX + rowW - 12 - valX;
        }

        static float GetContentHeight(IntPtr hwnd)
        {
            return _activeTab switch
            {
                0 => _overviewContentHeight,
                1 => DashboardLayout.ConvertContentHeight(ConvertCommandGroupSizes),
                2 => HistoryLayout.ContentHeight(GetHistoryStack()),
                3 => Math.Max(DashboardLayout.MinContentHeight, _settingsContentHeight),
                4 => Math.Max(DashboardLayout.MinContentHeight, _aboutBtnY + 60),
                _ => DashboardLayout.MinContentHeight
            };
        }

        /// <summary>
        /// History 頁的縱向堆疊：進行中佇列 → 待繼續任務 → 持久化歷史紀錄。
        /// 列高、行距與區塊高度都在 Core 的版面表（<see cref="DashboardLayout"/>、
        /// <see cref="HistoryLayout"/>）裡，繪製、命中測試與這裡的捲動高度共用同一份堆疊，
        /// 所以在上方插入一列時三者會一起移動。
        ///
        /// 三個區塊的列數都取自同一個清單（<see cref="HistoryFeed"/>，與 Fluent 的 History 頁共用），
        /// 所以不會出現「這裡畫 2 列、命中判定以為有 3 列」這種漂移。
        /// </summary>
        static IReadOnlyList<HistoryBlock> GetHistoryStack() =>
            HistoryLayout.Build(_historyFeed.ActiveCount, _historyFeed.ParkedCount, _historyFeed.CompletedCount, _expandedHistoryIndex);


        /// <summary>堆疊中指定種類的區塊。</summary>
        static HistoryBlock GetHistoryBlock(HistoryBlockKind kind) =>
            HistoryLayout.Find(GetHistoryStack(), kind);

        static void RecreateBuffer(int w, int h)
        {
            try { _bufferGraphics?.Dispose(); _bufferGraphics = null; } catch {}
            try { _bufferBmp?.Dispose(); _bufferBmp = null; } catch {}
            if (w <= 0 || h <= 0) return;
            _bufferBmp = new Bitmap(w, h);
            _bufferGraphics = Graphics.FromImage(_bufferBmp);
            _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        }

        static void RecreateScaledFonts()
        {
            try { _titleFont?.Dispose(); } catch {}
            try { _subFont?.Dispose(); } catch {}
            try { _tabFont?.Dispose(); } catch {}
            try { _contentTitleFont?.Dispose(); } catch {}
            try { _sectionFont?.Dispose(); } catch {}
            try { _bodyFont?.Dispose(); } catch {}
            try { _tagFont?.Dispose(); } catch {}
            try { _iconFont?.Dispose(); } catch {}

            string lang = ClickraStorage.GetSetting(ClickraSettings.Language);
            string fontName = LocalizedUiFontSelector.GetTextFontName(lang);
            float s = _dpiScale;

            _titleFont = new Font(fontName, 24f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            _subFont = new Font(fontName, 13f * s, GraphicsUnit.Pixel);
            _tabFont = new Font(fontName, 14f * s, GraphicsUnit.Pixel);
            _contentTitleFont = new Font(fontName, 22f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            _sectionFont = new Font(fontName, 15f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            _bodyFont = new Font(fontName, 13.5f * s, GraphicsUnit.Pixel);
            _tagFont = new Font(fontName, 12f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            _iconFont = new Font("Segoe MDL2 Assets", 14f * s, GraphicsUnit.Pixel);

            // Measure the tab button text widths to determine sidebar width dynamically
            float maxLabelW = 0;
            using (var tempBmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tempBmp))
            {
                string[] keys = { "tab_status", "tab_convert", "tab_history", "tab_settings", "tab_about" };
                foreach (var key in keys)
                {
                    string text = Clickra.Core.Localization.T(key, lang);
                    var size = g.MeasureString(text, _tabFont);
                    if (size.Width > maxLabelW)
                    {
                        maxLabelW = size.Width;
                    }
                }
            }
            // Sidebar width: 24 (left margin) + 16 (icon) + 12 (icon to text margin) = 52. Plus padding of 24.
            _sidebarWidth = (52f * _dpiScale + maxLabelW + 24f * _dpiScale) / _dpiScale;
            _sidebarWidth = Math.Max(130f, _sidebarWidth); // Ensure it's at least 130px

            // Cache button widths to avoid GC pressure in HitTest
            using var widthBmp = new Bitmap(1, 1);
            using var tempG = Graphics.FromImage(widthBmp);
            if (_subFont != null)
            {
                string textSource = GetText("setting_output_same_as_source");
                string textDesktop = GetText("setting_output_desktop");
                string textDownloads = GetText("setting_output_downloads");
                string textCustom = GetText("setting_output_custom");

                _wSource = Math.Max(110f, tempG.MeasureString(textSource, _subFont).Width / _dpiScale + 20f);
                _wDesktop = Math.Max(65f, tempG.MeasureString(textDesktop, _subFont).Width / _dpiScale + 20f);
                _wDownloads = Math.Max(80f, tempG.MeasureString(textDownloads, _subFont).Width / _dpiScale + 20f);
                _wCustom = Math.Max(100f, tempG.MeasureString(textCustom, _subFont).Width / _dpiScale + 20f);
                _wEngineAuto = Math.Max(80f, tempG.MeasureString(GetText("setting_engine_auto"), _subFont).Width / _dpiScale + 20f);
                _wEngineMicrosoft = Math.Max(125f, tempG.MeasureString(GetText("setting_engine_microsoft"), _subFont).Width / _dpiScale + 20f);
                _wEngineLibreOffice = Math.Max(110f, tempG.MeasureString(GetText("setting_engine_libreoffice"), _subFont).Width / _dpiScale + 20f);
                _wLibreOfficeBrowse = Math.Max(120f, tempG.MeasureString(GetText("setting_libreoffice_browse"), _subFont).Width / _dpiScale + 20f);
                _wLibreOfficeDownload = Math.Max(
                    125f,
                    Math.Max(
                        Math.Max(
                            tempG.MeasureString(GetText("setting_libreoffice_download"), _subFont).Width,
                            tempG.MeasureString(GetText("setting_libreoffice_update"), _subFont).Width),
                        tempG.MeasureString(GetText("setting_libreoffice_reinstall"), _subFont).Width) / _dpiScale + 20f);
                _wLibreOfficeUninstall = Math.Max(125f, tempG.MeasureString(GetText("setting_libreoffice_uninstall"), _subFont).Width / _dpiScale + 20f);
                _wLibreOfficeAdopt = Math.Max(135f, tempG.MeasureString(GetText("setting_libreoffice_adopt"), _subFont).Width / _dpiScale + 20f);

                string textGit = GetText("about_btn_github");
                string textGmail = GetText("about_btn_gmail");
                if (_iconFont != null)
                {
                    float iconW_git = tempG.MeasureString("\uE71B", _iconFont).Width / _dpiScale;
                    float textW_git = tempG.MeasureString(textGit, _subFont).Width / _dpiScale;
                    _wGit = Math.Max(160f, iconW_git + 6f + textW_git + 24f);

                    float iconW_gmail = tempG.MeasureString("\uE715", _iconFont).Width / _dpiScale;
                    float textW_gmail = tempG.MeasureString(textGmail, _subFont).Width / _dpiScale;
                    _wGmail = Math.Max(160f, iconW_gmail + 6f + textW_gmail + 24f);
                }
                else
                {
                    _wGit = Math.Max(160f, tempG.MeasureString(textGit, _subFont).Width / _dpiScale + 24f);
                    _wGmail = Math.Max(160f, tempG.MeasureString(textGmail, _subFont).Width / _dpiScale + 24f);
                }
            }
        }

        static string BrowseForFolder(IntPtr hwndOwner, string title)
        {
            var bi = new BROWSEINFO();
            bi.hwndOwner = hwndOwner;
            bi.lpszTitle = Marshal.StringToHGlobalUni(title);
            bi.ulFlags = 0x00000040 | 0x00000010; // BIF_NEWDIALOGSTYLE | BIF_EDITBOX
            try
            {
                IntPtr pidl = SHBrowseForFolder(ref bi);
                if (pidl != IntPtr.Zero)
                {
                    IntPtr pathBuffer = Marshal.AllocHGlobal(260 * 2);
                    string path = "";
                    if (SHGetPathFromIDList(pidl, pathBuffer))
                    {
                        path = Marshal.PtrToStringUni(pathBuffer) ?? "";
                    }
                    Marshal.FreeHGlobal(pathBuffer);
                    CoTaskMemFree(pidl);
                    return path;
                }
            }
            finally
            {
                if (bi.lpszTitle != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(bi.lpszTitle);
                }
            }
            return "";
        }

    }
}
