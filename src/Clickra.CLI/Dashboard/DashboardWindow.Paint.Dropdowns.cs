using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static void DrawLanguageDropdown(Graphics g, int y, float contentX)
        {
            float s = _dpiScale;
            string currentLangCode = ClickraStorage.GetSetting("Language");
            currentLangCode = Clickra.Core.Localization.NormalizeLanguageCode(currentLangCode);
            
            var currentLang = SupportedLanguages.FirstOrDefault(l => l.Code.Equals(currentLangCode, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(currentLang.Code))
            {
                currentLang = SupportedLanguages[0]; // Default to Traditional Chinese
            }

            string displayText = $"{currentLang.NativeName} ({currentLang.EnglishName})";
            bool isHovered = _hoveredElement == 10;

            int x = (int)contentX, w = 240, h = 30;

            UIHelper.DrawDropdownButton(g, x, y, w, h, displayText, _langDropdownOpen, isHovered, _subFont, _iconFont, s);

            // Draw overlay popup list if open
            if (_langDropdownOpen)
            {
                int popupH = 180;
                int popupY = y - popupH; // 210

                UIHelper.DrawDropdownPopup(g, x, popupY, w, popupH, s);

                // Search input box: y = 216
                int searchX = x + 6, searchY = popupY + 6, searchW = w - 12, searchH = 26;
                using (var searchPath = UIHelper.GetRoundedRectPath(new RectangleF(searchX * s, searchY * s, searchW * s, searchH * s), 4 * s))
                using (var searchBg = new SolidBrush(Color.FromArgb(45, 45, 45)))
                using (var searchBorder = new Pen(Color.FromArgb(75, 75, 75)))
                {
                    g.FillPath(searchBg, searchPath);
                    g.DrawPath(searchBorder, searchPath);
                }

                // Draw Search Icon
                if (_iconFont != null)
                {
                    using var searchIconBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString("\uE721", _iconFont, searchIconBrush, (searchX + 8) * s, (searchY + 7) * s);
                }

                // Draw Search Text or Placeholder
                if (_subFont != null)
                {
                    if (string.IsNullOrEmpty(_langSearchQuery))
                    {
                        using var placeholderBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                        g.DrawString(GetText("search_lang_placeholder"), _subFont, placeholderBrush, (searchX + 26) * s, (searchY + 6) * s);
                    }
                    else
                    {
                        using var queryBrush = new SolidBrush(Color.White);
                        g.DrawString(_langSearchQuery, _subFont, queryBrush, (searchX + 26) * s, (searchY + 6) * s);
                    }

                    // Draw flashing cursor (caret)
                    if ((DateTime.Now.Millisecond / 500) % 2 == 0)
                    {
                        var size = g.MeasureString(_langSearchQuery, _subFont);
                        using var cursorBrush = new SolidBrush(Color.White);
                        g.FillRectangle(cursorBrush, (searchX + 26) * s + size.Width, (searchY + 6) * s, 1.5f * s, 13 * s);
                    }
                }

                // Draw filtered list
                var filtered = GetFilteredLanguages();
                int listStartY = searchY + searchH + 6; // 248
                int maxVisible = 5;

                if (_langScrollOffset < 0) _langScrollOffset = 0;
                if (_langScrollOffset > 0 && _langScrollOffset > filtered.Count - maxVisible)
                {
                    _langScrollOffset = Math.Max(0, filtered.Count - maxVisible);
                }

                int drawCount = Math.Min(maxVisible, filtered.Count - _langScrollOffset);
                for (int i = 0; i < drawCount; i++)
                {
                    int itemIdx = _langScrollOffset + i;
                    var item = filtered[itemIdx];
                    int itemY = listStartY + i * 26;
                    int itemH = 24;

                    bool isItemHovered = _langHoveredIndex == itemIdx;
                    UIHelper.DrawDropdownItem(g, x, itemY, w, itemH, $"{item.NativeName} ({item.EnglishName})", isItemHovered, _subFont, s);
                }

                // Draw scrollbar for Language Dropdown
                if (filtered.Count > maxVisible)
                {
                    float trackX = x + w - 8;
                    float trackY = listStartY;
                    float trackW = 4;
                    float trackH = maxVisible * 26 - 2; // 128
                    using (var sbTrackBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    {
                        g.FillRectangle(sbTrackBrush, trackX * s, trackY * s, trackW * s, trackH * s);
                    }

                    float thumbH = Math.Max(15f, ((float)maxVisible / filtered.Count) * trackH);
                    float thumbY = trackY + ((float)_langScrollOffset / (filtered.Count - maxVisible)) * (trackH - thumbH);
                    using (var sbThumbBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
                    {
                        using (var thumbPath = UIHelper.GetRoundedRectPath(new RectangleF(trackX * s, thumbY * s, trackW * s, thumbH * s), 2 * s))
                        {
                            g.FillPath(sbThumbBrush, thumbPath);
                        }
                    }
                }
            }
        }

        static List<(string Code, string NativeName, string EnglishName)> GetFilteredLanguages()
        {
            if (string.IsNullOrEmpty(_langSearchQuery))
            {
                return SupportedLanguages;
            }
            return SupportedLanguages.Where(l =>
                l.NativeName.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                l.EnglishName.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                l.Code.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        static void SelectLanguage(string code)
        {
            ClickraStorage.SaveSetting("Language", code);
            RecreateScaledFonts();
        }

        static string GetText(string key)
        {
            return Clickra.Core.Localization.T(key, ClickraStorage.GetSetting("Language"));
        }



        static void DrawPdfLangDropdown(Graphics g, int y, float contentX)
        {
            float s = _dpiScale;

            string currentLang = ClickraStorage.GetSetting("TranslateTargetLang");
            if (string.IsNullOrEmpty(currentLang)) currentLang = "zh-TW";

            string displayText = currentLang;
            foreach (var l in PdfLangs)
            {
                if (l.Code.Equals(currentLang, StringComparison.OrdinalIgnoreCase))
                {
                    displayText = l.Name;
                    break;
                }
            }
            
            bool isHovered = _hoveredElement == 31;

            int x = (int)contentX, w = 240, h = 30;

            UIHelper.DrawDropdownButton(g, x, y, w, h, displayText, _pdfLangDropdownOpen, isHovered, _subFont, _iconFont, s);

            // Draw overlay popup list if open
            if (_pdfLangDropdownOpen)
            {
                int popupH = PdfLangs.Length * 26 + 8;
                int popupY = y - popupH;

                UIHelper.DrawDropdownPopup(g, x, popupY, w, popupH, s);

                int listStartY = popupY + 4;
                for (int i = 0; i < PdfLangs.Length; i++)
                {
                    var item = PdfLangs[i];
                    int itemY = listStartY + i * 26;
                    int itemH = 24;

                    bool isItemHovered = _pdfLangHoveredIndex == i;
                    UIHelper.DrawDropdownItem(g, x, itemY, w, itemH, item.Name, isItemHovered, _subFont, s);
                }
            }
        }


    }
}
