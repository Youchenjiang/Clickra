using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core;

internal abstract class BaseTranslator : ITranslationEngine
{
    public abstract string Name { get; }
    protected static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim ConcurrencySemaphore = new(1, 1);
    private static readonly Random Rnd = new();

    public abstract Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken);
    public abstract Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken);

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        int retries = 5;
        int delayMs = 1500;
        while (true)
        {
            await ConcurrencySemaphore.WaitAsync(cancellationToken);
            try
            {
                int sleepMs = Rnd.Next(150, 400);
                await Task.Delay(sleepMs, cancellationToken);

                string result = await TranslateInternalAsync(text, targetLanguage, cancellationToken);
                return string.IsNullOrWhiteSpace(result) ? text : result;
            }
            catch (Exception) when (retries > 0 && !cancellationToken.IsCancellationRequested) // skipcq: CS-R1008
            {
                retries--;
            }
            finally
            {
                ConcurrencySemaphore.Release();
            }

            await Task.Delay(delayMs, cancellationToken);
            delayMs *= 2;
        }
    }

    protected static string NormalizeLanguageCode(string code) => LanguageCodeHelper.Normalize(code);
}

internal static class LanguageCodeHelper
{
    public static string Normalize(string code)
    {
        code = code.ToLowerInvariant();
        return code switch
        {
            "zh-tw" => "zh-TW",
            "zh-cn" => "zh-CN",
            _ => code
        };
    }
}
