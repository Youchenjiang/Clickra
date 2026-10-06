using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Clickra.Core.Processors;

/// <summary>Compresses an image in its existing format: re-encodes at the saved quality level
/// and, when a max long edge is configured, downscales to it. The caller supplies the output
/// path (the registry's <c>_compressed</c> name), so the source file is never overwritten —
/// mirroring <see cref="ImageFormatConvertProcessor"/>, which is also one command per file.
/// <para>
/// The quality level comes from <see cref="ImageCompressionOptions"/> while codec checks use
/// the same WIC/libwebp capability helpers as image format conversion, so compression cannot
/// silently take a different codec path.
/// </para></summary>
public class ImageCompressionProcessor : MultiFileProcessorBase
{
    private sealed class PromotionState
    {
        public object SyncRoot { get; } = new();
        public long LatestSequence { get; set; }
        public int ActiveRequests { get; set; }
    }

    private static readonly ConcurrentDictionary<string, PromotionState> PromotionStates =
        new(StringComparer.OrdinalIgnoreCase);
    private static long _nextRequestSequence;

    private const string OptionLevel = "level";
    private const string OptionQuality = "quality";
    private const string OptionMaxDimension = "max_dimension";
    private const string MimeJpeg = "image/jpeg";

    private string? _outputPath;
    private ImageCompressionLevel _level = ImageCompressionLevel.Small;
    private int _quality = ImageCompressionOptions.GetQuality(ImageCompressionLevel.Small);
    private int _maxDimension;
    private string? _normalizedOutputPath;
    private PromotionState? _promotionState;
    private long _requestSequence;

    public override void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for image compression.");
        _outputPath = outputPath;
        _normalizedOutputPath = Path.GetFullPath(outputPath);

        if (!ImageCompressionOptions.TryParseLevel(GetString(options, OptionLevel), out _level))
            throw new ArgumentException($"Unknown image compression level '{GetString(options, OptionLevel)}'.");

        _quality = GetInt(options, OptionQuality) ?? ImageCompressionOptions.GetQuality(_level);
        if (_quality is < 1 or > 100)
            throw new ArgumentException($"Image compression quality must be between 1 and 100, got {_quality}.");

        // 0 keeps the original size, which is what the settings registry documents.
        _maxDimension = Math.Max(0, GetInt(options, OptionMaxDimension) ?? 0);

