using System.Text.RegularExpressions;

namespace Clickra.Core;

internal static class TranslationSourcePreservationClassifier
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static bool IsHighConfidenceTechnicalLabel(string source)
    {
        string label = RemoveStyleMarkers(source).Trim();
        label = Regex.Replace(
            label,
            @"^(?:(?:Appendix|Section)\s+)?(?:[A-Z]|\d{1,2}|[IVXLCDM]{1,6})(?:\.\d{1,2})*\.?\s+",
            string.Empty,
            RegexOptions.IgnoreCase,
            RegexTimeout);

        if (string.IsNullOrWhiteSpace(label) || label.Any(char.IsWhiteSpace))
            return false;

        // Product and protocol identifiers are an open set. Detect their
        // shape instead of maintaining a name whitelist: mixed-case words
        // (CoppeliaSim), identifiers containing digits (WinUI3), and compact
        // hyphenated identifiers with a digit or mixed casing (GPT-4).
        bool hasUpper = label.Any(char.IsUpper);
        bool hasLower = label.Any(char.IsLower);
        int upperCount = label.Count(char.IsUpper);
        bool mixedCaseIdentifier = hasUpper && hasLower && upperCount >= 2;
        bool digitIdentifier = label.Any(char.IsDigit) && label.Any(char.IsLetter);
        bool structuredIdentifier = label.Contains('-') &&
            (digitIdentifier || mixedCaseIdentifier);

        return Regex.IsMatch(
                   label,
                   @"^[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)*$",
                   RegexOptions.None,
                   RegexTimeout) &&
               (mixedCaseIdentifier || digitIdentifier || structuredIdentifier);
    }

    public static bool IsUnchanged(string source, string translated)
    {
        string normalizedSource = Normalize(source);
        string normalizedTranslation = Normalize(translated);
        return normalizedSource.Length > 0 &&
               string.Equals(normalizedSource, normalizedTranslation, StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveStyleMarkers(string value) =>
        Regex.Replace(value, @"\{/?[bi]\}", string.Empty, RegexOptions.IgnoreCase, RegexTimeout);

    private static string Normalize(string value) =>
        string.Join(' ', RemoveStyleMarkers(value).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
}
