using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    /// <summary>
    /// 提供 CLI 執行階段專專用之 Win32 進度視窗。
    /// </summary>
    public partial class ProgressWindow
    {
        private Color GetSystemColorizationColor()
        {
            if (_hasCachedColorizationColor) return _cachedColorizationColor;
            try
            {
                DwmGetColorizationColor(out uint color, out bool _);
                _cachedColorizationColor = Color.FromArgb(255, Color.FromArgb((int)color));
                _hasCachedColorizationColor = true;
                return _cachedColorizationColor;
            }
            catch
            {
                _cachedColorizationColor = Color.FromArgb(255, 0, 120, 212); // 微軟藍
                _hasCachedColorizationColor = true;
                return _cachedColorizationColor;
            }
        }

        private Color Lighten(Color c, float amount)
        {
            int r = (int)(c.R + (255 - c.R) * amount);
            int g = (int)(c.G + (255 - c.G) * amount);
            int b = (int)(c.B + (255 - c.B) * amount);
            return Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
        }

        private GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Paint(IntPtr hdc)
        {
            if (_bufferBmp == null || _bufferGraphics == null) return;
            var g = _bufferGraphics;
            g.Clear(Color.FromArgb(32, 32, 32));

            bool hasErr, comp, isPrompting; string msg, errMsg, pctStr, promptFile; bool isRetry;
            double dispW; float shimOff; int tot, cur;

            lock (_stateLock)
            {
                hasErr = _hasError; comp = _completed;
                msg = _message; errMsg = _errorMessage;
                dispW = _currentDispWidth; shimOff = _shimmerOffset;
                tot = _total; cur = _current;
                isPrompting = _isPromptingPassword;
                promptFile = _passwordPromptFilename;
                isRetry = _passwordPromptIsRetry;
            }

            float s = _dpiScale;

            if (_titleFont != null)
                g.DrawString("Clickra", _titleFont, Brushes.White, 36 * s, 28 * s);

            if (_subFont != null)
            {
                string lang = ClickraStorage.GetSetting("Language");
                string subText = hasErr ? "作業失敗" : (comp ? "作業完成" : (isPrompting ? Localization.T("pdf_password_title", lang) : "正在執行作業..."));
                Color subColor = hasErr ? Color.FromArgb(255, 90, 70) : (comp ? Color.FromArgb(100, 220, 100) : Color.FromArgb(160, 160, 160));
                using var subBrush = new SolidBrush(subColor);
                g.DrawString(subText, _subFont, subBrush, 36 * s, 72 * s);
            }

            if (_linePen != null)
                g.DrawLine(_linePen, 36 * s, 110 * s, 484 * s, 110 * s);

            if (hasErr)
            {
                if (_headerFont != null)
                {
                    using var errBrush = new SolidBrush(Color.FromArgb(255, 90, 70));
                    g.DrawString("❌ 處理失敗", _headerFont, errBrush, 36 * s, 130 * s);
                }
                if (_msgFont != null)
                {
                    using var errMsgBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    string displayErrMsg = errMsg;
                    if (displayErrMsg.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
                    {
                        displayErrMsg = Localization.T("error_user_aborted", ClickraStorage.GetSetting("Language"));
                    }
                    g.DrawString(displayErrMsg, _msgFont, errMsgBrush, new RectangleF(36 * s, 170 * s, 448 * s, 60 * s));
                }
            }
            else if (comp)
            {
                if (_headerFont != null)
                {
                    using var succBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                    g.DrawString("✔ 轉換成功！", _headerFont, succBrush, 36 * s, 130 * s);
                }
                if (_msgFont != null)
                {
                    using var msgBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    g.DrawString(msg, _msgFont, msgBrush, 36 * s, 170 * s);
                }
                if (_tipFont != null)
                {
                    using var tipBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString("視窗將於數秒後自動關閉...", _tipFont, tipBrush, 36 * s, 220 * s);
                }
            }
            else if (isPrompting)
            {
                if (_msgFont != null)
                {
                    string lang = ClickraStorage.GetSetting("Language");
                    string promptFormat = isRetry 
                        ? Localization.T("pdf_password_retry", lang) 
                        : Localization.T("pdf_password_prompt", lang);
                    string promptText = string.Format(promptFormat, Path.GetFileName(promptFile));

                    using var promptBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    g.DrawString(promptText, _msgFont, promptBrush, new RectangleF(36 * s, 130 * s, 448 * s, 32 * s));
                }
            }
            else
            {
                if (_msgFont != null)
                {
                    string drawPctStr = tot > 0 ? $"{(cur * 100 / tot)}%" : "";
                    float logicalPctW = 0;
                    if (_pctFont != null && tot > 0)
                    {
                        logicalPctW = g.MeasureString(drawPctStr, _pctFont).Width / s;
                    }
                    float logicalMaxMsgW = 448f;
                    if (logicalPctW > 0)
                    {
                        logicalMaxMsgW = 448f - logicalPctW - 10f;
                    }

                    float fullMsgW = g.MeasureString(msg, _msgFont).Width / s;
                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);

                    if (maxLogicalScroll > 0)
                    {
                        float currentScroll = 0f;
                        lock (_stateLock)
                        {
                            if (_scrollOffset > maxLogicalScroll) _scrollOffset = maxLogicalScroll;
                            currentScroll = _scrollOffset;
                        }

                        var oldClip = g.Clip;
                        g.SetClip(new RectangleF(36 * s, 120 * s, logicalMaxMsgW * s, 30 * s));
                        g.DrawString(msg, _msgFont, Brushes.White, 36 * s - currentScroll * s, 130 * s);
                        g.Clip = oldClip;

                        // Draw scrollbar if scrollable
                        float scrollbarY = 152;
                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                        float thumbX = 36f + (currentScroll / fullMsgW) * logicalMaxMsgW;
                        if (thumbX + thumbW > 36f + logicalMaxMsgW) thumbX = 36f + logicalMaxMsgW - thumbW;

                        using (var trackBrush = new SolidBrush(Color.FromArgb(15, 255, 255, 255)))
                        {
                            g.FillRectangle(trackBrush, 36 * s, scrollbarY * s, logicalMaxMsgW * s, 2 * s);
                        }
                        using (var thumbBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                        {
                            g.FillRectangle(thumbBrush, thumbX * s, scrollbarY * s, thumbW * s, 2 * s);
                        }
                    }
                    else
                    {
                        lock (_stateLock)
                        {
                            _scrollOffset = 0f;
                            _isDraggingScroll = false;
                            _dragStartMouseX = 0f;
                            _dragStartOffset = 0f;
                        }
                        g.DrawString(msg, _msgFont, Brushes.White, 36 * s, 130 * s);
                    }
                }

                float barX = 36 * s, barY = 170 * s, barW = 448 * s, barH = 16 * s;
                using var bgPath = GetRoundedRectPath(new RectangleF(barX, barY, barW, barH), 6 * s);
                if (_bgBrush != null) g.FillPath(_bgBrush, bgPath);
                if (_borderPen != null) g.DrawPath(_borderPen, bgPath);

                if (dispW > 3)
                {
                    var fillRect = new RectangleF(barX, barY, (float)(dispW * s), barH);
                    using var fillPath = GetRoundedRectPath(fillRect, 6 * s);
                    
                    Color accent = GetSystemColorizationColor();
                    Color accentLight = Lighten(accent, 0.3f);
                    using var gradBrush = new LinearGradientBrush(fillRect, accent, accentLight, LinearGradientMode.Horizontal);
                    g.FillPath(gradBrush, fillPath);

                    var oldClip = g.Clip;
                    g.SetClip(fillPath);

                    var shimmerRect = new RectangleF(shimOff * s, barY, 120 * s, barH);
                    using var shimmerBrush = new LinearGradientBrush(shimmerRect, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(100, 255, 255, 255), LinearGradientMode.Horizontal);
                    var blend = new ColorBlend(3);
                    blend.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(100, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                    blend.Positions = new float[] { 0.0f, 0.5f, 1.0f };
                    shimmerBrush.InterpolationColors = blend;

                    g.FillRectangle(shimmerBrush, shimmerRect);
                    g.Clip = oldClip;
                }

                pctStr = tot > 0 ? $"{(cur * 100 / tot)}%" : "";
                if (_pctFont != null)
                {
                    var size = g.MeasureString(pctStr, _pctFont);
                    using var pctBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    g.DrawString(pctStr, _pctFont, pctBrush, 484 * s - size.Width, 145 * s);
                }

                if (_tipFont != null)
                {
                    using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                    g.DrawString("請稍候，正在背景高速處理中...", _tipFont, tipBrush, 36 * s, 220 * s);
                }

                // Draw "Run in Background" (minimize to tray) icon button in top right
                {
                    var btnRect = new RectangleF(456 * s, 36 * s, 28 * s, 28 * s);
                    using var btnPath = GetRoundedRectPath(btnRect, 4 * s);

                    Color btnBg = _isTrayBtnHovered ? Color.FromArgb(60, 60, 60) : Color.Transparent;
                    Color btnPenColor = _isTrayBtnHovered ? GetSystemColorizationColor() : Color.FromArgb(160, 160, 160);

                    using var btnBrush = new SolidBrush(btnBg);
                    g.FillPath(btnBrush, btnPath);

                    if (_isTrayBtnHovered)
                    {
                        using var borderPen = new Pen(btnPenColor, 1f * s);
                        g.DrawPath(borderPen, btnPath);
                    }

                    // Draw diagonal arrow pointing down-right ↘
                    using var arrowPen = new Pen(btnPenColor, 2f * s);
                    float startX = btnRect.X + 8 * s;
                    float startY = btnRect.Y + 8 * s;
                    float endX = btnRect.X + 20 * s;
                    float endY = btnRect.Y + 20 * s;
                    g.DrawLine(arrowPen, startX, startY, endX, endY);
                    g.DrawLine(arrowPen, endX, endY, endX - 7 * s, endY);
                    g.DrawLine(arrowPen, endX, endY, endX, endY - 7 * s);

                    // Draw custom tooltip next to the button when hovered
                    if (_isTrayBtnHovered && _tipFont != null)
                    {
                        string lang = ClickraStorage.GetSetting("Language");
                        string tooltipText = Localization.T("progress_background", lang);
                        var tSize = g.MeasureString(tooltipText, _tipFont);
                        float tx = btnRect.X - tSize.Width - 10 * s;
                        float ty = btnRect.Y + (btnRect.Height - tSize.Height) / 2;

                        using var tBrush = new SolidBrush(Color.FromArgb(240, 30, 30, 30));
                        using var tPen = new Pen(Color.FromArgb(80, 80, 80), 1f * s);
                        using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));

                        var tRect = new RectangleF(tx - 6 * s, ty - 4 * s, tSize.Width + 12 * s, tSize.Height + 8 * s);
                        using var tPath = GetRoundedRectPath(tRect, 4 * s);
                        g.FillPath(tBrush, tPath);
                        g.DrawPath(tPen, tPath);
                        g.DrawString(tooltipText, _tipFont, textBrush, tx, ty);
                    }
                }
            }

            using var targetG = Graphics.FromHdc(hdc);
            if (isPrompting)
            {
                targetG.ExcludeClip(new Rectangle((int)(36 * s - 1), (int)(165 * s - 1), (int)(448 * s + 2), (int)(28 * s + 2)));
                targetG.ExcludeClip(new Rectangle((int)(280 * s - 1), (int)(210 * s - 1), (int)(90 * s + 2), (int)(30 * s + 2)));
                targetG.ExcludeClip(new Rectangle((int)(394 * s - 1), (int)(210 * s - 1), (int)(90 * s + 2), (int)(30 * s + 2)));
            }
            if (_bufferBmp != null)
            {
                targetG.DrawImage(_bufferBmp, 0, 0, _bufferBmp.Width, _bufferBmp.Height);
            }
        }

        private static string TruncateProgressMessage(Graphics g, string msg, Font font, float maxLogicalWidth, float scale)
        {
            if (string.IsNullOrEmpty(msg)) return "";
            if (font == null) return msg;

            int colonIdx = msg.IndexOf(": ");
            if (colonIdx == -1)
            {
                return TruncateText(g, msg, font, maxLogicalWidth, scale);
            }

            string prefix = msg.Substring(0, colonIdx + 2);
            string rest = msg.Substring(colonIdx + 2);

            string filename = rest;
            string suffix = "";

            if (rest.EndsWith("..."))
            {
                int pIdx = rest.LastIndexOf(" (");
                if (pIdx != -1 && pIdx < rest.Length - 3)
                {
                    filename = rest.Substring(0, pIdx);
                    suffix = rest.Substring(pIdx);
                }
                else
                {
                    filename = rest.Substring(0, rest.Length - 3);
                    suffix = "...";
                }
            }
            else
            {
                int pIdx = rest.LastIndexOf(" (");
                if (pIdx != -1)
                {
                    filename = rest.Substring(0, pIdx);
                    suffix = rest.Substring(pIdx);
                }
            }

            float prefixW = g.MeasureString(prefix, font).Width / scale;
            float suffixW = g.MeasureString(suffix, font).Width / scale;
            float availableW = maxLogicalWidth - prefixW - suffixW;

            if (availableW <= 20)
            {
                return TruncateText(g, msg, font, maxLogicalWidth, scale);
            }

            string truncatedFile = TruncateFileName(g, filename, font, availableW, scale);
            return prefix + truncatedFile + suffix;
        }

        private static string TruncateText(Graphics g, string text, Font font, float maxLogicalWidth, float scale)
        {
            if (string.IsNullOrEmpty(text)) return "";
            float measuredWidth = g.MeasureString(text, font).Width / scale;
            if (measuredWidth <= maxLogicalWidth) return text;

            string suffix = "...";
            float suffixWidth = g.MeasureString(suffix, font).Width / scale;
            if (maxLogicalWidth <= suffixWidth) return "...";

            int low = 0;
            int high = text.Length - 1;
            int bestLength = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                string candidate = text.Substring(0, mid) + suffix;
                float w = g.MeasureString(candidate, font).Width / scale;

                if (w <= maxLogicalWidth)
                {
                    bestLength = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return text.Substring(0, bestLength) + suffix;
        }

        private static string TruncateFileName(Graphics g, string filename, Font font, float maxWidth, float scale)
        {
            if (string.IsNullOrEmpty(filename)) return "";
            if (g.MeasureString(filename, font).Width / scale <= maxWidth) return filename;

            int low = 2;
            int high = filename.Length - 1;
            string best = "...";

            int extLen = 0;
            int dotIdx = filename.LastIndexOf('.');
            if (dotIdx >= 0)
            {
                extLen = filename.Length - dotIdx;
            }

            int targetRight = extLen + 8;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                
                int rightLen, leftLen;
                if (mid > targetRight)
                {
                    rightLen = targetRight;
                    leftLen = mid - rightLen;
                }
                else
                {
                    rightLen = Math.Min(extLen, mid - 1);
                    if (rightLen < 0) rightLen = 0;
                    leftLen = mid - rightLen;
                }

                string separator = "...";
                string rightPart = filename.Substring(filename.Length - rightLen);
                if (rightPart.StartsWith("."))
                {
                    separator = "..";
                }
                string candidate = filename.Substring(0, leftLen) + separator + rightPart;

                if (g.MeasureString(candidate, font).Width / scale <= maxWidth)
                {
                    best = candidate;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            if (best == "...")
            {
                int left = Math.Max(1, filename.Length - extLen);
                string suffix = extLen > 0 ? filename.Substring(filename.Length - extLen) : "";
                best = filename.Substring(0, Math.Min(2, left)) + (suffix.StartsWith(".") ? ".." : "...") + suffix;
            }

            return best;
        }
    }
}
