using System.Text;
using System.Text.RegularExpressions;

namespace Clickra.Core;

/// <summary>
/// Rejects provider output that is non-empty but unsafe to render as a
/// successful translation. This guard intentionally uses high-confidence
/// signals only; stylistic translation quality still belongs to review.
/// </summary>
internal static class TranslationResultQualityGuard
{
    private static readonly Regex BoldMarkerRegex = new(
        @"\{/?b\}", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex BrokenBoldMarkerRegex = new(
        @"(?:/\s*b\s*[{}]|[{}]\s*/\s*b(?!\s*\})|[{}]\s*b\s*[{}])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex EnglishConnectorRunRegex = new(
        @"\b(?:a|an|the|and|or|but|as|well|of|to|in|on|for|with|by|from|that|this|is|are|was|were)(?:\s+(?:a|an|the|and|or|but|as|well|of|to|in|on|for|with|by|from|that|this|is|are|was|were)){2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static string? FindProblem(string source, string translated, string targetLanguage)
    {
        var sourceMarkers = BoldMarkerRegex.Matches(source).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var translatedMarkers = BoldMarkerRegex.Matches(translated).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (!sourceMarkers.SequenceEqual(translatedMarkers))
            return "inline bold markers were changed, reordered, or duplicated";

        string withoutValidMarkers = BoldMarkerRegex.Replace(translated, "");
        if (BrokenBoldMarkerRegex.IsMatch(withoutValidMarkers))
            return "broken or incomplete bold marker tags found";

        if (ContainsTripledSequence(translated))
            return "a long phrase was repeated three times";

        if (EnglishConnectorRunRegex.IsMatch(translated))
            return "an untranslated English connective phrase remained";

        return null;
    }

    private static bool ContainsTripledSequence(string value)
    {
        var compact = new StringBuilder(value.Length);
        foreach (char character in BoldMarkerRegex.Replace(value, "").Where(char.IsLetterOrDigit))
        {
            compact.Append(char.ToLowerInvariant(character));
        }

        string text = compact.ToString();
        int maximumLength = Math.Min(48, text.Length / 3);
        for (int length = 6; length <= maximumLength; length++)
        {
            for (int start = 0; start + length * 3 <= text.Length; start++)
            {
                if (text.AsSpan(start, length).SequenceEqual(text.AsSpan(start + length, length)) &&
                    text.AsSpan(start, length).SequenceEqual(text.AsSpan(start + length * 2, length)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCjkTarget(string targetLanguage) =>
        targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
        targetLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
        targetLanguage.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCjk(string value) => value.Any(character =>
        character is >= '\u3040' and <= '\u30ff' or
        >= '\u3400' and <= '\u4dbf' or
        >= '\u4e00' and <= '\u9fff' or
        >= '\uac00' and <= '\ud7af');
}
