using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Modules.PdfDecrypt.Application;
using Clickra.Core.Modules.PdfToImage.Processing;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Modules.PdfToImage.Application;

/// <summary>Authoritative application workflow for converting PDF files to raster images.</summary>
public sealed class PdfToImageUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "pdf2img";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(PdfToImageUseCase);
    protected override string InputRequirementError => "At least one PDF file is required.";
    protected override string OutputCountError => "PDF to image conversion requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported PDF input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + ".png";

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        if (request.Options != null)
        {
            foreach (var kvp in request.Options)
            {
                normalized[kvp.Key] = kvp.Value;
            }
        }

        string format = PdfToImageOptions.NormalizeFormat(
            normalized.TryGetValue(PdfToImageOptions.FormatOptionKey, out var f) ? f as string : null);
        normalized[PdfToImageOptions.FormatOptionKey] = format;

        int rawDpi = PdfToImageOptions.DefaultDpi;
        if (normalized.TryGetValue(PdfToImageOptions.DpiOptionKey, out var d))
        {
            if (d is int di)
            {
                rawDpi = di;
            }
            else if (int.TryParse(d?.ToString(), out int parsedDpi))
            {
                rawDpi = parsedDpi;
            }
        }
        normalized[PdfToImageOptions.DpiOptionKey] = PdfToImageOptions.NormalizeDpi(rawDpi);

        string pages = PdfToImageOptions.DefaultPages;
        if (normalized.TryGetValue(PdfToImageOptions.PagesOptionKey, out var p) && p is string ps && !string.IsNullOrWhiteSpace(ps))
        {
            pages = ps.Trim();
        }
        normalized[PdfToImageOptions.PagesOptionKey] = pages;

        bool stitch = false;
        if (normalized.TryGetValue(PdfToImageOptions.StitchOptionKey, out var s))
        {
            if (s is bool sb)
            {
                stitch = sb;
            }
            else if (bool.TryParse(s?.ToString(), out bool pb))
            {
                stitch = pb;
            }
        }
        normalized[PdfToImageOptions.StitchOptionKey] = stitch;

        return normalized;
    }

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        string? initialPassword = plan.NormalizedOptions.TryGetValue(PdfToImageOptions.PasswordOptionKey, out var pwdObj) && pwdObj is string pwdStr
            ? pwdStr
            : null;

        await ExecuteWithPasswordRetryAsync(
            index,
            plan,
            interaction,
            initialPassword,
            async password =>
            {
                var processor = new PdfToImageProcessor();
                var options = new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(password))
                {
                    options[PdfToImageOptions.PasswordOptionKey] = password;
                }

                await Task.Run(
                    () => processor.Process(
                        new List<string> { plan.Inputs[index] },
                        plan.Outputs[index],
                        options,
                        (current, total, message) =>
                            ReportFileProgress(plan, index, progress, current, message),
                        cancellationToken),
                    cancellationToken);
            },
            IsPasswordError,
            cancellationToken);
    }

    private static bool IsPasswordError(Exception ex) =>
        ex is PdfEncryptedException ||
        (ex is PdfReaderException readerEx && readerEx.Message.Contains("password", StringComparison.OrdinalIgnoreCase));
}
