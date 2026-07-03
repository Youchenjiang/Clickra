using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Filters;

namespace Clickra.Core.Processors;

internal static class PdfStructuralCompressionOptimizer
{
        private static readonly string[] FontFileKeys = { "/FontFile", "/FontFile2", "/FontFile3" };
        private static readonly HashSet<string> RewritableFilters = new(StringComparer.Ordinal)
        {
            "",
            "/Fl",
            "/FlateDecode",
            "/LZW",
            "/LZWDecode"
        };

        // PDF dictionary key constants
        private const string KeyFilter = "/Filter";
        private const string KeyDecodeParms = "/DecodeParms";
        private const string KeyWidth = "/Width";
        private const string KeyHeight = "/Height";
        private const string KeyDecompressAlias = "/DP";

        public static void Optimize(PdfDocument document, PdfCompressionSettings settings, string? inputPath = null)
        {
            if (settings.MinifyContent)
                MinifyPageContentStreams(document);

            if (settings.DeduplicateFonts)
                DeduplicateEmbeddedFontStreams(document);

            if (settings.StripFonts)
            {
                RemoveDisposableMetadata(document);
                UnembedLargeFonts(document);
            }

            if (settings.TargetDpi > 0 && !string.IsNullOrEmpty(inputPath) && File.Exists(inputPath))
                DownsampleAndRecompressImages(document, settings.TargetDpi, settings.JpegQuality, inputPath);
        }

        public static void Optimize(PdfDocument document, PdfCompressionLevel level, string? inputPath = null)
        {
            var settings = PdfCompressionSettings.Parse(new Dictionary<string, object> { { "level", level.ToString().ToLowerInvariant() } });
            Optimize(document, settings, inputPath);
        }

        private static void MinifyPageContentStreams(PdfDocument document)
        {
            var flate = new FlateDecode();
            foreach (PdfPage page in document.Pages)
            {
                foreach (PdfDictionary content in GetPageContentStreams(page))
                    TryMinifyContentStream(content, flate);
            }
        }

        private static void TryMinifyContentStream(PdfDictionary content, FlateDecode flate)
        {
            if (content.Stream == null || !CanRewriteStream(content))
                return;

            byte[] originalBytes = content.Stream.Value;
            byte[] decodedBytes = Array.Empty<byte>();
            try
            {
                decodedBytes = content.Stream.UnfilteredValue;
            }
            catch
            {
                // Stream may use unsupported filters; skip silently
                return;
            }

            if (!PdfContentStreamMinifier.TryMinify(decodedBytes, out byte[] minifiedBytes))
                return;

            byte[] encodedBytes = flate.Encode(minifiedBytes, PdfFlateEncodeMode.BestCompression);
            if (encodedBytes.Length + 16 >= originalBytes.Length)
                return;

            content.Stream.Value = encodedBytes;
            content.Elements.SetName(KeyFilter, "/FlateDecode");
            content.Elements.Remove(KeyDecodeParms);
            content.Elements.Remove(KeyDecompressAlias);
        }

        private static IEnumerable<PdfDictionary> GetPageContentStreams(PdfPage page)
        {
            for (int i = 0; i < page.Contents.Elements.Count; i++)
            {
                PdfItem item = page.Contents.Elements[i];
                if (item is PdfReference reference)
                    item = reference.Value;
                if (item is PdfDictionary { Stream: not null } dictionary)
                    yield return dictionary;
            }
        }

        private static void DeduplicateEmbeddedFontStreams(PdfDocument document)
        {
            var usages = CollectFontFileUsages(document);
            if (usages.Count < 2)
                return;

            var duplicateReferences = BuildCanonicalFontMap(usages);
            if (duplicateReferences.Count == 0)
                return;

            Dictionary<PdfReference, int> referenceCounts = CountReferences(document);
            foreach (PdfReference duplicateReference in duplicateReferences)
            {
                if (!referenceCounts.ContainsKey(duplicateReference) && duplicateReference.Value != null)
                    document.Internals.RemoveObject(duplicateReference.Value);
            }
        }

