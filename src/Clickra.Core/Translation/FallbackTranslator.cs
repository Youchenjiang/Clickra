using System;
using System.Collections.Generic;
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
            return await _primary.TranslateAsync(text, targetLanguage, cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return await _fallback.TranslateAsync(text, targetLanguage, cancellationToken);
        }
    }

    public async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        if (texts == null || texts.Count == 0) return results;

        foreach (var chunk in BuildChunks(texts, maxItems: 24, maxChars: 6000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<string> translated;
            try
            {
                translated = await _primary.TranslateBatchAsync(chunk, targetLanguage, cancellationToken);
                if (translated.Count != chunk.Count)
                    throw new Exception($"{_primary.Name} returned {translated.Count}/{chunk.Count} results.");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine(
                    $"[Translate] {_primary.Name} batch failed ({ex.Message}); falling back to {_fallback.Name}.");
                translated = await _fallback.TranslateBatchAsync(chunk, targetLanguage, cancellationToken);
                if (translated.Count != chunk.Count)
                    throw new Exception($"{_fallback.Name} returned {translated.Count}/{chunk.Count} results.");
            }

            results.AddRange(translated);
        }

        if (results.Count != texts.Count)
            throw new Exception($"{Name} returned {results.Count}/{texts.Count} total results.");

        return results;
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
