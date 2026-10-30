using System;

namespace Clickra.Core.Modules.PdfPrint.Processing;

/// <summary>
/// Physical dimensions of a paper format in points (1/72 inch) and millimeters.
/// </summary>
public readonly record struct PaperDimension(
    float WidthPt,
    float HeightPt,
    float WidthMm,
    float HeightMm,
    string DisplayName)
{
    public float AspectRatio => HeightPt > 0 ? WidthPt / HeightPt : 1f;
}

/// <summary>
/// Registry of standard paper sizes and physical dimensions for preview simulation and printing.
/// </summary>
public static class PaperSizeRegistry
{
    public static readonly PaperDimension A3 = new(841.89f, 1190.55f, 297f, 420f, "A3");
    public static readonly PaperDimension A4 = new(595.28f, 841.89f, 210f, 297f, "A4");
    public static readonly PaperDimension A5 = new(419.53f, 595.28f, 148f, 210f, "A5");
    public static readonly PaperDimension B4 = new(728.50f, 1031.81f, 257f, 364f, "B4");
    public static readonly PaperDimension B5 = new(515.91f, 728.50f, 182f, 257f, "B5");
    public static readonly PaperDimension Letter = new(612f, 792f, 215.9f, 279.4f, "Letter");
    public static readonly PaperDimension Legal = new(612f, 1008f, 215.9f, 355.6f, "Legal");
    public static readonly PaperDimension Tabloid = new(792f, 1224f, 279.4f, 431.8f, "Tabloid");
    public static readonly PaperDimension Executive = new(522f, 756f, 184.15f, 266.7f, "Executive");

    /// <summary>
    /// Resolves known paper dimensions by name; defaults to A4 when unspecified or unrecognized.
    /// </summary>
    public static PaperDimension GetDimensions(string? paperName)
    {
        if (string.IsNullOrWhiteSpace(paperName)) return A4;

        string name = paperName.Trim();
        if (ContainsToken(name, "A3")) return A3;
        if (ContainsToken(name, "A4")) return A4;
        if (ContainsToken(name, "A5")) return A5;
        if (ContainsToken(name, "B4")) return B4;
        if (ContainsToken(name, "B5")) return B5;
        if (ContainsToken(name, "Tabloid") || ContainsToken(name, "Ledger") || ContainsToken(name, "11x17")) return Tabloid;
        if (ContainsToken(name, "Legal")) return Legal;
        if (ContainsToken(name, "Letter")) return Letter;
        if (ContainsToken(name, "Executive")) return Executive;

        return A4;
    }

    private static bool ContainsToken(string text, string token) =>
        text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}
