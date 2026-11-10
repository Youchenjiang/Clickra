using System;

namespace Clickra.Core.Modules.Pdf.ToImage.Application;

/// <summary>
/// Options for configuring PDF to image conversion.
/// </summary>
public sealed record PdfToImageOptions(
    string Format = PdfToImageOptions.DefaultFormat,
    int Dpi = PdfToImageOptions.DefaultDpi,
    string Pages = PdfToImageOptions.DefaultPages,
    bool Stitch = false,
    string? Password = null)
{
    public const string DefaultFormat = "png";
    public const int DefaultDpi = 150;
    public const string DefaultPages = "all";

    public const string FormatOptionKey = "format";
    public const string DpiOptionKey = "dpi";
    public const string PagesOptionKey = "pages";
    public const string StitchOptionKey = "stitch";
    public const string PasswordOptionKey = "password";

    /// <summary>Normalizes image format string to png, jpg, or webp.</summary>
    public static string NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return DefaultFormat;
        string f = format.Trim().ToLowerInvariant().TrimStart('.');
        return f switch
        {
            "jpg" or "jpeg" => "jpg",
            "webp" => "webp",
            _ => "png"
        };
    }

    /// <summary>Clamps DPI within [72, 600].</summary>
    public static int NormalizeDpi(int dpi) => Math.Clamp(dpi, 72, 600);
}
