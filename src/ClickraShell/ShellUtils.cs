using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml.Linq;
using System.Runtime.CompilerServices;

namespace ClickraShell
{
    internal static class ShellUtils
    {
        public static unsafe string GetModuleDir()
        {
            IntPtr fnPtr = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryAddRef;
            if (Kernel32.GetModuleHandleExW(6, fnPtr, out IntPtr hModule))
            {
                unsafe
                {
                    char* buf = stackalloc char[260];
                    uint len = Kernel32.GetModuleFileNameW(hModule, buf, 260);
                    if (len > 0) return Path.GetDirectoryName(new string(buf, 0, (int)len)) ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public static string GetString(string key)
        {
            try
            {
                string dir = GetModuleDir();
                string culture = CultureInfo.CurrentUICulture.Name;

                string subFolder = "zh-tw"; // Default fallback
                if (culture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    subFolder = "en-us";
                else if (culture.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                    subFolder = "zh-cn";
                else if (culture.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                    subFolder = "ja-jp";
                else if (culture.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                    subFolder = "ko-kr";
                else if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    subFolder = "zh-tw";

                string resPath = Path.Combine(dir, "Strings", subFolder, "Resources.resw");
                if (!File.Exists(resPath))
                {
                    resPath = Path.Combine(dir, "Strings", "zh-tw", "Resources.resw");
                }

                if (!File.Exists(resPath)) return key;

                var doc = XDocument.Load(resPath);
                var data = doc.Root?.Elements("data").FirstOrDefault(e => e.Attribute("name")?.Value == key);
                return data?.Element("value")?.Value ?? key;
            }
            catch { return key; }
        }

        public static string? GetPackagedAppUserModelId()
        {
            try
            {
                string dir = GetModuleDir();
                string packageName = Path.GetFileName(dir);
                int separator = packageName.IndexOf('_');
                int publisherSeparator = packageName.LastIndexOf("__", StringComparison.Ordinal);
                if (separator <= 0 || publisherSeparator <= separator + 1) return null;

                string packageFamilyName = packageName[..separator] + "_" + packageName[(publisherSeparator + 2)..];
                string manifestPath = Path.Combine(dir, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) return null;

                var doc = XDocument.Load(manifestPath);
                string? appId = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application")?.Attribute("Id")?.Value;
                return string.IsNullOrWhiteSpace(appId) ? null : $"{packageFamilyName}!{appId}";
            }
            catch { return null; }
        }
    }
}
