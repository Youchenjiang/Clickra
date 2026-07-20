using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core;

internal class FallbackTranslator : ITranslationEngine
{
    private readonly ITranslationEngine _primary;
    private readonly ITranslationEngine _fallback;

    public FallbackTranslator(ITranslationEngine primary, ITranslationEngine fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public string Name => $"{_primary.Name}+{_fallback.Name}";

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        try
        {
            string translated = await RunProviderCallAsync(
                token => _primary.TranslateAsync(text, targetLanguage, token),
                cancellationToken);
            EnsureTranslated(text, translated, targetLanguage, _primary.Name);
            return translated;
        }
        catch when (!cancellationToken.IsCancellationRequested) // skipcq: CS-R1008
        {
            string translated = await RunProviderCallAsync(
                token => _fallback.TranslateAsync(text, targetLanguage, token),
                cancellationToken);
            EnsureTranslated(text, translated, targetLanguage, _fallback.Name);
            return translated;
        }
    }

    public async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        if (texts == null || texts.Count == 0) return results;

        foreach (var chunk in BuildChunks(texts, maxItems: 24, maxChars: 6000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<string>? translated = null;
            try
            {
                translated = await RunProviderCallAsync(
                    token => _primary.TranslateBatchAsync(chunk, targetLanguage, token),
                    cancellationToken);
                EnsureBatchTranslated(chunk, translated, targetLanguage, _primary.Name);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) // skipcq: CS-R1008
            {
                await Console.Error.WriteLineAsync(
                    $"[Translate] {_primary.Name} batch failed ({ex.Message}); falling back to {_fallback.Name}.");
                translated = await RunProviderCallAsync(
                    token => _fallback.TranslateBatchAsync(chunk, targetLanguage, token),
                    cancellationToken);
                EnsureBatchTranslated(chunk, translated, targetLanguage, _fallback.Name);
            }

            results.AddRange(translated);
        }

        if (results.Count != texts.Count)
            throw new InvalidOperationException($"{Name} returned {results.Count}/{texts.Count} total results.");

        return results;
    }

    private static async Task<T> RunProviderCallAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken callerCancellationToken)
    {
        using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        providerCts.CancelAfter(TranslationTimeouts.ProviderCallTimeout);

        try
        {
            return await operation(providerCts.Token).WaitAsync(providerCts.Token);
        }
        catch (OperationCanceledException) when (
            !callerCancellationToken.IsCancellationRequested && providerCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Translation provider call exceeded {TranslationTimeouts.ProviderCallTimeout.TotalSeconds:0}s.");
        }
    }

    private static void EnsureBatchTranslated(
        IReadOnlyList<string> source,
        IReadOnlyList<string>? translated,
        string targetLanguage,
        string providerName)
    {
        if (translated == null || translated.Count != source.Count)
            throw new InvalidOperationException(
                $"{providerName} returned {translated?.Count ?? 0}/{source.Count} results.");

        for (int index = 0; index < source.Count; index++)
            EnsureTranslated(source[index], translated[index], targetLanguage, providerName, index);
    }

    private static void EnsureTranslated(
        string source,
        string? translated,
        string targetLanguage,
        string providerName,
        int? index = null)
    {
        string location = index.HasValue ? $" at item {index.Value + 1}" : "";
        if (string.IsNullOrWhiteSpace(translated))
            throw new InvalidOperationException($"{providerName} returned an empty translation{location}.");

        if (LooksUntranslated(source, translated, targetLanguage))
            throw new InvalidOperationException(
                $"{providerName} returned source text unchanged{location}: {Preview(source)}");

        if (LooksPartiallyUntranslated(source, translated, targetLanguage))
            throw new InvalidOperationException(
                $"{providerName} returned an untranslated source fragment{location}: {Preview(source)}");
    }

    private static bool LooksUntranslated(string source, string translated, string targetLanguage)
    {
        if (!IsCjkTarget(targetLanguage) || ContainsCjk(source))
            return false;

        // Providers intentionally preserve compact product names and chart
        // labels such as "Developer-written" or "CodaMosa". They are not
        // prose and must not trigger a fallback request (which can turn a
        // valid document into a provider-rate-limit failure).
        string label = source.Trim();
        if (Regex.IsMatch(label, @"^[A-Za-z]+(?:-[A-Za-z0-9]+)+$") ||
            Regex.IsMatch(label, @"^[A-Z][a-z]+[A-Z][A-Za-z0-9]*$"))
            return false;

        string normalizedSource = NormalizeForComparison(source);
        string normalizedTranslation = NormalizeForComparison(translated);
        if (normalizedSource.Length < 8 || !string.Equals(normalizedSource, normalizedTranslation, StringComparison.OrdinalIgnoreCase))
            return false;

        int latinLetters = 0;
        foreach (char value in normalizedSource)
        {
            if (value is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
                latinLetters++;
        }

        return latinLetters >= 8;
    }

    /// <summary>
    /// Reject a mixed result where the provider translated most of a paragraph
    /// but copied a complete English sentence/line unchanged. This commonly
    /// happens at a page break and is worse than a provider failure because it
    /// produces a PDF that looks successful while still containing source text.
    /// Short technical names (ASTER, Java, CodaMosa, etc.) are intentionally
    /// ignored; a contiguous run of four source words is the minimum signal.
    /// </summary>
    private static bool LooksPartiallyUntranslated(string source, string translated, string targetLanguage)
    {
        if (!IsCjkTarget(targetLanguage) || ContainsCjk(source) || !ContainsCjk(translated))
            return false;

        string normalizedTranslation = NormalizeForComparison(translated).ToLowerInvariant();
        var sourceWords = Regex.Matches(source, @"[A-Za-z]{2,}")
            .Select(match => match.Value.ToLowerInvariant())
            .ToList();
        if (sourceWords.Count < 5) return false;

        string[] stopWords = { "the", "of", "and", "to", "in", "for", "with", "on", "is", "are", "as", "by", "from", "that", "this" };
        for (int start = 0; start <= sourceWords.Count - 5; start++)
        {
            var window = sourceWords.Skip(start).Take(5).ToList();
            string phrase = string.Join(" ", window);
            int stopWordCount = window.Count(word => stopWords.Contains(word, StringComparer.Ordinal));
            if (stopWordCount >= 2 && phrase.Split(' ').Any(word => word.Length >= 5) &&
                normalizedTranslation.Contains(phrase, StringComparison.Ordinal))
                return true;
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

    private static string NormalizeForComparison(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Preview(string value)
    {
        string normalized = NormalizeForComparison(value);
        return normalized.Length <= 120 ? normalized : normalized[..120] + "…";
    }

    private static IEnumerable<List<string>> BuildChunks(List<string> texts, int maxItems, int maxChars)
    {
        var chunk = new List<string>();
        int charCount = 0;

        foreach (var text in texts)
        {
            string value = text ?? "";
            int textLength = value.Length;
            if (chunk.Count > 0 &&
                (chunk.Count >= maxItems || charCount + textLength > maxChars))
            {
                yield return chunk;
                chunk = new List<string>();
                charCount = 0;
            }

            chunk.Add(value);
            charCount += textLength;
        }

        if (chunk.Count > 0)
            yield return chunk;
    }
}
