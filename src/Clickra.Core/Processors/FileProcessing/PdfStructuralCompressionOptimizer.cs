using System;
using System.Collections.Generic;
using System.IO;
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

        public static void Optimize(PdfDocument document, PdfCompressionLevel level)
        {
            MinifyPageContentStreams(document);
            DeduplicateEmbeddedFontStreams(document);

            if (level == PdfCompressionLevel.Small)
                RemoveDisposableMetadata(document);
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
