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

            if (string.Equals(_format, "heic", StringComparison.OrdinalIgnoreCase))
            {
                SaveAsHeic(filePath, _outputPath!);
            }
            else
            {
                ImageFormat imageFormat = ToImageFormat(_format);
                using (var image = WicImageHelper.LoadImageSafely(filePath))
                {
                    image.Save(_outputPath!, imageFormat);
                }
            }
        }

        private const string LanguageSettingKey = "Language";

        /// <summary>
        /// Saves the image as HEIC using Windows WIC / WinRT (Microsoft HEIF Encoder).
        /// When a Windows HEIF/HEIC extension is present it is used directly; otherwise this throws
        /// with a localized, actionable message rather than producing a non-conformant file.
        /// </summary>
        public static void SaveAsHeic(string inputPath, string outputPath)
        {
            WicImageHelper.ConvertFileToHeic(inputPath, outputPath, 90);
        }

        /// <summary>Saves an already-loaded image as HEIC with an explicit encoder quality (1-100).</summary>
        public static void SaveAsHeic(Image image, string outputPath, long quality)
        {
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
            "heic" => "heic",
            "webp" => "webp",
            "png" or "jpg" or "jpeg" or "gif" => null,
            _ => null
        };

        /// <summary>Microsoft Store URI of the free extension that provides the given codec;
        /// empty when the codec needs no Store install.</summary>
        public static string GetCodecStoreUri(string codec) => codec.ToLowerInvariant() switch
        {
            "heic" => "ms-windows-store://pdp/?productid=9PMMSR1CGPWG", // HEIF Image Extensions
            "webp" => "ms-windows-store://pdp/?productid=9PG2DK419DRG", // WebP Image Extensions
            _ => ""
        };

        /// <summary>The codec the command needs for these files but the system lacks, or null
        /// when every output can be encoded. Covers img-to-heic / img-to-webp targets and
        /// img-compress on .heic/.webp inputs, which re-encode through the same codec.</summary>
        public static string? GetMissingCodecForCommand(string command, IEnumerable<string> files)
        {
            // If any source file is .heic, we need a HEIC decoder to read it
            if (files.Any(f => string.Equals(Path.GetExtension(f), ".heic", StringComparison.OrdinalIgnoreCase)))
            {
                if (!WicImageHelper.IsHeicDecoderAvailable()) return "heic";
            }

            string? codec = command switch
            {
                "img-to-heic" => "heic",
                "img-to-webp" => "webp",
                "img-compress" => files
                    .Select(f => Path.GetExtension(f).ToLowerInvariant())
                    .Select(ext => ext switch { ".heic" => "heic", ".webp" => "webp", _ => null })
                    .FirstOrDefault(c => c is not null),
                _ => null
            };

            return codec switch
            {
                "heic" => IsHeicEncodingSupported() ? null : "heic",
                "webp" => IsWebpEncodingSupported() ? null : "webp",
                _ => null
            };
        }

        /// <summary>Maps a target format name to its GDI+ encoder; throws for unsupported formats.</summary>
        public static ImageFormat ToImageFormat(string format) => format.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => ImageFormat.Png,
            "jpg" or "jpeg" => ImageFormat.Jpeg,
            "webp" => ImageFormat.Webp,
            "gif" => ImageFormat.Gif,
            _ => throw new NotSupportedException($"Unsupported target image format: {format}")
        };

        /// <summary>The canonical output extension for a target format name (e.g. "jpeg" → ".jpg").</summary>
        public static string ToOutputExtension(string format) => format.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => ".png",
            "jpg" or "jpeg" => ".jpg",
            "webp" => ".webp",
            "gif" => ".gif",
            "heic" => ".heic",
            _ => throw new NotSupportedException($"Unsupported target image format: {format}")
        };
    }