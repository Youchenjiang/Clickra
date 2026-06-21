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

            DrawEngineRow(g, GetText("engine_pdf"), true, (int)contentX, 125);
            DrawEngineRow(g, GetText("engine_ppt"), IsOfficeInstalled("PowerPoint"), (int)contentX, 165);
            DrawEngineRow(g, GetText("engine_word"), IsOfficeInstalled("Word"), (int)contentX, 205);
            DrawEngineRow(g, GetText("engine_excel"), IsOfficeInstalled("Excel"), (int)contentX, 245);

            // Statistics
            if (_sectionFont != null)
                g.DrawString(GetText("overview_stats"), _sectionFont, Brushes.White, contentX * s, 280 * s);

            // Draw Cards
            DrawStatCard(g, GetText("overview_stat_total"), _statTotal.ToString(), Color.FromArgb(200, 200, 200), (int)contentX, 310, 140);
            DrawStatCard(g, GetText("overview_stat_success"), _statSuccess.ToString(), Color.FromArgb(100, 220, 100), (int)contentX + 160, 310, 140);
            DrawStatCard(g, GetText("overview_stat_failed"), _statFailed.ToString(), Color.FromArgb(255, 90, 70), (int)contentX + 320, 310, 140);
            
            if (_subFont != null)
            {
                using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                var rect = new RectangleF(contentX * s, 400 * s, (logW - contentX - 40) * s, 45 * s);
                g.DrawString(GetText("overview_tip"), _subFont, tipBrush, rect);
            }
        }
    }
}
