using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core
{
    public interface ITranslationEngine
    {
        string Name { get; }
        Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken);
    }

    public static class TranslationEngineFactory
    {
        public static ITranslationEngine Create()
        {
            return new GoogleFreeTranslator();
        }
    }

    public abstract class BaseTranslator : ITranslationEngine
    {
        public abstract string Name { get; }
        protected static readonly HttpClient HttpClient = new HttpClient();
        private static readonly SemaphoreSlim ConcurrencySemaphore = new SemaphoreSlim(5, 5);

        public abstract Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken);

        public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            await ConcurrencySemaphore.WaitAsync(cancellationToken);
            try
            {
                int retries = 3;
                int delayMs = 1000;
                while (true)
                {
                    try
                    {
                        string result = await TranslateInternalAsync(text, targetLanguage, cancellationToken);
                        return string.IsNullOrWhiteSpace(result) ? text : result;
                    }
                    catch (Exception) when (retries > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        retries--;
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs *= 2;
                    }
                }
            }
            finally
            {
                ConcurrencySemaphore.Release();
            }
        }

        protected string NormalizeLanguageCode(string code)
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

    public class GoogleFreeTranslator : BaseTranslator
    {
        public override string Name => "google-free";

        public override async Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            string lang = NormalizeLanguageCode(targetLanguage);
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={lang}&dt=t";

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("q", text)
            });

            using var response = await HttpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var segments = root[0];
                if (segments.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var seg in segments.EnumerateArray())
                    {
                        if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0)
                        {
                            var translatedPart = seg[0].GetString();
                            if (translatedPart != null)
                            {
                                sb.Append(translatedPart);
                            }
                        }
                    }
                    return sb.ToString();
                }
            }
            throw new Exception("Unexpected response format from Google Free Translate.");
        }
    }
}
