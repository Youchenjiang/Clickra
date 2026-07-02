using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Filters;

namespace Clickra.Core.Processors
{
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

        public static void Optimize(PdfDocument document, PdfCompressionSettings settings, string? inputPath = null)
        {
            if (settings.MinifyContent)
            {
                MinifyPageContentStreams(document);
            }

            if (settings.DeduplicateFonts)
            {
                DeduplicateEmbeddedFontStreams(document);
            }

            if (settings.StripFonts)
            {
                RemoveDisposableMetadata(document);
                UnembedLargeFonts(document);
            }

            if (settings.TargetDpi > 0 && !string.IsNullOrEmpty(inputPath) && File.Exists(inputPath))
            {
                DownsampleAndRecompressImages(document, settings.TargetDpi, settings.JpegQuality, inputPath);
            }
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
                {
                    if (content.Stream == null || !CanRewriteStream(content))
                        continue;

                    byte[] originalBytes = content.Stream.Value;
                    byte[] decodedBytes;
                    try
                    {
                        decodedBytes = content.Stream.UnfilteredValue;
                    }
                    catch
                    {
                        continue;
                    }

                    if (!PdfContentStreamMinifier.TryMinify(decodedBytes, out byte[] minifiedBytes))
                        continue;

                    byte[] encodedBytes = flate.Encode(minifiedBytes, PdfFlateEncodeMode.BestCompression);
                    if (encodedBytes.Length + 16 >= originalBytes.Length)
                        continue;

                    content.Stream.Value = encodedBytes;
                    content.Elements.SetName("/Filter", "/FlateDecode");
                    content.Elements.Remove("/DecodeParms");
                    content.Elements.Remove("/DP");
                }
            }
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

            var canonicalByHash = new Dictionary<string, PdfReference>(StringComparer.Ordinal);
            var duplicateReferences = new HashSet<PdfReference>();

            foreach (FontFileUsage usage in usages)
            {
                if (usage.Reference.Value is not PdfDictionary fontFile || fontFile.Stream == null)
                    continue;

                byte[] bytes;
                try
                {
                    bytes = fontFile.Stream.UnfilteredValue;
                }
                catch
                {
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

            if (duplicateReferences.Count == 0)
                return;

            Dictionary<PdfReference, int> referenceCounts = CountReferences(document);
            foreach (PdfReference duplicateReference in duplicateReferences)
            {
                if (!referenceCounts.ContainsKey(duplicateReference) && duplicateReference.Value != null)
                    document.Internals.RemoveObject(duplicateReference.Value);
            }
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
            if (dictionary.Elements.ContainsKey("/DecodeParms") || dictionary.Elements.ContainsKey("/DP"))
                return false;
            if (dictionary.Elements.ContainsKey("/F") || dictionary.Elements.ContainsKey("/FFilter"))
                return false;

            foreach (string filterName in GetFilterNames(dictionary))
            {
                if (!RewritableFilters.Contains(filterName))
                    return false;
            }

            return true;
        }

        private static IEnumerable<string> GetFilterNames(PdfDictionary dictionary)
        {
            PdfItem? filter = dictionary.Elements["/Filter"];
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
                    if (array.Elements[i] is PdfName arrayName)
                        yield return arrayName.Value;
                    else
                        yield return "__unsupported__";
                }

                yield break;
            }

