using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static void DrawOverviewTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_status"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // Engine status
            if (_sectionFont != null)
                g.DrawString(GetText("overview_engine_status"), _sectionFont, Brushes.White, contentX * s, 95 * s);

            bool microsoftReady = IsOfficeInstalled("Word") && IsOfficeInstalled("Excel") && IsOfficeInstalled("PowerPoint");
            bool libreOfficeReady = !string.IsNullOrEmpty(LibreOfficeHelper.GetResolvedExecutablePath());
            string engineMode = ClickraStorage.GetSetting("OfficeEngine");
            bool isLibreOfficeMode = engineMode.Equals("libreoffice", StringComparison.OrdinalIgnoreCase);
            bool isMicrosoftMode = engineMode.Equals("microsoft", StringComparison.OrdinalIgnoreCase);
            bool officeReady = isLibreOfficeMode
                ? libreOfficeReady
                : isMicrosoftMode
                    ? microsoftReady
                    : microsoftReady || libreOfficeReady;
            string activeOfficeEngine = isLibreOfficeMode
                ? GetText("setting_engine_libreoffice")
                : isMicrosoftMode || microsoftReady
                    ? GetText("setting_engine_microsoft")
                    : libreOfficeReady
                        ? GetText("setting_engine_libreoffice")
                        : GetText("setting_engine_auto");

            DrawOverviewStatusLine(g, "PDF", GetText("engine_ready"), true, (int)contentX, 128, 420);
            DrawOverviewStatusLine(
                g,
                GetText("overview_office_conversion"),
                string.Format(GetText(officeReady ? "overview_engine_active" : "overview_engine_unavailable"), activeOfficeEngine),
                officeReady,
                (int)contentX,
                170,
                420);

            // Statistics
            if (_sectionFont != null)
                g.DrawString(GetText("overview_stats"), _sectionFont, Brushes.White, contentX * s, 240 * s);

            // Draw Cards
            DrawStatCard(g, GetText("overview_stat_total"), _statTotal.ToString(), Color.FromArgb(200, 200, 200), (int)contentX, 270, 140);
            DrawStatCard(g, GetText("overview_stat_success"), _statSuccess.ToString(), Color.FromArgb(100, 220, 100), (int)contentX + 160, 270, 140);
            DrawStatCard(g, GetText("overview_stat_failed"), _statFailed.ToString(), Color.FromArgb(255, 90, 70), (int)contentX + 320, 270, 140);
            
            if (_subFont != null)
            {
                using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                float tipY = 360f;
                float tipH = 45f;
                var rect = new RectangleF(contentX * s, tipY * s, (logW - contentX - 40) * s, tipH * s);
                g.DrawString(GetText("overview_tip"), _subFont, tipBrush, rect);
                _overviewContentHeight = tipY + tipH + 16f;
            }
            else
            {
                _overviewContentHeight = 405f;
            }
        }

        static void DrawOverviewStatusLine(Graphics g, string label, string status, bool ok, int x, int y, int w)
        {
            float s = _dpiScale;
            int h = 30;
            Color bg = Color.FromArgb(32, 32, 32);
            Color border = Color.FromArgb(52, 52, 52);
            Color dotColor = ok ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
            using var path = UIHelper.GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 5 * s);
            using var bgBrush = new SolidBrush(bg);
            using var borderPen = new Pen(border);
            g.FillPath(bgBrush, path);
            g.DrawPath(borderPen, path);

            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, (x + 12) * s, (y + 10) * s, 10 * s, 10 * s);

            if (_subFont != null)
            {
                using var labelBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                using var statusBrush = new SolidBrush(ok ? Color.FromArgb(170, 220, 170) : Color.FromArgb(255, 120, 100));
                g.DrawString(label, _subFont, labelBrush, (x + 32) * s, (y + 6) * s);

                float labelWidth = g.MeasureString(label, _subFont).Width / s;
                g.DrawString(status, _subFont, statusBrush, (x + 44 + labelWidth) * s, (y + 6) * s);
            }
        }
    }
}