        private static HashSet<PdfReference> BuildCanonicalFontMap(List<FontFileUsage> usages)
        {
            var canonicalByHash = new Dictionary<string, PdfReference>(StringComparer.Ordinal);
            var duplicateReferences = new HashSet<PdfReference>();

            foreach (FontFileUsage usage in usages)
            {
                if (usage.Reference.Value is not PdfDictionary fontFile || fontFile.Stream == null)
                    continue;

                byte[] bytes = Array.Empty<byte>();
                try
                {
                    bytes = fontFile.Stream.UnfilteredValue;
                }
                catch
                {
                    // Stream may use unsupported filter; skip deduplication for this entry
                    continue;
                }

                string hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!canonicalByHash.TryGetValue(hash, out PdfReference? canonicalReference))
                {
                    canonicalByHash[hash] = usage.Reference;
                    continue;
                }

                if (ReferenceEquals(canonicalReference, usage.Reference))
                    continue;

                usage.Owner.Elements.SetReference(usage.Key, canonicalReference);
                duplicateReferences.Add(usage.Reference);
            }

            return duplicateReferences;
        }

        private static List<FontFileUsage> CollectFontFileUsages(PdfDocument document)
        {
            var usages = new List<FontFileUsage>();
            foreach (PdfObject obj in document.Internals.GetAllObjects())
            {
                if (obj is not PdfDictionary dictionary)
                    continue;

                foreach (string key in FontFileKeys)
                {
                    PdfReference? reference = dictionary.Elements.GetReference(key);
                    if (reference?.Value is PdfDictionary { Stream: not null })
                        usages.Add(new FontFileUsage(dictionary, key, reference));
                }
            }

            return usages;
        }

        private static Dictionary<PdfReference, int> CountReferences(PdfDocument document)
        {
            var counts = new Dictionary<PdfReference, int>();
            foreach (PdfObject obj in document.Internals.GetAllObjects())
                CountReferences(obj, counts, new HashSet<PdfItem>());
            return counts;
        }

        private static void CountReferences(PdfItem? item, Dictionary<PdfReference, int> counts, HashSet<PdfItem> visited)
        {
            if (item == null)
                return;
            if (!visited.Add(item))
                return;

            if (item is PdfReference reference)
            {
                counts.TryGetValue(reference, out int count);
                counts[reference] = count + 1;
                return;
            }

            if (item is PdfDictionary dictionary)
            {
                foreach (PdfName key in dictionary.Elements.KeyNames)
                    CountReferences(dictionary.Elements[key], counts, visited);
                return;
            }

            if (item is PdfArray array)
            {
                for (int i = 0; i < array.Elements.Count; i++)
                    CountReferences(array.Elements[i], counts, visited);
            }
        }

        private static void RemoveDisposableMetadata(PdfDocument document)
        {
            RemoveOptionalMetadata(document.Internals.Catalog);
            foreach (PdfPage page in document.Pages)
                RemoveOptionalMetadata(page);
        }

        private static void RemoveOptionalMetadata(PdfDictionary dictionary)
        {
            dictionary.Elements.Remove("/Metadata");
            dictionary.Elements.Remove("/PieceInfo");
            dictionary.Elements.Remove("/Thumb");
        }

        private static bool CanRewriteStream(PdfDictionary dictionary)
        {
            if (dictionary.Elements.ContainsKey(KeyDecodeParms) || dictionary.Elements.ContainsKey(KeyDecompressAlias))
                return false;
            if (dictionary.Elements.ContainsKey("/F") || dictionary.Elements.ContainsKey("/FFilter"))
                return false;

            return GetFilterNames(dictionary).All(RewritableFilters.Contains);
        }

        private static IEnumerable<string> GetFilterNames(PdfDictionary dictionary)
        {
            PdfItem? filter = dictionary.Elements[KeyFilter];
            if (filter == null)
            {
                yield return "";
                yield break;
            }

            if (filter is PdfName name)
            {
                yield return name.Value;
                yield break;
            }

            if (filter is PdfArray array)
            {
                for (int i = 0; i < array.Elements.Count; i++)
                {
                    yield return array.Elements[i] is PdfName arrayName
                        ? arrayName.Value
                        : "__unsupported__";
                }

                yield break;
            }

            yield return "__unsupported__";
        }

        // ─── Image Downsampling ───────────────────────────────────────────────────

        private static void DownsampleAndRecompressImages(PdfDocument document, double targetDpi, int jpegQuality, string inputPath)
        {
            if (targetDpi <= 0)
                return;

            UglyToad.PdfPig.PdfDocument? pigDoc = null;
            try
            {
                pigDoc = OpenPigDocument(inputPath);
                if (pigDoc == null)
                    return;

                for (int pageIdx = 0; pageIdx < document.Pages.Count; pageIdx++)
                {
                    PdfPage page = document.Pages[pageIdx];
                    int pigPageNum = pageIdx + 1;
                    if (pigPageNum > pigDoc.NumberOfPages)
                        continue;

                    if (!TryGetPigPageImages(pigDoc, pigPageNum, out var pigImages))
                        continue;

                    if (pigImages.Count == 0)
                        continue;

                    ProcessPageImages(document, page, pigImages, targetDpi, jpegQuality);
                }
            }
            finally
            {
                pigDoc?.Dispose();
            }
        }

        private static UglyToad.PdfPig.PdfDocument? OpenPigDocument(string inputPath)
        {
            try
            {
                return UglyToad.PdfPig.PdfDocument.Open(inputPath);
            }
            catch
            {
                // Cannot open with PdfPig; skip image downsampling
                return null;
            }
        }

        private static bool TryGetPigPageImages(UglyToad.PdfPig.PdfDocument pigDoc, int pigPageNum,
            out List<UglyToad.PdfPig.Content.IPdfImage> pigImages)
        {
            try
            {
                var pigPage = pigDoc.GetPage(pigPageNum);
                pigImages = pigPage.GetImages().ToList();
                return true;
            }
            catch
            {
                // Page may be corrupt or use unsupported features; skip
                pigImages = new List<UglyToad.PdfPig.Content.IPdfImage>();
                return false;
            }
        }

        private static void ProcessPageImages(PdfDocument document, PdfPage page,
            List<UglyToad.PdfPig.Content.IPdfImage> pigImages, double targetDpi, int jpegQuality)
        {
            PdfDictionary? resources = page.Elements.GetDictionary("/Resources");
            PdfDictionary? xobjects = resources?.Elements.GetDictionary("/XObject");
            if (xobjects == null)
                return;

            foreach (string xName in xobjects.Elements.KeyNames.Select(n => n.Value))
            {
                PdfReference? r = xobjects.Elements.GetReference(xName);
                if (r?.Value is not PdfDictionary imgDict || imgDict.Elements.GetName("/Subtype") != "/Image")
                    continue;

                TryDownsampleImage(imgDict, pigImages, targetDpi, jpegQuality, xName);
            }
        }

        private static void TryDownsampleImage(PdfDictionary imgDict,
            List<UglyToad.PdfPig.Content.IPdfImage> pigImages, double targetDpi, int jpegQuality, string xName)
        {
            if (imgDict.Stream == null || imgDict.Stream.Value == null)
                return;

            int w = imgDict.Elements.GetInteger(KeyWidth);
            int h = imgDict.Elements.GetInteger(KeyHeight);
            if (w <= 0 || h <= 0)
                return;

            // Skip low-resolution or already-small images to preserve legibility
            if ((long)w * h < 300000 || imgDict.Stream.Value.Length < 100 * 1024)
                return;

            var matchedPigImage = FindMatchingPigImage(pigImages, w, h);
            if (matchedPigImage == null)
                return;

            if (!ComputeDownsampleTarget(matchedPigImage, w, h, targetDpi, out int targetW, out int targetH))
                return;

            if (!matchedPigImage.TryGetPng(out byte[]? pngBytes))
                return;

            try
            {
                ApplyDownsample(imgDict, pngBytes!, targetW, targetH, jpegQuality);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to compress image {xName}: {ex.Message}");
            }
        }

        private static UglyToad.PdfPig.Content.IPdfImage? FindMatchingPigImage(
            List<UglyToad.PdfPig.Content.IPdfImage> pigImages, int w, int h)
        {
            // Exact match first
            var exact = pigImages.FirstOrDefault(p => p.WidthInSamples == w && p.HeightInSamples == h);
            if (exact != null)
                return exact;

            // Near match (±2 px tolerance for sub-pixel rounding)
            return pigImages.FirstOrDefault(p =>
                Math.Abs(p.WidthInSamples - w) <= 2 && Math.Abs(p.HeightInSamples - h) <= 2);
        }

        private static bool ComputeDownsampleTarget(
            UglyToad.PdfPig.Content.IPdfImage pigImage, int w, int h, double targetDpi,
            out int targetW, out int targetH)
        {
            targetW = 0;
            targetH = 0;

            double wPt = pigImage.BoundingBox.Width;
            double hPt = pigImage.BoundingBox.Height;
            if (wPt <= 0 || hPt <= 0)
                return false;

            double dpiX = (w / wPt) * 72.0;
            double dpiY = (h / hPt) * 72.0;
            double effDpi = Math.Max(dpiX, dpiY);

            if (effDpi <= targetDpi)
                return false;

            double scale = targetDpi / effDpi;
            targetW = (int)Math.Max(1, Math.Round(w * scale));
            targetH = (int)Math.Max(1, Math.Round(h * scale));

            return targetW < w && targetH < h;
        }

        private static void ApplyDownsample(PdfDictionary imgDict,
            byte[] pngBytes, int targetW, int targetH, int jpegQuality)
        {
            using var msInput = new MemoryStream(pngBytes);
            using var originalBmp = new System.Drawing.Bitmap(msInput);
            using var resizedBmp = new System.Drawing.Bitmap(targetW, targetH);

            using (var g = System.Drawing.Graphics.FromImage(resizedBmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(originalBmp, 0, 0, targetW, targetH);
            }

            PdfReference? smaskRef = imgDict.Elements.GetReference("/SMask");
            if (smaskRef != null && smaskRef.Value is PdfDictionary smaskDict)
                RewriteImageWithAlpha(imgDict, smaskDict, resizedBmp, targetW, targetH, jpegQuality);
            else
                RewriteImageSimple(imgDict, resizedBmp, targetW, targetH, jpegQuality);
        }

        private static void RewriteImageWithAlpha(PdfDictionary imgDict,
            PdfDictionary smaskDict, System.Drawing.Bitmap resizedBmp,
            int targetW, int targetH, int jpegQuality)
        {
            SplitArgbChannels(resizedBmp, targetW, targetH, out byte[] rgbBytes, out byte[] alphaBytes);

            using var rgbBmp = BuildRgbBitmap(rgbBytes, targetW, targetH);
            using var msJpg = new MemoryStream();
            SaveJpegWithQuality(rgbBmp, msJpg, jpegQuality);
            byte[] newJpgBytes = msJpg.ToArray();

            if (newJpgBytes.Length < imgDict.Stream.Value.Length)
            {
                imgDict.Stream.Value = newJpgBytes;
                imgDict.Elements.SetInteger(KeyWidth, targetW);
                imgDict.Elements.SetInteger(KeyHeight, targetH);
                imgDict.Elements.SetName(KeyFilter, "/DCTDecode");
                imgDict.Elements.Remove(KeyDecodeParms);
                imgDict.Elements.Remove(KeyDecompressAlias);
            }

            var flate = new FlateDecode();
            byte[] encodedMask = flate.Encode(alphaBytes, PdfFlateEncodeMode.BestCompression);
            if (encodedMask.Length < smaskDict.Stream.Value.Length)
            {
                smaskDict.Stream.Value = encodedMask;
                smaskDict.Elements.SetInteger(KeyWidth, targetW);
                smaskDict.Elements.SetInteger(KeyHeight, targetH);
                smaskDict.Elements.SetName(KeyFilter, "/FlateDecode");
                smaskDict.Elements.SetName("/ColorSpace", "/DeviceGray");
                smaskDict.Elements.SetInteger("/BitsPerComponent", 8);
                smaskDict.Elements.Remove(KeyDecodeParms);
                smaskDict.Elements.Remove(KeyDecompressAlias);
            }
        }

        private static void RewriteImageSimple(PdfDictionary imgDict,
            System.Drawing.Bitmap resizedBmp, int targetW, int targetH, int jpegQuality)
        {
            using var msJpg = new MemoryStream();
            SaveJpegWithQuality(resizedBmp, msJpg, jpegQuality);
            byte[] newJpgBytes = msJpg.ToArray();

            if (newJpgBytes.Length < imgDict.Stream.Value.Length)
            {
                imgDict.Stream.Value = newJpgBytes;
                imgDict.Elements.SetInteger(KeyWidth, targetW);
                imgDict.Elements.SetInteger(KeyHeight, targetH);
                imgDict.Elements.SetName(KeyFilter, "/DCTDecode");
                imgDict.Elements.Remove(KeyDecodeParms);
                imgDict.Elements.Remove(KeyDecompressAlias);
            }
        }

        private static void SplitArgbChannels(System.Drawing.Bitmap bmp, int targetW, int targetH,
            out byte[] rgbBytes, out byte[] alphaBytes)
        {
            rgbBytes = new byte[targetW * targetH * 3];
            alphaBytes = new byte[targetW * targetH];

            var rect = new System.Drawing.Rectangle(0, 0, targetW, targetH);
            var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int bytesCount = bmpData.Stride * targetH;
            byte[] argbValues = new byte[bytesCount];
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, argbValues, 0, bytesCount);
            bmp.UnlockBits(bmpData);

            int rgbIndex = 0;
            int alphaIndex = 0;
            for (int y = 0; y < targetH; y++)
            {
                int rowOffset = y * bmpData.Stride;
                for (int x = 0; x < targetW; x++)
                {
                    int pixelOffset = rowOffset + (x * 4);
                    rgbBytes[rgbIndex++] = argbValues[pixelOffset + 2]; // R
                    rgbBytes[rgbIndex++] = argbValues[pixelOffset + 1]; // G
                    rgbBytes[rgbIndex++] = argbValues[pixelOffset];     // B
                    alphaBytes[alphaIndex++] = argbValues[pixelOffset + 3];
                }
            }
        }

        private static System.Drawing.Bitmap BuildRgbBitmap(byte[] rgbBytes, int targetW, int targetH)
        {
            var rgbBmp = new System.Drawing.Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var rgbRect = new System.Drawing.Rectangle(0, 0, targetW, targetH);
            var rgbBmpData = rgbBmp.LockBits(rgbRect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            int rgbStride = rgbBmpData.Stride;
            byte[] rgbValues = new byte[rgbStride * targetH];

            int srcIndex = 0;
            for (int y = 0; y < targetH; y++)
            {
                int rowOffset = y * rgbStride;
                for (int x = 0; x < targetW; x++)
                {
                    int pixelOffset = rowOffset + (x * 3);
                    rgbValues[pixelOffset]     = rgbBytes[srcIndex + 2]; // B
                    rgbValues[pixelOffset + 1] = rgbBytes[srcIndex + 1]; // G
                    rgbValues[pixelOffset + 2] = rgbBytes[srcIndex];     // R
                    srcIndex += 3;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, rgbBmpData.Scan0, rgbValues.Length);
            rgbBmp.UnlockBits(rgbBmpData);
            return rgbBmp;
        }

        private static void SaveJpegWithQuality(System.Drawing.Image img, Stream stream, int quality)
        {
            var encoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);
            if (encoder == null)
            {
                img.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
                return;
            }
            var encoderParameters = new System.Drawing.Imaging.EncoderParameters(1);
            encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, quality);
            img.Save(stream, encoder, encoderParameters);
        }

        private static System.Drawing.Imaging.ImageCodecInfo? GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            return System.Drawing.Imaging.ImageCodecInfo.GetImageDecoders()
                .FirstOrDefault(codec => codec.FormatID == format.Guid);
        }

        // ─── Font Unembedding ─────────────────────────────────────────────────────

        private static void UnembedLargeFonts(PdfDocument document)
        {
            var objects = document.Internals.GetAllObjects();
            foreach (var obj in objects)
            {
                if (obj is not PdfDictionary dict)
                    continue;

                if (dict.Elements.GetName("/Type") != "/FontDescriptor")
                    continue;

                foreach (string key in FontFileKeys)
                    TryRemoveFontFileIfLarge(document, dict, key);
            }
        }

        private static void TryRemoveFontFileIfLarge(PdfDocument document, PdfDictionary dict, string key)
        {
            PdfReference? fontFileRef = dict.Elements.GetReference(key);
            if (fontFileRef == null)
                return;

            if (fontFileRef.Value is not PdfDictionary fontFileDict || fontFileDict.Stream == null)
                return;

            int streamLen = fontFileDict.Stream.Value.Length;
            if (streamLen <= 100 * 1024)
                return;

            dict.Elements.Remove(key);

            // Recount after each removal so shared streams are only deleted when truly unreferenced
            var currentCounts = CountReferences(document);
            currentCounts.TryGetValue(fontFileRef, out int count);
            if (count <= 0 && fontFileRef.Value != null)
                document.Internals.RemoveObject(fontFileRef.Value);
        }

        private readonly record struct FontFileUsage(PdfDictionary Owner, string Key, PdfReference Reference);
    }

    internal static class PdfContentStreamMinifier
    {
        public static bool TryMinify(byte[] contentBytes, out byte[] minifiedBytes)
        {
            minifiedBytes = contentBytes;
            if (ContainsInlineImage(contentBytes))
                return false;

            using var output = new MemoryStream(contentBytes.Length);
            bool pendingSpace = false;
            byte previousSignificant = 0;

            for (int i = 0; i < contentBytes.Length;)
            {
                byte current = contentBytes[i];

                if (IsWhiteSpace(current))
                {
                    pendingSpace = output.Length > 0;
                    i++;
                    continue;
                }

                if (current == (byte)'%')
                {
                    i = SkipComment(contentBytes, i);
                    pendingSpace = output.Length > 0;
                    continue;
                }

                if (NeedsSpace(previousSignificant, current, pendingSpace))
                    output.WriteByte((byte)' ');
                pendingSpace = false;

                ProcessToken(contentBytes, output, ref i, current, ref previousSignificant);
            }

            if (output.Length >= contentBytes.Length)
                return false;

            minifiedBytes = output.ToArray();
            return true;
        }

        private static int SkipComment(byte[] bytes, int index)
        {
            while (index < bytes.Length && bytes[index] != (byte)'\n' && bytes[index] != (byte)'\r')
                index++;
            return index;
        }

        private static void ProcessToken(byte[] contentBytes, Stream output, ref int i, byte current, ref byte previousSignificant)
        {
            if (current == (byte)'(')
            {
                CopyLiteralString(contentBytes, output, ref i);
                previousSignificant = (byte)')';
            }
            else if (current == (byte)'<' && (i + 1 >= contentBytes.Length || contentBytes[i + 1] != (byte)'<'))
            {
                CopyHexString(contentBytes, output, ref i);
                previousSignificant = (byte)'>';
            }
            else
            {
                output.WriteByte(current);
                previousSignificant = current;
                i++;
            }
        }

        private static bool ContainsInlineImage(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length;)
            {
                byte current = bytes[i];
                if (IsWhiteSpace(current) || IsDelimiter(current))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < bytes.Length && !IsWhiteSpace(bytes[i]) && !IsDelimiter(bytes[i]))
                    i++;

                if (i - start == 2 && bytes[start] == (byte)'B' && bytes[start + 1] == (byte)'I')
                    return true;
            }

            return false;
        }

        private static void CopyLiteralString(byte[] bytes, Stream output, ref int index)
        {
            int depth = 0;
            bool escaped = false;
            while (index < bytes.Length)
            {
                byte current = bytes[index++];
                output.WriteByte(current);

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == (byte)'\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == (byte)'(')
                    depth++;
                else if (current == (byte)')')
                {
                    depth--;
                    if (depth <= 0)
                        break;
                }
            }
        }

        private static void CopyHexString(byte[] bytes, Stream output, ref int index)
        {
            while (index < bytes.Length)
            {
                byte current = bytes[index++];
                output.WriteByte(current);
                if (current == (byte)'>')
                    break;
            }
        }

        private static bool NeedsSpace(byte previous, byte current, bool pendingSpace)
        {
            return pendingSpace &&
                   previous != 0 &&
                   IsRegularTokenByte(previous) &&
                   IsRegularTokenByte(current);
        }

        private static bool IsRegularTokenByte(byte value) => !IsWhiteSpace(value) && !IsDelimiter(value);

        private static bool IsWhiteSpace(byte value)
            => value == 0 || value == 9 || value == 10 || value == 12 || value == 13 || value == 32;

        private static bool IsDelimiter(byte value)
            => value == (byte)'(' || value == (byte)')' ||
               value == (byte)'<' || value == (byte)'>' ||
               value == (byte)'[' || value == (byte)']' ||
               value == (byte)'{' || value == (byte)'}' ||
               value == (byte)'/' || value == (byte)'%';
    }
