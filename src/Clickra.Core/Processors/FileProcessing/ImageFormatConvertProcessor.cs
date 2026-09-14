using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Linq;

namespace Clickra.Core.Processors;

/// <summary>Converts an image file to a target format (png / jpg / webp / gif / heic).
/// The target format is supplied through the "format" option; files that are
/// already in the target format are skipped so the source is never overwritten.</summary>
    public class ImageFormatConvertProcessor : MultiFileProcessorBase
    {
        private const string ExtHeic = ".heic";
        private const string ExtWebp = ".webp";
        private const string CodecHeic = "heic";
        private const string CodecWebp = "webp";
        private const string HeifStoreUri = "ms-windows-store://pdp/?productid=9PMMSR1CGPWG";
        private const string WebpStoreUri = "ms-windows-store://pdp/?productid=9PG2DK419DRG";

        private string? _outputPath;
        private string _format = "";

        public new void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for image format conversion.");
            _outputPath = outputPath;
            _format = options is not null && options.TryGetValue("format", out var fmt) ? Convert.ToString(fmt) ?? "" : "";
            if (string.IsNullOrWhiteSpace(_format)) throw new ArgumentException("The 'format' option is required for image format conversion.");
            base.Process(files, outputPath, options, onProgress, cancellationToken);
        }

        protected override void ProcessFile(string filePath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke((fileIndex * 100) + 50, totalFiles * 100, $"正在轉換圖片格式: {Path.GetFileName(filePath)} ({fileIndex + 1}/{totalFiles})...");

            if (!File.Exists(filePath)) throw new FileNotFoundException("Image file not found", filePath);

            string sourceExt = Path.GetExtension(filePath).ToLowerInvariant();
            string targetExt = "." + _format.TrimStart('.').ToLowerInvariant();
            if (sourceExt == targetExt)
            {
                onProgress?.Invoke((fileIndex * 100) + 100, totalFiles * 100, $"已是目標格式，跳過: {Path.GetFileName(filePath)}");
                return;
            }

            if (string.Equals(sourceExt, ExtHeic, StringComparison.OrdinalIgnoreCase) && !WicImageHelper.IsHeicDecoderAvailable())
            {
                throw new NotSupportedException(Localize("error_heic_codec_missing"));
            }

            if (string.Equals(_format, CodecHeic, StringComparison.OrdinalIgnoreCase))
            {
                SaveAsHeic(filePath, _outputPath!);
            }
            else if (string.Equals(_format, CodecWebp, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsWebpEncodingSupported())
                {
                    throw new NotSupportedException(Localize("error_webp_codec_missing"));
                }
                ImageFormat imageFormat = ToImageFormat(_format);
                using var image = WicImageHelper.LoadImageSafely(filePath);
                image.Save(_outputPath!, imageFormat);
            }
            else
            {
                ImageFormat imageFormat = ToImageFormat(_format);
                using var image = WicImageHelper.LoadImageSafely(filePath);
                image.Save(_outputPath!, imageFormat);
            }
        }

        private const string LanguageSettingKey = "Language";

        private static string Localize(string key) => Localization.T(key, ClickraStorage.GetSetting(LanguageSettingKey));

        /// <summary>
        /// Saves the image as HEIC using Windows WIC / WinRT (Microsoft HEIF Encoder).
        /// When a Windows HEIF/HEIC extension is present it is used directly; otherwise this throws
        /// with a localized, actionable message rather than producing a non-conformant file.
        /// </summary>
        public static void SaveAsHeic(string inputPath, string outputPath)
        {
            if (!IsHeicEncodingSupported())
            {
                throw new NotSupportedException(Localize("error_heic_codec_missing"));
            }
            WicImageHelper.ConvertFileToHeic(inputPath, outputPath, 90);
        }

        /// <summary>Saves an already-loaded image as HEIC with an explicit encoder quality (1-100).</summary>
        public static void SaveAsHeic(Image image, string outputPath, long quality)
        {
            if (!IsHeicEncodingSupported())
            {
                throw new NotSupportedException(Localize("error_heic_codec_missing"));
            }
            WicImageHelper.SaveAsHeic(image, outputPath, quality);
        }

        /// <summary>Whether the system exposes a HEIF/HEIC encoder (Microsoft HEIF Encoder in WIC/WinRT).</summary>
        public static bool IsHeicEncodingSupported() => WicImageHelper.IsHeicEncoderAvailable();

        /// <summary>Whether the system exposes a WebP encoder (installed with the free
        /// Windows "WebP Image Extensions" package).</summary>
        public static bool IsWebpEncodingSupported() => GetWebpEncoder() is not null;

        private static ImageCodecInfo? GetWebpEncoder()
            => ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => string.Equals(c.MimeType, "image/webp", StringComparison.OrdinalIgnoreCase));

        /// <summary>The codec a target format must encode with ("heic" or "webp"); null for
        /// formats GDI+ encodes out of the box (png/jpg/gif) and unknown formats.</summary>
        public static string? GetRequiredCodec(string format) => format.TrimStart('.').ToLowerInvariant() switch
        {
            CodecHeic => CodecHeic,
            CodecWebp => CodecWebp,
            "png" or "jpg" or "jpeg" or "gif" => null,
            _ => null
        };

        /// <summary>Microsoft Store URI of the free extension that provides the given codec;
        /// null when the codec needs no Store install.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S1075", Justification = "Store URIs are fixed product identifiers.")]
        public static Uri? GetCodecStoreUri(string codec) => codec.ToLowerInvariant() switch
        {
            CodecHeic => new Uri(HeifStoreUri), // NOSONAR:S1075
            CodecWebp => new Uri(WebpStoreUri), // NOSONAR:S1075
            _ => null
        };

        /// <summary>The codec the command needs for these files but the system lacks, or null
        /// when every output can be encoded. Covers img-to-heic / img-to-webp targets and
        /// img-compress on .heic/.webp inputs, which re-encode through the same codec.</summary>
        public static string? GetMissingCodecForCommand(string command, IEnumerable<string> files)
        {
            // If any source file is .heic, we need a HEIC decoder to read it
            if (!WicImageHelper.IsHeicDecoderAvailable() && files.Any(f => string.Equals(Path.GetExtension(f), ExtHeic, StringComparison.OrdinalIgnoreCase)))
            {
                return CodecHeic;
            }

            string? codec = command switch
            {
                "img-to-heic" => CodecHeic,
                "img-to-webp" => CodecWebp,
                "img-compress" => files
                    .Select(f => Path.GetExtension(f).ToLowerInvariant())
                    .Select(ext => ext switch { ExtHeic => CodecHeic, ExtWebp => CodecWebp, _ => null })
                    .FirstOrDefault(c => c is not null),
                _ => null
            };

            return codec switch
            {
                CodecHeic => IsHeicEncodingSupported() ? null : CodecHeic,
                CodecWebp => IsWebpEncodingSupported() ? null : CodecWebp,
                _ => null
            };
        }

        /// <summary>Maps a target format name to its GDI+ encoder; throws for unsupported formats.</summary>
        public static ImageFormat ToImageFormat(string format) => format.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => ImageFormat.Png,
            "jpg" or "jpeg" => ImageFormat.Jpeg,
            CodecWebp => ImageFormat.Webp,
            "gif" => ImageFormat.Gif,
            _ => throw new NotSupportedException($"Unsupported target image format: {format}")
        };

        /// <summary>The canonical output extension for a target format name (e.g. "jpeg" → ".jpg").</summary>
        public static string ToOutputExtension(string format) => format.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => ".png",
            "jpg" or "jpeg" => ".jpg",
            CodecWebp => ExtWebp,
            "gif" => ".gif",
            CodecHeic => ExtHeic,
            _ => throw new NotSupportedException($"Unsupported target image format: {format}")
        };
    }