            yield return "__unsupported__";
        }

        private static void DownsampleAndRecompressImages(PdfDocument document, double targetDpi, int jpegQuality, string inputPath)
        {
            if (targetDpi <= 0)
                return;

            UglyToad.PdfPig.PdfDocument? pigDoc = null;
            try
            {
                pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
            }
            catch
            {
                return;
            }

            try
            {
                for (int pageIdx = 0; pageIdx < document.Pages.Count; pageIdx++)
                {
                    PdfPage page = document.Pages[pageIdx];
                    int pigPageNum = pageIdx + 1;
                    if (pigPageNum > pigDoc.NumberOfPages)
                        continue;

                    UglyToad.PdfPig.Content.Page pigPage;
                    List<UglyToad.PdfPig.Content.IPdfImage> pigImages;
                    try
                    {
                        pigPage = pigDoc.GetPage(pigPageNum);
                        pigImages = pigPage.GetImages().ToList();
                    }
                    catch
                    {
                        continue;
                    }

                    if (pigImages.Count == 0)
                        continue;

                    PdfDictionary? resources = page.Elements.GetDictionary("/Resources");
                    PdfDictionary? xobjects = resources?.Elements.GetDictionary("/XObject");
                    if (xobjects == null)
                        continue;

                    foreach (PdfName name in xobjects.Elements.KeyNames)
                    {
                        PdfReference? r = xobjects.Elements.GetReference(name.Value);
                        if (r?.Value is not PdfDictionary imgDict || imgDict.Elements.GetName("/Subtype") != "/Image")
                            continue;

                        int w = imgDict.Elements.GetInteger("/Width");
                        int h = imgDict.Elements.GetInteger("/Height");
                        if (w <= 0 || h <= 0)
                            continue;

                        // Skip downsampling for low-resolution (< 300,000 pixels) or already small (< 100 KB) images to preserve text/diagram legibility
                        if (w * h < 300000 || imgDict.Stream.Value.Length < 100 * 1024)
                            continue;

                        UglyToad.PdfPig.Content.IPdfImage? matchedPigImage = null;
                        foreach (var pigImg in pigImages)
                        {
                            if (pigImg.WidthInSamples == w && pigImg.HeightInSamples == h)
                            {
                                matchedPigImage = pigImg;
                                break;
                            }
                        }

                        if (matchedPigImage == null)
                        {
                            foreach (var pigImg in pigImages)
                            {
                                if (Math.Abs(pigImg.WidthInSamples - w) <= 2 && Math.Abs(pigImg.HeightInSamples - h) <= 2)
                                {
                                    matchedPigImage = pigImg;
                                    break;
                                }
                            }
                        }

                        if (matchedPigImage == null)
                            continue;

                        double wPt = matchedPigImage.BoundingBox.Width;
                        double hPt = matchedPigImage.BoundingBox.Height;
                        if (wPt <= 0 || hPt <= 0)
                            continue;

                        double dpiX = (w / wPt) * 72.0;
                        double dpiY = (h / hPt) * 72.0;
                        double effDpi = Math.Max(dpiX, dpiY);

                        if (effDpi <= targetDpi)
                            continue;

                        double scale = targetDpi / effDpi;
                        int targetW = (int)Math.Max(1, Math.Round(w * scale));
                        int targetH = (int)Math.Max(1, Math.Round(h * scale));

                        if (targetW >= w || targetH >= h)
                            continue;

                        if (!matchedPigImage.TryGetPng(out byte[]? pngBytes) || pngBytes == null)
                            continue;

                        try
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
                            {
                                byte[] rgbBytes = new byte[targetW * targetH * 3];
                                byte[] alphaBytes = new byte[targetW * targetH];

                                var rect = new System.Drawing.Rectangle(0, 0, targetW, targetH);
                                var bmpData = resizedBmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                int bytesCount = bmpData.Stride * targetH;
                                byte[] argbValues = new byte[bytesCount];
                                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, argbValues, 0, bytesCount);
                                resizedBmp.UnlockBits(bmpData);

                                int rgbIndex = 0;
                                int alphaIndex = 0;
                                for (int y = 0; y < targetH; y++)
                                {
                                    int rowOffset = y * bmpData.Stride;
                                    for (int x = 0; x < targetW; x++)
                                    {
                                        int pixelOffset = rowOffset + (x * 4);
                                        byte blue = argbValues[pixelOffset];
                                        byte green = argbValues[pixelOffset + 1];
                                        byte red = argbValues[pixelOffset + 2];
                                        byte alpha = argbValues[pixelOffset + 3];

                                        rgbBytes[rgbIndex++] = red;
                                        rgbBytes[rgbIndex++] = green;
                                        rgbBytes[rgbIndex++] = blue;

                                        alphaBytes[alphaIndex++] = alpha;
                                    }
                                }

                                using (var rgbBmp = new System.Drawing.Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                                {
                                    var rgbRect = new System.Drawing.Rectangle(0, 0, targetW, targetH);
                                    var rgbBmpData = rgbBmp.LockBits(rgbRect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                                    int rgbStride = rgbBmpData.Stride;
                                    byte[] rgbValues = new byte[rgbStride * targetH];

                                    int rgbValIndex = 0;
                                    for (int y = 0; y < targetH; y++)
                                    {
                                        int rowOffset = y * rgbStride;
                                        for (int x = 0; x < targetW; x++)
                                        {
                                            int pixelOffset = rowOffset + (x * 3);
                                            rgbValues[pixelOffset] = rgbBytes[rgbValIndex + 2];     // B
                                            rgbValues[pixelOffset + 1] = rgbBytes[rgbValIndex + 1]; // G
                                            rgbValues[pixelOffset + 2] = rgbBytes[rgbValIndex];     // R
                                            rgbValIndex += 3;
                                        }
                                    }

                                    System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, rgbBmpData.Scan0, rgbValues.Length);
                                    rgbBmp.UnlockBits(rgbBmpData);

                                    using var msJpg = new MemoryStream();
                                    SaveJpegWithQuality(rgbBmp, msJpg, jpegQuality);
                                    byte[] newJpgBytes = msJpg.ToArray();

                                    if (newJpgBytes.Length < imgDict.Stream.Value.Length)
                                    {
                                        imgDict.Stream.Value = newJpgBytes;
                                        imgDict.Elements.SetInteger("/Width", targetW);
                                        imgDict.Elements.SetInteger("/Height", targetH);
                                        imgDict.Elements.SetName("/Filter", "/DCTDecode");
                                        imgDict.Elements.Remove("/DecodeParms");
                                        imgDict.Elements.Remove("/DP");
                                    }
                                }

                                var flate = new FlateDecode();
                                byte[] encodedMask = flate.Encode(alphaBytes, PdfFlateEncodeMode.BestCompression);
                                if (encodedMask.Length < smaskDict.Stream.Value.Length)
                                {
                                    smaskDict.Stream.Value = encodedMask;
                                    smaskDict.Elements.SetInteger("/Width", targetW);
                                    smaskDict.Elements.SetInteger("/Height", targetH);
                                    smaskDict.Elements.SetName("/Filter", "/FlateDecode");
                                    smaskDict.Elements.SetName("/ColorSpace", "/DeviceGray");
                                    smaskDict.Elements.SetInteger("/BitsPerComponent", 8);
                                    smaskDict.Elements.Remove("/DecodeParms");
                                    smaskDict.Elements.Remove("/DP");
                                }
                            }
                            else
                            {
                                using var msJpg = new MemoryStream();
                                SaveJpegWithQuality(resizedBmp, msJpg, jpegQuality);
                                byte[] newJpgBytes = msJpg.ToArray();

                                if (newJpgBytes.Length < imgDict.Stream.Value.Length)
                                {
                                    imgDict.Stream.Value = newJpgBytes;
                                    imgDict.Elements.SetInteger("/Width", targetW);
                                    imgDict.Elements.SetInteger("/Height", targetH);
                                    imgDict.Elements.SetName("/Filter", "/DCTDecode");
                                    imgDict.Elements.Remove("/DecodeParms");
                                    imgDict.Elements.Remove("/DP");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Warning] Failed to compress image {name.Value}: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                pigDoc?.Dispose();
            }
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
            encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            img.Save(stream, encoder, encoderParameters);
        }

        private static System.Drawing.Imaging.ImageCodecInfo? GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            return System.Drawing.Imaging.ImageCodecInfo.GetImageDecoders()
                .FirstOrDefault(codec => codec.FormatID == format.Guid);
        }

        private static void UnembedLargeFonts(PdfDocument document)
        {
            var objects = document.Internals.GetAllObjects();
            foreach (var obj in objects)
            {
                if (obj is not PdfDictionary dict)
                    continue;

                if (dict.Elements.GetName("/Type") != "/FontDescriptor")
                    continue;

                string[] fontFileKeys = { "/FontFile", "/FontFile2", "/FontFile3" };
                foreach (string key in fontFileKeys)
                {
                    PdfReference? fontFileRef = dict.Elements.GetReference(key);
                    if (fontFileRef != null && fontFileRef.Value is PdfDictionary fontFileDict && fontFileDict.Stream != null)
                    {
                        int streamLen = fontFileDict.Stream.Value.Length;
                        if (streamLen > 100 * 1024)
                        {
                            dict.Elements.Remove(key);
                            document.Internals.RemoveObject(fontFileRef.Value);
                        }
                    }
                }
            }
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
                    while (i < contentBytes.Length && contentBytes[i] != (byte)'\n' && contentBytes[i] != (byte)'\r')
                        i++;
                    pendingSpace = output.Length > 0;
                    continue;
                }

                if (NeedsSpace(previousSignificant, current, pendingSpace))
                    output.WriteByte((byte)' ');
                pendingSpace = false;

                if (current == (byte)'(')
                {
                    CopyLiteralString(contentBytes, output, ref i);
                    previousSignificant = (byte)')';
                    continue;
                }

                if (current == (byte)'<' && (i + 1 >= contentBytes.Length || contentBytes[i + 1] != (byte)'<'))
                {
                    CopyHexString(contentBytes, output, ref i);
                    previousSignificant = (byte)'>';
                    continue;
                }

                output.WriteByte(current);
                previousSignificant = current;
                i++;
            }

            if (output.Length >= contentBytes.Length)
                return false;

            minifiedBytes = output.ToArray();
            return true;
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

        private static bool IsRegularTokenByte(byte value)
        {
            return !IsWhiteSpace(value) && !IsDelimiter(value);
        }

        private static bool IsWhiteSpace(byte value)
        {
            return value == 0 || value == 9 || value == 10 || value == 12 || value == 13 || value == 32;
        }

        private static bool IsDelimiter(byte value)
        {
            return value == (byte)'(' || value == (byte)')' ||
                   value == (byte)'<' || value == (byte)'>' ||
                   value == (byte)'[' || value == (byte)']' ||
                   value == (byte)'{' || value == (byte)'}' ||
                   value == (byte)'/' || value == (byte)'%';
        }
    }
}
