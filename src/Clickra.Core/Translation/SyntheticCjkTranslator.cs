using System.Text;

namespace Clickra.Core;

/// <summary>
/// Deterministic CJK stress engine used only by PDF layout regression tests.
/// It preserves formula placeholders and punctuation while mapping Latin words
/// to a representative CJK information density.
/// </summary>
internal sealed class SyntheticCjkTranslator : ITranslationEngine
{
    public string Name => "synthetic-cjk";

    public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Translate(text));
    }

    public Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((texts ?? new List<string>()).Select(Translate).ToList());
    }

    private static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var builder = new StringBuilder(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '{')
            {
                int end = text.IndexOf('}', index);
                if (end >= index)
                {
                    builder.Append(text, index, end - index + 1);
                    index = end + 1;
                    continue;
                }
            }

            char value = text[index];
            if (!char.IsLetterOrDigit(value))
            {
                builder.Append(value);
                index++;
                continue;
            }

            int runStart = index;
            while (index < text.Length && char.IsLetterOrDigit(text[index]))
            {
                index++;
            }

            int runLength = index - runStart;
            // Chinese translations normally encode a Latin word in materially
            // fewer glyphs. One CJK glyph per three Latin letters keeps this
            // deterministic fixture stressful without making it wider than a
            // plausible translation solely because CJK glyphs are full-width.
            int cjkGlyphCount = Math.Max(1, (runLength + 2) / 3);
            builder.Append('測', cjkGlyphCount);
        }
        return builder.ToString();
    }
}
