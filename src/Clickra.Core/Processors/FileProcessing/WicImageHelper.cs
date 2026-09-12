using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Clickra.Core.Processors
{
    /// <summary>
    /// Bridges Windows Imaging Component (WIC) and WinRT (Windows.Graphics.Imaging) APIs
    /// with System.Drawing, providing robust encoding and decoding for modern formats
    /// like HEIC/HEIF and WebP that are not natively supported by legacy GDI+.
    /// </summary>
    public static class WicImageHelper
    {
        private const string LanguageSettingKey = "Language";

        /// <summary>
        /// Checks whether the system provides a WIC/WinRT HEIF/HEIC encoder (Microsoft HEIF Encoder).
        /// </summary>
        public static bool IsHeicEncoderAvailable()
        {
            try
            {
                var encoders = BitmapEncoder.GetEncoderInformationEnumerator();
                foreach (var enc in encoders)
                {
                    if (enc.CodecId == BitmapEncoder.HeifEncoderId) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether the system provides a WIC/WinRT HEIF/HEIC decoder (Microsoft HEIF Decoder).
        /// </summary>
        public static bool IsHeicDecoderAvailable()
        {
            try
            {
                var decoders = BitmapDecoder.GetDecoderInformationEnumerator();
                foreach (var dec in decoders)
                {
                    if (dec.CodecId == BitmapDecoder.HeifDecoderId) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely loads an image file into a System.Drawing.Bitmap.
        /// Uses GDI+ for standard formats (PNG/JPG/BMP/GIF) and automatically uses WIC BitmapDecoder
        /// for HEIC/HEIF or when GDI+ fails (e.g. WebP or specialized color profiles).
        /// </summary>
        public static Bitmap LoadImageSafely(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Image file not found.", filePath);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".heic" && ext != ".heif" && ext != ".hif")
            {
                try
                {
                    using var fileStream = File.OpenRead(filePath);
                    using var original = new Bitmap(fileStream);
                    return new Bitmap(original);
                }
                catch
                {
                    // Fall back to WIC if GDI+ failed to parse the image
                }
            }

            return LoadViaWic(filePath);
        }

        /// <summary>
        /// Loads an image using WIC/WinRT BitmapDecoder and converts it into a 32bpp GDI+ Bitmap.
        /// </summary>
        private static Bitmap LoadViaWic(string filePath)
        {
            using var fileStream = File.OpenRead(filePath);
            using var inRas = fileStream.AsRandomAccessStream();

            var decoder = BitmapDecoder.CreateAsync(inRas).AsTask().GetAwaiter().GetResult();
            var pixelData = decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb
            ).AsTask().GetAwaiter().GetResult();

            byte[] pixels = pixelData.DetachPixelData();
            int width = checked((int)decoder.PixelWidth);
            int height = checked((int)decoder.PixelHeight);

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            var bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            return bitmap;
        }

        /// <summary>
        /// Converts an input image file directly to HEIC using WIC/WinRT BitmapEncoder.
        /// </summary>
        public static void ConvertFileToHeic(string inputPath, string outputPath, long quality = 90)
        {
            if (!IsHeicEncoderAvailable())
            {
                throw new NotSupportedException(Localization.T("error_heic_codec_missing", ClickraStorage.GetSetting(LanguageSettingKey)));
            }

            using var fileStream = File.OpenRead(inputPath);
            using var inRas = fileStream.AsRandomAccessStream();

            var decoder = BitmapDecoder.CreateAsync(inRas).AsTask().GetAwaiter().GetResult();
            var pixelData = decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb
            ).AsTask().GetAwaiter().GetResult();

            byte[] pixels = pixelData.DetachPixelData();

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var outStream = File.Create(outputPath);
            using var outRas = outStream.AsRandomAccessStream();

            BitmapEncoder encoder;
            try
            {
                var propertySet = new BitmapPropertySet();
                float q = Math.Clamp(quality / 100f, 0.01f, 1.0f);
                propertySet.Add("ImageQuality", new BitmapTypedValue(q, Windows.Foundation.PropertyType.Single));
                encoder = BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, outRas, propertySet).AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                encoder = BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, outRas).AsTask().GetAwaiter().GetResult();
            }

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                decoder.PixelWidth,
                decoder.PixelHeight,
                decoder.DpiX > 0 ? decoder.DpiX : 96,
                decoder.DpiY > 0 ? decoder.DpiY : 96,
                pixels);

            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Saves a System.Drawing.Image to HEIC format using WIC/WinRT BitmapEncoder.
        /// </summary>
        public static void SaveAsHeic(Image image, string outputPath, long quality = 90)
        {
            if (!IsHeicEncoderAvailable())
            {
                throw new NotSupportedException(Localization.T("error_heic_codec_missing", ClickraStorage.GetSetting(LanguageSettingKey)));
            }

            // Convert System.Drawing.Image to pixel buffer in BGRA8 format
            using var bmp = image is Bitmap b && b.PixelFormat == PixelFormat.Format32bppPArgb
                ? (Bitmap)b.Clone()
                : new Bitmap(image);

            int width = bmp.Width;
            int height = bmp.Height;

            var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            byte[] pixels = new byte[width * height * 4];
            try
            {
                Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var outStream = File.Create(outputPath);
            using var outRas = outStream.AsRandomAccessStream();

            BitmapEncoder encoder;
            try
            {
                var propertySet = new BitmapPropertySet();
                float q = Math.Clamp(quality / 100f, 0.01f, 1.0f);
                propertySet.Add("ImageQuality", new BitmapTypedValue(q, Windows.Foundation.PropertyType.Single));
                encoder = BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, outRas, propertySet).AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                encoder = BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, outRas).AsTask().GetAwaiter().GetResult();
            }

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)width,
                (uint)height,
                image.HorizontalResolution > 0 ? image.HorizontalResolution : 96,
                image.VerticalResolution > 0 ? image.VerticalResolution : 96,
                pixels);

            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
