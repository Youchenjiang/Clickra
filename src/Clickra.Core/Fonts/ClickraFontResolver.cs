using System;
using System.IO;
using System.Reflection;
using PdfSharp.Fonts;

namespace Clickra.Core
{
    /// <summary>Resolves Latin and CJK font faces for PDF generation.</summary>
    public class ClickraFontResolver : IFontResolver
    {
        /// <summary>Default fallback font family.</summary>
        public string DefaultFontName => "Arial";

        /// <summary>Maps a template font family to a supported canonical font name.</summary>
        public static bool TryGetCanonicalTemplateFamily(string familyName, out string canonicalFamily)
        {
            canonicalFamily = familyName.Trim() switch
            {
                string value when value.Equals("Arial", StringComparison.OrdinalIgnoreCase) => "Arial",
                string value when value.Equals("Calibri", StringComparison.OrdinalIgnoreCase) => "Calibri",
                string value when value.Equals("Cambria", StringComparison.OrdinalIgnoreCase) => "Cambria",
                string value when value.Equals("Constantia", StringComparison.OrdinalIgnoreCase) => "Constantia",
                string value when value.Equals("Consolas", StringComparison.OrdinalIgnoreCase) => "Consolas",
                string value when value.Equals("Courier New", StringComparison.OrdinalIgnoreCase) => "Courier New",
                string value when value.Equals("KaiU", StringComparison.OrdinalIgnoreCase) => "KaiU",
                string value when value.Equals("Malgun Gothic", StringComparison.OrdinalIgnoreCase) => "Malgun Gothic",
                string value when value.Equals("Microsoft JhengHei", StringComparison.OrdinalIgnoreCase) => "Microsoft JhengHei",
                string value when value.Equals("MS Gothic", StringComparison.OrdinalIgnoreCase) => "MS Gothic",
                string value when value.Equals("Noto Sans TC", StringComparison.OrdinalIgnoreCase) => "Noto Sans TC",
                string value when value.Equals("PMingLiU", StringComparison.OrdinalIgnoreCase) => "PMingLiU",
                string value when value.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase) => "Segoe UI",
                string value when value.Equals("Segoe UI Symbol", StringComparison.OrdinalIgnoreCase) => "Segoe UI Symbol",
                string value when value.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase) => "Times New Roman",
                _ => string.Empty
            };
            return canonicalFamily.Length > 0;
        }

        /// <summary>Resolves the requested family and emphasis to an available PDF font face.</summary>
        // skipcq: CS-R1140 — ordered font aliases must preserve family precedence and style fallback semantics.
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string suffix = "";
            if (isBold && isItalic) suffix = "|bi";
            else if (isBold) suffix = "|b";
            else if (isItalic) suffix = "|i";

            if (string.IsNullOrEmpty(familyName))
            {
                return new FontResolverInfo("arial" + suffix);
            }

            string name = familyName.ToLowerInvariant().Trim();

            // Map family names to unique faces
            if (name.Contains("dfkai") || name.Contains("kaiu") ||
                name.Contains("標楷") || name.Contains("标楷"))
            {
                // CJK translation output must use the real regular KaiU face.
                // Bold is simulated by the renderer so no unsupported CJK
                // bold face (or tofu-producing fallback) is embedded.
                return new FontResolverInfo("kaiu");
            }
            if (name.Contains("jhenghei") || name.Contains("正黑"))
            {
                string face = isBold ? "msjh|b" : "msjh";
                return new FontResolverInfo(face, false, isItalic);
            }
            if (name.Contains("yahei") || name.Contains("雅黑"))
            {
                return new FontResolverInfo("kaiu");
            }
            if (name.Contains("noto sans tc"))
            {
                return new FontResolverInfo("notosanstc");
            }
            if (name.Contains("pmingliu") || name.Contains("新細明"))
            {
                return new FontResolverInfo("pmingliu", isBold, isItalic);
            }
            if (name.Contains("malgun"))
            {
                return new FontResolverInfo("malgun" + suffix);
            }
            if (name.Contains("ms gothic") || name.Contains("msgothic"))
            {
                return new FontResolverInfo("msgothic" + suffix);
            }
            if (name.Contains("cambria"))
            {
                return new FontResolverInfo("cambria" + suffix);
            }
            if (name.Contains("calibri"))
            {
                return new FontResolverInfo("calibri" + suffix);
            }
            if (name.Contains("constantia"))
            {
                return new FontResolverInfo("constantia" + suffix);
            }
            if (name.Contains("times"))
            {
                return new FontResolverInfo("times" + suffix);
            }
            if (name.Contains("segoe ui symbol") || name.Contains("symbol") || name.Contains("math") || name.Contains("cmsy") || name.Contains("msam") || name.Contains("msbm"))
            {
                return new FontResolverInfo("seguisym");
            }
            if (name.Contains("consolas"))
            {
                return new FontResolverInfo("consolas" + suffix);
            }
            if (name.Contains("courier") || name.Contains("mono") || name.Contains("nimbusmon") || name.Contains("monl") || name.Contains("cmtt"))
            {
                return new FontResolverInfo("courier" + suffix);
            }
            if (name.Contains("segoe"))
            {
                return new FontResolverInfo("segoeui" + suffix);
            }
            if (name.Contains("arial"))
            {
                return new FontResolverInfo("arial" + suffix);
            }

            // Fallback
            return new FontResolverInfo("arial" + suffix);
        }

        /// <summary>Returns embeddable font data for the specified resolved face.</summary>
        public byte[]? GetFont(string faceName)
        {
            string[] parts = faceName.Split('|');
            string baseFace = parts[0];
            string style = parts.Length > 1 ? parts[1] : "";

            string fontPath = GetFontPath(baseFace, style);
            if (File.Exists(fontPath))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(fontPath);
                    int ttcFaceIndex = baseFace == "pmingliu" ? 1 : 0;
                    return Path.GetExtension(fontPath).Equals(".ttc", StringComparison.OrdinalIgnoreCase)
                        ? ExtractTtcFace(bytes, ttcFaceIndex)
                        : bytes;
                }
                catch
                {
                    // Fall through to the known-safe font fallbacks below.
                }
            }

            // Fallback for CJK faces if the file is missing
            if (baseFace == "notosanstc")
            {
                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string winFonts = Path.Combine(systemDir, "..", "Fonts");
                string malgunPath = Path.Combine(winFonts, "malgun.ttf");
                if (File.Exists(malgunPath))
                {
                    try { return File.ReadAllBytes(malgunPath); }
                    catch
                    {
                        // Continue through the remaining CJK fallback chain.
                    }
                }
            }

            if (baseFace == "notosanstc" || baseFace == "kaiu" || baseFace == "msjh" || baseFace == "msgothic" ||
                baseFace == "msyh" || baseFace == "malgun")
            {
                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string winFonts = Path.Combine(systemDir, "..", "Fonts");
                string kaiuPath = Path.Combine(winFonts, "kaiu.ttf");
                if (File.Exists(kaiuPath))
                {
                    try { return File.ReadAllBytes(kaiuPath); }
                    catch
                    {
                        // Continue through the remaining CJK fallback chain.
                    }
                }
                string simsunbPath = Path.Combine(winFonts, "simsunb.ttf");
                if (File.Exists(simsunbPath))
                {
                    try { return File.ReadAllBytes(simsunbPath); }
                    catch
                    {
                        // Continue through the remaining CJK fallback chain.
                    }
                }
            }

            // Style fallback
            if (!string.IsNullOrEmpty(style))
            {
                string regularPath = GetFontPath(baseFace, "");
                if (File.Exists(regularPath))
                {
                    try { return File.ReadAllBytes(regularPath); } catch { }
                }
            }

            // Arial fallback
            string systemDir2 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string fallbackPath = Path.Combine(systemDir2, "..", "Fonts", "arial.ttf");
            if (File.Exists(fallbackPath))
            {
                try { return File.ReadAllBytes(fallbackPath); } catch { }
            }

            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "Explicit family/style mapping mirrors Windows font filenames and keeps fallback behavior auditable.")]
        // skipcq: CS-R1140 — the explicit family/style mapping mirrors installed Windows font filenames.
        private string GetFontPath(string baseFace, string style)
        {
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string winFonts = Path.Combine(systemDir, "..", "Fonts");
            string file = baseFace switch
            {
                "kaiu" => "kaiu.ttf",
                "notosanstc" => "NotoSansTC-VF.ttf",
                "msjh" => style switch
                {
                    "b" or "bi" => "msjhbd.ttc",
                    _ => "msjh.ttc"
                },
                "pmingliu" => "mingliu.ttc",
                "msyh" => "msyh.ttc", // Use standard Windows Microsoft YaHei TTC
                "msgothic" => "msgothic.ttc", // Use standard Windows MS Gothic TTC
                "malgun" => style switch // Malgun Gothic (Korean)
                {
                    "b" => "malgunbd.ttf",
                    _ => "malgun.ttf"
                },
                "courier" => style switch
                {
                    "b" => "courbd.ttf",
                    "i" => "couri.ttf",
                    "bi" => "courbi.ttf",
                    _ => "cour.ttf"
                },
                "consolas" => style switch
                {
                    "b" => "consolab.ttf",
                    "i" => "consolai.ttf",
                    "bi" => "consolaz.ttf",
                    _ => "consola.ttf"
                },
                "seguisym" => "seguisym.ttf",
                "cambria" => style switch
                {
                    "b" => "cambriab.ttf",
                    "i" => "cambriai.ttf",
                    "bi" => "cambriaz.ttf",
                    _ => "cambria.ttc"
                },
                "calibri" => style switch
                {
                    "b" => "calibrib.ttf",
                    "i" => "calibrii.ttf",
                    "bi" => "calibriz.ttf",
                    _ => "calibri.ttf"
                },
                "constantia" => style switch
                {
                    "b" => "constanb.ttf",
                    "i" => "constani.ttf",
                    "bi" => "constanz.ttf",
                    _ => "constan.ttf"
                },
                "times" => style switch
                {
                    "b" => "timesbd.ttf",
                    "i" => "timesi.ttf",
                    "bi" => "timesbi.ttf",
                    _ => "times.ttf"
                },
                "segoeui" => style switch
                {
                    "b" => "segoeuib.ttf",
                    "i" => "segoeuii.ttf",
                    "bi" => "segoeuiz.ttf",
                    _ => "segoeui.ttf"
                },
                _ => style switch // arial
                {
                    "b" => "arialbd.ttf",
                    "i" => "ariali.ttf",
                    "bi" => "arialbi.ttf",
                    _ => "arial.ttf"
                }
            };

            return Path.Combine(winFonts, file);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "TTC parsing validates each binary table boundary explicitly before reconstructing a standalone font.")]
        private static byte[] ExtractTtcFace(byte[] collection, int faceIndex)
        {
            if (collection.Length < 16 || collection[0] != (byte)'t' || collection[1] != (byte)'t' ||
                collection[2] != (byte)'c' || collection[3] != (byte)'f') return collection;

            int faceCount = checked((int)ReadUInt32(collection, 8));
            if (faceIndex < 0 || faceIndex >= faceCount) throw new InvalidDataException("Invalid TTC face index.");
            int faceOffset = checked((int)ReadUInt32(collection, 12 + (faceIndex * 4)));
            if (faceOffset < 0 || faceOffset + 12 > collection.Length) throw new InvalidDataException("Invalid TTC face offset.");

            int tableCount = ReadUInt16(collection, faceOffset + 4);
            int directorySize = 12 + (tableCount * 16);
            if (faceOffset + directorySize > collection.Length) throw new InvalidDataException("Invalid TTC table directory.");

            var tables = new (int Record, int SourceOffset, int Length)[tableCount];
            int totalSize = directorySize;
            for (int i = 0; i < tableCount; i++)
            {
                int record = faceOffset + 12 + (i * 16);
                int sourceOffset = checked((int)ReadUInt32(collection, record + 8));
                int length = checked((int)ReadUInt32(collection, record + 12));
                if (sourceOffset < 0 || length < 0 || sourceOffset + length > collection.Length)
                    throw new InvalidDataException("Invalid TTC table range.");
                tables[i] = (record, sourceOffset, length);
                totalSize = checked(totalSize + Align4(length));
            }

            byte[] font = new byte[totalSize];
            Buffer.BlockCopy(collection, faceOffset, font, 0, directorySize);
            int destination = directorySize;
            int headDestination = -1;
            for (int i = 0; i < tableCount; i++)
            {
                (int record, int sourceOffset, int length) = tables[i];
                int outputRecord = 12 + (i * 16);
                WriteUInt32(font, outputRecord + 8, (uint)destination);
                Buffer.BlockCopy(collection, sourceOffset, font, destination, length);
                if (collection[record] == (byte)'h' && collection[record + 1] == (byte)'e' &&
                    collection[record + 2] == (byte)'a' && collection[record + 3] == (byte)'d')
                    headDestination = destination;
                destination += Align4(length);
            }

            if (headDestination >= 0 && headDestination + 12 <= font.Length)
            {
                WriteUInt32(font, headDestination + 8, 0);
                uint sum = FontChecksum(font);
                WriteUInt32(font, headDestination + 8, unchecked(0xB1B0AFBAu - sum));
            }
            return font;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset) =>
            (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

        private static uint ReadUInt32(byte[] bytes, int offset) =>
            ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
            ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        private static int Align4(int value) => (value + 3) & ~3;

        private static uint FontChecksum(byte[] bytes)
        {
            uint sum = 0;
            for (int i = 0; i < bytes.Length; i += 4)
            {
                uint word = (uint)bytes[i] << 24;
                if (i + 1 < bytes.Length) word |= (uint)bytes[i + 1] << 16;
                if (i + 2 < bytes.Length) word |= (uint)bytes[i + 2] << 8;
                if (i + 3 < bytes.Length) word |= bytes[i + 3];
                sum = unchecked(sum + word);
            }
            return sum;
        }
    }
}