        RegisterPromotionRequest();
        try
        {
            base.Process(files, outputPath, options, onProgress, cancellationToken);
        }
        finally
        {
            ReleasePromotionRequest();
        }
    }

    protected override void ProcessFile(string filePath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke((fileIndex * 100) + 10, totalFiles * 100, Localization.T(
            "img_compress_progress_compressing", Path.GetFileName(filePath), fileIndex + 1, totalFiles));

        if (!File.Exists(filePath)) throw new FileNotFoundException("Image file not found", filePath);

        // Compressing into the file being read would destroy the only copy of the input; the
        // output name comes from ConvertCommandRegistry.EstimateOutputs and never collides, so
        // reaching this means a caller built its own path.
        if (string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(_outputPath!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image compression must not write over the file it is compressing.");

        EnsureCodecAvailable(filePath);

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (IsMultiFrameImage(filePath, extension))
        {
            if (_maxDimension > 0)
                throw new NotSupportedException(Localization.T("error_img_compress_multiframe_resize", Path.GetFileName(filePath)));

            PreserveOriginal(
                filePath,
                extension,
                cancellationToken,
                onProgress,
                fileIndex,
                totalFiles,
                "img_compress_progress_preserving");
            return;
        }

        using var source = WicImageHelper.LoadImageSafely(filePath);
        using var resized = ResizeToLongEdge(source, _maxDimension);
        bool wasResized = resized is not null;
        Bitmap imageToSave = resized ?? source;

        cancellationToken.ThrowIfCancellationRequested();

        // PNG/GIF are already lossless. Re-encoding them without either a useful indexed-palette
        // reduction (PNG) or a requested resize only throws metadata away and can make the file
        // larger. Preserve the original bytes in that case.
        if (extension == ".gif" && !wasResized)
        {
            PreserveOriginal(
                filePath,
                extension,
                cancellationToken,
                onProgress,
                fileIndex,
                totalFiles,
                "img_compress_progress_preserving");
            return;
        }

        using Bitmap? indexedPng = extension == ".png"
            ? TryCreateIndexedPaletteBitmap(imageToSave, cancellationToken)
            : null;

        if (extension == ".png" && indexedPng is null && !wasResized)
        {
            PreserveOriginal(
                filePath,
                extension,
                cancellationToken,
                onProgress,
                fileIndex,
                totalFiles,
                "img_compress_progress_preserving");
            return;
        }

        onProgress?.Invoke((fileIndex * 100) + 70, totalFiles * 100, Localization.T(
            "img_compress_progress_saving", Path.GetFileName(filePath)));

        string candidatePath = CreateCandidatePath(extension);
        try
        {
            Save(indexedPng ?? imageToSave, extension, candidatePath);
            cancellationToken.ThrowIfCancellationRequested();

            // "Compress" must never hand the user a larger file. If the encoder made no
            // improvement, preserve the original bytes at the requested output name instead.
            if (!wasResized && new FileInfo(candidatePath).Length >= new FileInfo(filePath).Length)
            {
                TryDelete(candidatePath);
                PreserveOriginal(
                    filePath,
                    extension,
                    cancellationToken,
                    onProgress,
                    fileIndex,
                    totalFiles,
                    "img_compress_progress_not_smaller");
                return;
            }

            PromoteCandidate(candidatePath);
        }
        finally
        {
            TryDelete(candidatePath);
        }
    }

    /// <summary>Fails loudly when compression cannot decode or re-encode the source format.</summary>
    private static void EnsureCodecAvailable(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (WicImageHelper.IsHeifExtension(extension))
        {
            if (!WicImageHelper.IsHeicDecoderAvailable())
                throw new NotSupportedException(Localization.T("error_heic_decoder_missing"));
            if (!WicImageHelper.IsHeicEncoderAvailable())
                throw new NotSupportedException(Localization.T("error_heic_codec_missing"));
        }
        if (extension == ".webp" && !WicImageHelper.IsWebpEncoderAvailable())
            throw new NotSupportedException(Localization.T("error_img_compress_unsupported", "webp"));
    }

    /// <summary>Returns true when GIF/TIFF input contains multiple frames/pages that a single
    /// Bitmap re-encode would silently discard.</summary>
    private static bool IsMultiFrameImage(string filePath, string extension)
    {
        if (extension is not (".gif" or ".tif" or ".tiff")) return false;

        using var image = Image.FromFile(filePath);
        Guid dimension = extension == ".gif" ? FrameDimension.Time.Guid : FrameDimension.Page.Guid;
        return image.GetFrameCount(new FrameDimension(dimension)) > 1;
    }

    /// <summary>Downscales so the long edge lands exactly on <paramref name="maxDimension"/>,
    /// preserving the aspect ratio. Returns null when no resizing is needed, so the caller can
    /// keep using the decoded source instead of paying for a second bitmap.</summary>
    private static Bitmap? ResizeToLongEdge(Bitmap source, int maxDimension)
    {
        if (maxDimension <= 0) return null;

        int longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxDimension) return null;

        double scale = (double)maxDimension / longEdge;
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var target = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        try
        {
            if (source.HorizontalResolution > 0 && source.VerticalResolution > 0)
                target.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using var graphics = Graphics.FromImage(target);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    /// <summary>Re-encodes the image into the format its source file already uses, so "compress"
    /// never silently changes the user's file type. Quality applies where the codec supports it
    /// (JPEG/WebP/HEIC); PNG, BMP, GIF and TIFF re-encode losslessly through their own encoder.</summary>
    private void Save(Bitmap image, string extension, string outputPath)
    {
        switch (extension)
        {
            case ".jpg":
            case ".jpeg":
                SaveWithQuality(image, MimeJpeg, "jpg", outputPath);
                break;
            case ".webp":
                WicImageHelper.SaveAsWebp(image, outputPath, _quality);
                break;
            case ".heic":
            case ".heif":
            case ".hif":
                ImageFormatConvertProcessor.SaveAsHeic(image, outputPath, _quality);
                break;
            case ".png":
                image.Save(outputPath, ImageFormat.Png);
                break;
            case ".bmp":
                image.Save(outputPath, ImageFormat.Bmp);
                break;
            case ".gif":
                image.Save(outputPath, ImageFormat.Gif);
                break;
            case ".tif":
            case ".tiff":
                // TIFF is a registry-accepted input, so it must compress here too rather than
                // fail as "unsupported": GDI+ re-encodes it losslessly (LZW).
                image.Save(outputPath, ImageFormat.Tiff);
                break;
            default:
                throw new NotSupportedException(Localization.T(
                    "error_img_compress_unsupported", extension.TrimStart('.')));
        }
    }

    /// <summary>Saves through the named lossy encoder with the resolved quality.</summary>
    private void SaveWithQuality(Bitmap image, string mimeType, string formatName, string outputPath)
    {
        var encoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => string.Equals(c.MimeType, mimeType, StringComparison.OrdinalIgnoreCase));
        if (encoder is null)
        {
            // WebP is the one lossy encoder Windows installs separately; JPEG ships with GDI+,
            // so a missing JPEG encoder is not a "go install something" case.
            throw new NotSupportedException(Localization.T("error_img_compress_unsupported", formatName));
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
        image.Save(outputPath, encoder, parameters);
    }

    /// <summary>
    /// Builds an exact 8-bit indexed copy when the image contains at most 256 ARGB colours.
    /// Returns null as soon as a 257th colour is seen, so ordinary photos do not pay for a
    /// full-image palette conversion. The palette is exact: there is no nearest-colour mapping.
    /// </summary>
    private static Bitmap? TryCreateIndexedPaletteBitmap(Bitmap source, CancellationToken cancellationToken)
    {
        int width = source.Width;
        int height = source.Height;
        var rectangle = new Rectangle(0, 0, width, height);
        BitmapData sourceData = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = checked(width * 4);
            var row = new byte[rowBytes];
            var paletteMap = new Dictionary<int, byte>();
            var paletteColors = new List<Color>(256);

            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Marshal.Copy(GetRowPointer(sourceData, y, height), row, 0, rowBytes);
                for (int x = 0; x < width; x++)
                {
                    int offset = x * 4;
                    int argbValue = (row[offset + 3] << 24) |
                                    (row[offset + 2] << 16) |
                                    (row[offset + 1] << 8) |
                                     row[offset];
                    if (paletteMap.ContainsKey(argbValue))
                        continue;

                    if (paletteMap.Count == 256)
                        return null;

                    byte index = checked((byte)paletteMap.Count);
                    paletteMap.Add(argbValue, index);
                    paletteColors.Add(Color.FromArgb(argbValue));
                }
            }

            var indexed = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            try
            {
                if (source.HorizontalResolution > 0 && source.VerticalResolution > 0)
                    indexed.SetResolution(source.HorizontalResolution, source.VerticalResolution);

                ColorPalette palette = indexed.Palette;
                for (int i = 0; i < paletteColors.Count; i++)
                    palette.Entries[i] = paletteColors[i];
                indexed.Palette = palette;

                BitmapData indexedData = indexed.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                try
                {
                    int indexedStride = Math.Abs(indexedData.Stride);
                    var indexedRow = new byte[indexedStride];
                    for (int y = 0; y < height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Marshal.Copy(GetRowPointer(sourceData, y, height), row, 0, rowBytes);
                        Array.Clear(indexedRow, 0, indexedRow.Length);
                        for (int x = 0; x < width; x++)
                        {
                            int offset = x * 4;
                            int argbValue = (row[offset + 3] << 24) |
                                            (row[offset + 2] << 16) |
                                            (row[offset + 1] << 8) |
                                             row[offset];
                            indexedRow[x] = paletteMap[argbValue];
                        }
                        Marshal.Copy(indexedRow, 0, GetRowPointer(indexedData, y, height), indexedStride);
                    }
                }
                finally
                {
                    indexed.UnlockBits(indexedData);
                }

                return indexed;
            }
            catch
            {
                indexed.Dispose();
                throw;
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
        }
    }

    private static IntPtr GetRowPointer(BitmapData data, int y, int height) =>
        data.Stride >= 0
            ? IntPtr.Add(data.Scan0, y * data.Stride)
            : IntPtr.Add(data.Scan0, (height - 1 - y) * -data.Stride);

    /// <summary>Copies the source bytes to the normal _compressed output without decoding them.</summary>
    private void PreserveOriginal(
        string sourcePath,
        string extension,
        CancellationToken cancellationToken,
        Action<int, int, string>? onProgress,
        int fileIndex,
        int totalFiles,
        string progressKey)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke((fileIndex * 100) + 70, totalFiles * 100, Localization.T(
            progressKey, Path.GetFileName(sourcePath)));

        string candidatePath = CreateCandidatePath(extension);
        try
        {
            CopyFileCancellable(sourcePath, candidatePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PromoteCandidate(candidatePath);
        }
        finally
        {
            TryDelete(candidatePath);
        }
    }

    private string CreateCandidatePath(string extension)
    {
        string outputPath = Path.GetFullPath(_outputPath!);
        string directory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        return Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.clickra{extension}");
    }

    private void RegisterPromotionRequest()
    {
        string outputPath = _normalizedOutputPath!;
        while (true)
        {
            PromotionState state = PromotionStates.GetOrAdd(outputPath, _ => new PromotionState());
            lock (state.SyncRoot)
            {
                if (!PromotionStates.TryGetValue(outputPath, out PromotionState? current) || !ReferenceEquals(current, state))
                    continue;

                long sequence = Interlocked.Increment(ref _nextRequestSequence);
                state.ActiveRequests++;
                state.LatestSequence = sequence;
                _promotionState = state;
                _requestSequence = sequence;
                return;
            }
        }
    }

    private void ReleasePromotionRequest()
    {
        PromotionState? state = _promotionState;
        string? outputPath = _normalizedOutputPath;
        if (state is null || outputPath is null) return;

        lock (state.SyncRoot)
        {
            state.ActiveRequests--;
            if (state.ActiveRequests == 0)
                PromotionStates.TryRemove(outputPath, out _);
        }
        _promotionState = null;
    }

    private void PromoteCandidate(string candidatePath)
    {
        PromotionState state = _promotionState
            ?? throw new InvalidOperationException("Image compression promotion state was not initialized.");
        lock (state.SyncRoot)
        {
            if (state.LatestSequence != _requestSequence) return;
            File.Move(candidatePath, _outputPath!, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only. A conversion failure must preserve the original exception.
        }
    }

    private static void CopyFileCancellable(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 128 * 1024;
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
        using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            destination.Write(buffer, 0, read);
        }
    }

    private static string? GetString(Dictionary<string, object>? options, string key) =>
        options is not null && options.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

    private static int? GetInt(Dictionary<string, object>? options, string key) =>
        options is not null && options.TryGetValue(key, out var value) && int.TryParse(Convert.ToString(value), out int parsed)
            ? parsed
            : null;
}
