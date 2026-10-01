using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Clickra.Core.Rendering;

/// <summary>
/// Font-independent vector geometries and drawing routines shared across Clickra UIs.
/// Eliminates runtime font dependency, broken emoji glyphs, and cross-platform metric differences
/// for status headers (check/cross) and visual splitter controls (plus/minus/chevrons).
/// </summary>
public static class VectorIcons
{
    // --- XAML Path Data strings for Fluent UI -----------------------------
    public const string CheckmarkPathData = "M 2,7.5 L 5.5,11 L 12,3.5";
    public const string CrossPathData = "M 3,3 L 11,11 M 11,3 L 3,11";
    public const string PlusPathData = "M 1,6 H 11 M 6,1 V 11";
    public const string MinusPathData = "M 1,6 H 11";
    public const string ChevronLeftPathData = "M 7,2 L 3,6 L 7,10";
    public const string ChevronRightPathData = "M 3,2 L 7,6 L 3,10";

    // --- GDI+ Drawing Methods for Win32 CLI Progress Window ---------------

    /// <summary>Draws a vector checkmark without relying on any font or emoji glyph.</summary>
    public static void DrawSuccessCheckmark(Graphics g, float x, float y, float size, Color color, float strokeWidth)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        PointF[] pts = new[]
        {
            new PointF(x + size * 0.15f, y + size * 0.55f),
            new PointF(x + size * 0.42f, y + size * 0.82f),
            new PointF(x + size * 0.88f, y + size * 0.22f)
        };
        g.DrawLines(pen, pts);
    }

    /// <summary>Draws a vector cross without relying on any font or emoji glyph.</summary>
    public static void DrawFailureCross(Graphics g, float x, float y, float size, Color color, float strokeWidth)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(pen, x + size * 0.2f, y + size * 0.2f, x + size * 0.8f, y + size * 0.8f);
        g.DrawLine(pen, x + size * 0.8f, y + size * 0.2f, x + size * 0.2f, y + size * 0.8f);
    }

    /// <summary>Draws a centered vector plus sign (+) without font dependency.</summary>
    public static void DrawPlus(Graphics g, float centerX, float centerY, float halfSize, Color color, float strokeWidth)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(pen, centerX - halfSize, centerY, centerX + halfSize, centerY);
        g.DrawLine(pen, centerX, centerY - halfSize, centerX, centerY + halfSize);
    }

    /// <summary>Draws a centered vector minus sign (-) without font dependency.</summary>
    public static void DrawMinus(Graphics g, float centerX, float centerY, float halfSize, Color color, float strokeWidth)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(pen, centerX - halfSize, centerY, centerX + halfSize, centerY);
    }

    /// <summary>Draws a centered vector chevron pointing left (<) without font dependency.</summary>
    public static void DrawChevronLeft(Graphics g, float centerX, float centerY, float halfSize, Color color, float strokeWidth)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        PointF[] pts = new[]
        {
            new PointF(centerX + halfSize * 0.5f, centerY - halfSize),
            new PointF(centerX - halfSize * 0.5f, centerY),
            new PointF(centerX + halfSize * 0.5f, centerY + halfSize)
        };
        g.DrawLines(pen, pts);
    }

    /// <summary>Draws a centered vector chevron pointing right (>) without font dependency.</summary>
    public static void DrawChevronRight(Graphics g, float centerX, float centerY, float halfSize, Color color, float strokeWidth)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        PointF[] pts = new[]
        {
            new PointF(centerX - halfSize * 0.5f, centerY - halfSize),
            new PointF(centerX + halfSize * 0.5f, centerY),
            new PointF(centerX - halfSize * 0.5f, centerY + halfSize)
        };
        g.DrawLines(pen, pts);
    }
}
