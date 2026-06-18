using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Clickra.UI.Native;

namespace Clickra.UI
{
    internal static class UIHelper
    {
        public static GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
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

        public static Color Lighten(Color c, float amount)
        {
            int r = (int)(c.R + (255 - c.R) * amount);
            int g = (int)(c.G + (255 - c.G) * amount);
            int b = (int)(c.B + (255 - c.B) * amount);
            return Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
        }

        public static Color GetSystemColorizationColor()
        {
            try
            {
                Win32.DwmGetColorizationColor(out uint color, out bool _);
                return Color.FromArgb(255, Color.FromArgb((int)color));
            }
            catch
            {
                return Color.FromArgb(255, 0, 120, 212);
            }
        }

        public static void DrawHorizontalScrollbar(Graphics g, float x, float y, float width, float thumbX, float thumbWidth, float scale)
        {
            using (var trackBrush = new SolidBrush(Color.FromArgb(15, 255, 255, 255)))
            {
                g.FillRectangle(trackBrush, x * scale, y * scale, width * scale, 2 * scale);
            }
            using (var thumbBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
            {
                g.FillRectangle(thumbBrush, thumbX * scale, y * scale, thumbWidth * scale, 2 * scale);
            }
        }

        public static string TruncateText(Graphics g, string text, Font font, float maxLogicalWidth, float scale)
        {
            if (string.IsNullOrEmpty(text)) return "";
            float measuredWidth = g.MeasureString(text, font).Width / scale;
            if (measuredWidth <= maxLogicalWidth) return text;

            string suffix = "...";
            float suffixWidth = g.MeasureString(suffix, font).Width / scale;
            if (maxLogicalWidth <= suffixWidth) return "...";

            int low = 0;
            int high = text.Length;
            int bestLength = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                string sub = text.Substring(0, mid) + suffix;
                float w = g.MeasureString(sub, font).Width / scale;
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

        public static string TruncateFileName(Graphics g, string filename, Font font, float maxWidth, float scale)
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
