using System.Text;

namespace Clickra.Core;

/// <summary>
/// Deterministic CJK stress engine used only by PDF layout regression tests.
/// It preserves formula placeholders and punctuation while expanding Latin words
/// into representative CJK glyphs.
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
        var builder = new StringBuilder(text.Length * 2);
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                int end = text.IndexOf('}', index);
                if (end >= index)
                {
                    builder.Append(text, index, end - index + 1);
                    index = end;
                    continue;
                }
            }

            char value = text[index];
            builder.Append(char.IsLetterOrDigit(value) ? '測' : value);
        }
        return builder.ToString();
    }
}
