using System;
using System.IO;
using System.Reflection;
using PdfSharp.Fonts;

namespace Clickra.Core
{
    public class ClickraFontResolver : IFontResolver
    {
        public string DefaultFontName => "Arial";

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
                // CJK translation output must always use the real regular KaiU face.
                // Simulated bold/italic and Latin fallbacks produce missing glyph boxes.
                return new FontResolverInfo("kaiu");
            }
            if (name.Contains("jhenghei") || name.Contains("正黑"))
            {
                return new FontResolverInfo("kaiu");
            }
            if (name.Contains("yahei") || name.Contains("雅黑"))
            {
                return new FontResolverInfo("kaiu");
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
            if (name.Contains("times"))
            {
                return new FontResolverInfo("times" + suffix);
            }
            if (name.Contains("segoe ui symbol") || name.Contains("symbol") || name.Contains("math") || name.Contains("cmsy") || name.Contains("msam") || name.Contains("msbm"))
            {
                return new FontResolverInfo("seguisym");
            }
            if (name.Contains("courier") || name.Contains("mono") || name.Contains("consolas") || name.Contains("nimbusmon") || name.Contains("monl") || name.Contains("cmtt"))
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
                    return File.ReadAllBytes(fontPath);
                }
                catch { }
            }

            // Fallback for CJK faces if the file is missing
            if (baseFace == "kaiu" || baseFace == "msjh" || baseFace == "msgothic" ||
                baseFace == "msyh" || baseFace == "malgun")
            {
                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string winFonts = Path.Combine(systemDir, "..", "Fonts");
                string kaiuPath = Path.Combine(winFonts, "kaiu.ttf");
                if (File.Exists(kaiuPath))
                {
                    try { return File.ReadAllBytes(kaiuPath); } catch { }
                }
                string simsunbPath = Path.Combine(winFonts, "simsunb.ttf");
                if (File.Exists(simsunbPath))
                {
                    try { return File.ReadAllBytes(simsunbPath); } catch { }
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

        private string GetFontPath(string baseFace, string style)
        {
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string winFonts = Path.Combine(systemDir, "..", "Fonts");
            string file = baseFace switch
            {
                "kaiu" => "kaiu.ttf",
                "msjh" => "msjh.ttc", // Use standard Windows Microsoft JhengHei TTC
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
                "seguisym" => "seguisym.ttf",
                "cambria" => style switch
                {
                    "b" => "cambriab.ttf",
                    "i" => "cambriai.ttf",
                    "bi" => "cambriaz.ttf",
                    _ => "cambria.ttc"
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
    }
}
