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
        static void Paint(IntPtr hwnd, IntPtr hdc)
        {
            if (_bufferBmp == null || _bufferGraphics == null) return;
            var g = _bufferGraphics;
            g.Clear(Color.FromArgb(32, 32, 32));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float logW = LogicalWidth(hwnd);
            float logH = LogicalHeight(hwnd);
            float s = _dpiScale;

            float sidebarW = GetSidebarWidth(logW);
            float contentX = GetContentX(logW);

            // 1. Draw Sidebar
            using (var sidebarBrush = new SolidBrush(Color.FromArgb(24, 24, 24)))
            {
                g.FillRectangle(sidebarBrush, 0, 0, sidebarW * s, logH * s);
            }
            using (var dividerPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(dividerPen, sidebarW * s, 0, sidebarW * s, logH * s);
            }

            // Draw Brand Title (Centered in sidebar)
            if (_titleFont != null)
            {
                var size = g.MeasureString("Clickra", _titleFont);
                float titleW = size.Width / s;
                float titleX = (sidebarW - titleW) / 2;
                g.DrawString("Clickra", _titleFont, Brushes.White, titleX * s, 30 * s);
            }

            // Brand Divider (Aligned with right side content divider)
            using (var brandDivPen = new Pen(Color.FromArgb(45, 45, 45)))
            {
                g.DrawLine(brandDivPen, 20 * s, 75 * s, (sidebarW - 20) * s, 75 * s);
            }

            // Draw Version Number in bottom-left of sidebar
            if (_subFont != null)
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
                using var verBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                g.DrawString($"v{verStr}", _subFont, verBrush, 24 * s, (logH - 24) * s);
            }

            // Draw Tabs
            DrawTabButton(g, "\uE80F", GetText("tab_status"), 0, 120, sidebarW);
            DrawTabButton(g, "\uEC7E", GetText("tab_convert"), 1, 168, sidebarW);
            DrawTabButton(g, "\uE81C", GetText("tab_history"), 2, 216, sidebarW);
            DrawTabButton(g, "\uE713", GetText("tab_settings"), 3, 264, sidebarW);
            DrawTabButton(g, "\uE897", GetText("tab_about"), 4, 312, sidebarW);

            // 2. Draw Content Area (with Clip & Translation)
            var state = g.Save();
            float clipW = Math.Max(0f, logW - sidebarW) * s;
            g.SetClip(new RectangleF(sidebarW * s, 0, clipW, logH * s));
            g.TranslateTransform(-_contentScrollX * s, -_contentScrollY * s);

            float virtLogW = Math.Max(760f, logW);
            float virtLogH = Math.Max(460f, logH);

            if (_activeTab == 0)
            {
                DrawOverviewTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 1)
            {
                DrawConvertTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 2)
            {
                DrawHistoryTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 3)
            {
                DrawSettingsTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 4)
            {
                DrawAboutTab(g, virtLogW, virtLogH, contentX);
            }

            g.Restore(state);

            // 3. Draw Viewport Scrollbars (fixed on screen, not translated)
            float contentH = GetContentHeight(hwnd);
            bool showV = logH < contentH;
            bool showH = logW < 760;

            if (showV)
            {
                float trackY = 4;
                float trackH = logH - 8;
                if (showH) trackH = logH - 16;
                float thumbH = Math.Max(20f, (logH / contentH) * trackH);
                float thumbY = trackY + (_contentScrollY / (contentH - logH)) * (trackH - thumbH);
                float trackX = logW - 8;
                float trackW = 5;

                using (var sbTrackBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                {
                    g.FillRectangle(sbTrackBrush, trackX * s, trackY * s, trackW * s, trackH * s);
                }
                using (var sbThumbBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
                using (var thumbPath = UIHelper.GetRoundedRectPath(new RectangleF(trackX * s, thumbY * s, trackW * s, thumbH * s), 2.5f * s))
                {
                    g.FillPath(sbThumbBrush, thumbPath);
                }
            }

            if (showH)
            {
                float trackX = sidebarW + 4;
                float trackW_sb = (logW - sidebarW) - 8;
                if (showV) trackW_sb = (logW - sidebarW) - 16;
                if (trackW_sb > 0)
                {
                    float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW_sb);
                    float thumbX = trackX + (_contentScrollX / (760f - logW)) * (trackW_sb - thumbW);
                    float trackY = logH - 8;
                    float trackH = 5;

                    using (var sbTrackBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                    {
                        g.FillRectangle(sbTrackBrush, trackX * s, trackY * s, trackW_sb * s, trackH * s);
                    }
                    using (var sbThumbBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
                    using (var thumbPath = UIHelper.GetRoundedRectPath(new RectangleF(thumbX * s, trackY * s, thumbW * s, trackH * s), 2.5f * s))
                    {
                        g.FillPath(sbThumbBrush, thumbPath);
                    }
                }
            }

            // Draw double buffer to screen
            using var targetG = Graphics.FromHdc(hdc);
            if (_bufferBmp != null)
            {
                targetG.DrawImage(_bufferBmp, 0, 0, _bufferBmp.Width, _bufferBmp.Height);
            }
        }

        static void DrawTabButton(Graphics g, string icon, string label, int tabIndex, int y, float sidebarW)
        {
            float s = _dpiScale;
            bool isActive = _activeTab == tabIndex;
            bool isHovered = _hoveredElement == tabIndex;

            float scaledY = y * s;
            float scaledH = 40 * s;
            float width = sidebarW - 4;

            if (isActive)
            {
                // Accent left border
                using var accentBrush = new SolidBrush(UIHelper.GetSystemColorizationColor());
                g.FillRectangle(accentBrush, 0, scaledY + 4 * s, 4 * s, 32 * s);

                // Subtle background for active tab
                using var activeBg = new SolidBrush(Color.FromArgb(36, 36, 36));
                g.FillRectangle(activeBg, 4 * s, scaledY, width * s, scaledH);
            }
            else if (isHovered)
            {
                // Hover background
                using var hoverBg = new SolidBrush(Color.FromArgb(30, 30, 30));
                g.FillRectangle(hoverBg, 4 * s, scaledY, width * s, scaledH);
            }

            Color textColor = isActive ? Color.White : (isHovered ? Color.FromArgb(220, 220, 220) : Color.FromArgb(150, 150, 150));
            using var textBrush = new SolidBrush(textColor);

            // Draw Icon (Segoe MDL2 Assets)
            if (_iconFont != null)
            {
                g.DrawString(icon, _iconFont, textBrush, 24 * s, scaledY + 12 * s);
            }

            // Draw Label (Segoe UI)
            if (_tabFont != null)
            {
                g.DrawString(label, _tabFont, textBrush, 52 * s, scaledY + 10 * s);
            }
        }
    }
}
