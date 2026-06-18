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
        static void DrawAboutTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_about"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // 1. Project Description
            float currentY = 95;
            if (_sectionFont != null)
            {
                g.DrawString(GetText("about_desc_title"), _sectionFont, Brushes.White, contentX * s, currentY * s);
            }

            currentY += 25;
            if (_bodyFont != null)
            {
                using var descBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                string descText = GetText("about_desc_body");
                float maxTextW = logW - contentX - 40;
                var size = g.MeasureString(descText, _bodyFont, (int)(maxTextW * s));
                g.DrawString(descText, _bodyFont, descBrush, new RectangleF(contentX * s, currentY * s, maxTextW * s, size.Height));
                currentY += size.Height / s;
            }
            else
            {
                currentY += 40;
            }

            // 2. Collaboration Guide
            currentY += 20;
            if (_sectionFont != null)
            {
                g.DrawString(GetText("about_collab_title"), _sectionFont, Brushes.White, contentX * s, currentY * s);
            }

            currentY += 25;
            if (_bodyFont != null)
            {
                using var collabBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                string collabText = GetText("about_collab_body");
                float maxTextW = logW - contentX - 40;
                var size = g.MeasureString(collabText, _bodyFont, (int)(maxTextW * s));
                g.DrawString(collabText, _bodyFont, collabBrush, new RectangleF(contentX * s, currentY * s, maxTextW * s, size.Height));
                currentY += size.Height / s;
            }
            else
            {
                currentY += 40;
            }

            string textGit = GetText("about_btn_github");
            string textGmail = GetText("about_btn_gmail");

            float wGit = _wGit;
            float wGmail = _wGmail;

            // Button to open GitHub repository
            _githubBtnY = currentY + 12;
            bool isGitBtnHovered = _hoveredElement == 23;
            Color gitBtnBg = isGitBtnHovered ? Color.FromArgb(70, 70, 70) : Color.FromArgb(50, 50, 50);
            Color gitBtnBorder = isGitBtnHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(70, 70, 70);
            using (var btnBgBrush = new SolidBrush(gitBtnBg))
            using (var btnBorderPen = new Pen(gitBtnBorder))
            using (var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, _githubBtnY * s, wGit * s, 32 * s), 4 * s))
            {
                g.FillPath(btnBgBrush, path);
                g.DrawPath(btnBorderPen, path);
            }
            if (_subFont != null)
            {
                Color btnText = isGitBtnHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                if (_iconFont != null)
                {
                    string iconGit = "\uE71B";
                    var iconSize = g.MeasureString(iconGit, _iconFont);
                    var textSize = g.MeasureString(textGit, _subFont);
                    float iconW = iconSize.Width / s;
                    float iconH = iconSize.Height / s;
                    float textW = textSize.Width / s;
                    float textH = textSize.Height / s;
                    float spacing = 6f;
                    float totalContentW = iconW + spacing + textW;
                    float startX = contentX + (wGit - totalContentW) / 2;

                    g.DrawString(iconGit, _iconFont, btnTextBrush, startX * s, (_githubBtnY + (32 - iconH) / 2) * s);
                    g.DrawString(textGit, _subFont, btnTextBrush, (startX + iconW + spacing) * s, (_githubBtnY + (32 - textH) / 2) * s);
                }
                else
                {
                    var size = g.MeasureString(textGit, _subFont);
                    g.DrawString(textGit, _subFont, btnTextBrush, (contentX + (wGit - size.Width / s) / 2) * s, (_githubBtnY + (32 - size.Height / s) / 2) * s);
                }
            }
            currentY += 12 + 32;

            // 3. Diagnostics Info
            currentY += 20;
            if (_sectionFont != null)
            {
                g.DrawString(GetText("about_diag_title"), _sectionFont, Brushes.White, contentX * s, currentY * s);
            }

            currentY += 25;
            if (_bodyFont != null)
            {
                using var diagBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                string diagText = GetText("about_diag_body");
                float maxTextW = logW - contentX - 40;
                var size = g.MeasureString(diagText, _bodyFont, (int)(maxTextW * s));
                g.DrawString(diagText, _bodyFont, diagBrush, new RectangleF(contentX * s, currentY * s, maxTextW * s, size.Height));
                currentY += size.Height / s;
            }
            else
            {
                currentY += 40;
            }

            // Buttons for diagnostics
            _aboutBtnY = currentY + 16;

            // One-click Gmail feedback button (ID 24)
            bool isGmailBtnHovered = _hoveredElement == 24;
            Color gmailBtnBg = isGmailBtnHovered ? Color.FromArgb(70, 70, 70) : Color.FromArgb(50, 50, 50);
            Color gmailBtnBorder = isGmailBtnHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(70, 70, 70);
            using (var btnBgBrush = new SolidBrush(gmailBtnBg))
            using (var btnBorderPen = new Pen(gmailBtnBorder))
            using (var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, _aboutBtnY * s, wGmail * s, 32 * s), 4 * s))
            {
                g.FillPath(btnBgBrush, path);
                g.DrawPath(btnBorderPen, path);
            }
            if (_subFont != null)
            {
                Color btnText = isGmailBtnHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                if (_iconFont != null)
                {
                    string iconGmail = "\uE715";
                    var iconSize = g.MeasureString(iconGmail, _iconFont);
                    var textSize = g.MeasureString(textGmail, _subFont);
                    float iconW = iconSize.Width / s;
                    float iconH = iconSize.Height / s;
                    float textW = textSize.Width / s;
                    float textH = textSize.Height / s;
                    float spacing = 6f;
                    float totalContentW = iconW + spacing + textW;
                    float startX = contentX + (wGmail - totalContentW) / 2;

                    g.DrawString(iconGmail, _iconFont, btnTextBrush, startX * s, (_aboutBtnY + (32 - iconH) / 2) * s);
                    g.DrawString(textGmail, _subFont, btnTextBrush, (startX + iconW + spacing) * s, (_aboutBtnY + (32 - textH) / 2) * s);
                }
                else
                {
                    var size = g.MeasureString(textGmail, _subFont);
                    g.DrawString(textGmail, _subFont, btnTextBrush, (contentX + (wGmail - size.Width / s) / 2) * s, (_aboutBtnY + (32 - size.Height / s) / 2) * s);
                }
            }
        }

        static bool IsOfficeInstalled(string app)
        {
            string progId = app == "PowerPoint" ? "PowerPoint.Application" : "Word.Application";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\{progId}");
                return key != null;
            }
            catch { return false; }
        }
    }
}
