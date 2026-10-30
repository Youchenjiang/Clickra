using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Clickra.Core.Rendering;

/// <summary>
/// Pre-print contrast enhancement and handwriting darkening algorithm.
/// Bleaches scanning shadows / paper background to pure white (#FFFFFF),
/// detects and boosts blue/black pen strokes, and applies a non-linear gamma curve (default 2.2)
/// to transform faint handwriting into solid, clear laser toner black.
/// </summary>
public static class PdfContrastEnhancer
{
    public const double DefaultGamma = 2.2;

    /// <summary>
    /// Enhances the bitmap in-place with adaptive background bleaching and handwriting darkening.
    /// </summary>
    public static void Enhance(Bitmap bitmap, double gamma = DefaultGamma)
    {
        if (bitmap == null) return;

        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0) return;

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);

        try
        {
            int stride = Math.Abs(data.Stride);
            int totalBytes = stride * height;
            byte[] buffer = new byte[totalBytes];
            Marshal.Copy(data.Scan0, buffer, 0, totalBytes);

            int totalPixels = width * height;
            int[] hist = BuildLuminanceHistogram(buffer, width, height, stride);

            if (!TryCalculateCutoffs(hist, totalPixels, out float blackPt, out float whitePt))
            {
                return;
            }

            byte[] lut = BuildGammaLut(blackPt, whitePt, gamma);
            ApplyLut(buffer, width, height, stride, lut);

            Marshal.Copy(buffer, 0, data.Scan0, totalBytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int[] BuildLuminanceHistogram(byte[] buffer, int width, int height, int stride)
    {
        int[] hist = new int[256];
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + (x * 4);
                byte b = buffer[idx];
                byte g = buffer[idx + 1];
                byte r = buffer[idx + 2];
                hist[CalculateLuminance(r, g, b)]++;
            }
        }
        return hist;
    }

    private static bool TryCalculateCutoffs(int[] hist, int totalPixels, out float blackPt, out float whitePt)
    {
        blackPt = 20.0f;
        whitePt = 255.0f;

        int whiteCountTarget = (int)Math.Round(totalPixels * 0.98);
        int accumulated = 0;
        int detectedWhite = 255;
        for (int i = 0; i < 256; i++)
        {
            accumulated += hist[i];
            if (accumulated >= whiteCountTarget)
            {
                detectedWhite = i;
                break;
            }
        }

        int textThreshold = Math.Max(0, detectedWhite - 15);
        int textPixelCount = 0;
        for (int i = 0; i < textThreshold; i++)
        {
            textPixelCount += hist[i];
        }

        if (textPixelCount == 0)
        {
            return false;
        }

        int blackCountTarget = (int)Math.Round(textPixelCount * 0.15);
        accumulated = 0;
        int detectedBlack = 0;
        for (int i = 0; i < textThreshold; i++)
        {
            accumulated += hist[i];
            if (accumulated >= blackCountTarget)
            {
                detectedBlack = i;
                break;
            }
        }

        blackPt = Math.Max(20.0f, detectedBlack);
        whitePt = Math.Max(blackPt + 1.0f, detectedWhite);
        return true;
    }

    private static byte[] BuildGammaLut(float blackPt, float whitePt, double gamma)
    {
        float range = whitePt - blackPt;
        byte[] lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            float scale = Math.Clamp((i - blackPt) / range, 0f, 1f);
            lut[i] = (byte)Math.Clamp((int)Math.Round(Math.Pow(scale, gamma) * 255.0), 0, 255);
        }
        return lut;
    }

    private static void ApplyLut(byte[] buffer, int width, int height, int stride, byte[] lut)
    {
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + (x * 4);
                byte lum = (byte)CalculateLuminance(buffer[idx + 2], buffer[idx + 1], buffer[idx]);
                byte enhanced = lut[lum];
                buffer[idx] = enhanced;
                buffer[idx + 1] = enhanced;
                buffer[idx + 2] = enhanced;
            }
        }
    }

    /// <summary>
    /// Calculates perceived grayscale luminance with blue ballpoint pen boost.
    /// </summary>
    public static int CalculateLuminance(byte r, byte g, byte b)
    {
        float blueBoost = (b > r + 10 && b > 100) ? (b - r) * 0.5f : 0f;
        float lum = (0.299f * r) + (0.587f * g) + (0.114f * b) - blueBoost;
        return Math.Clamp((int)Math.Round(lum), 0, 255);
    }
}
