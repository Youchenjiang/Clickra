using System.Collections.Generic;
using System.Text;

namespace Clickra.Core
{
    /// <summary>Converts common Simplified Chinese characters to Traditional (zh-TW).</summary>
    internal static partial class ChineseTextConverter
    {
        private static readonly Dictionary<string, string> SimpToTrad;

        static ChineseTextConverter()
        {
            SimpToTrad = new Dictionary<string, string>(3910);
            var pairs = BuildPairs();
            for (int i = 0; i < pairs.Length; i += 2)
            {
                string simp = pairs[i];
                string trad = pairs[i + 1];
                if (!SimpToTrad.ContainsKey(simp))
                    SimpToTrad[simp] = trad;
            }
        }

        public static string SimplifiedToTraditional(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    string key = text.Substring(i, 2);
                    if (SimpToTrad.TryGetValue(key, out string? trad))
                        sb.Append(trad);
                    else
                        sb.Append(key);
                    i++;
                }
                else
                {
                    string key = c.ToString();
                    if (SimpToTrad.TryGetValue(key, out string? trad))
                        sb.Append(trad);
                    else
                        sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
