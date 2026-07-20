using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core;

internal class MyMemoryTranslator : ITranslationEngine
{
    private static readonly string? _nodePath = ResolveNodePath();
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static string? ResolveNodePath()
    {
        string[] knownPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
        };
        return knownPaths.FirstOrDefault(File.Exists);
    }

    public string Name => "mymemory";

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        int retries = 2;
        int delayMs = 500;
        while (true)
        {
            try
            {
                return await TranslateInternalAsync(text, targetLanguage, cancellationToken);
            }
            catch (Exception httpException) when (!cancellationToken.IsCancellationRequested) // skipcq: CS-R1008
            {
                // Node is an optional per-request fallback. Never permanently latch
                // the translator into Node mode after one transient HTTP failure:
                // that made every later request fail on machines without Node.js.
                if (_nodePath != null)
                {
                    try
                    {
                        return await TranslateWithNodeAsync(text, targetLanguage, cancellationToken);
                    }
                    catch (Exception nodeException) when (!cancellationToken.IsCancellationRequested) // skipcq: CS-R1008
                    {
                        httpException = new AggregateException(httpException, nodeException);
                    }
                }

                if (retries <= 0)
                {
                    throw new InvalidOperationException(
                        "MyMemory HTTP translation failed after bounded retries.",
                        httpException);
                }
                retries--;
            }

            await Task.Delay(delayMs, cancellationToken);
            delayMs = Math.Min(delayMs * 2, 3000);
        }
    }

    private static async Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        string langPair = Uri.EscapeDataString($"en|{NormalizeLanguageCode(targetLanguage)}");
        string query = Uri.EscapeDataString(text);
        string url = $"https://api.mymemory.translated.net/get?q={query}&langpair={langPair}&de=clickra@yandex.com";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 Clickra/1.0");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("responseData", out var responseData) &&
            responseData.TryGetProperty("translatedText", out var translatedText))
        {
            return translatedText.GetString() ?? text;
        }

        throw new InvalidOperationException("Invalid MyMemory response structure.");
    }

    private static async Task<string> TranslateWithNodeAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        string script = @"
const https = require('https');
const text = process.argv[1] || '';
const target = process.argv[2] || 'zh-TW';
const url = 'https://api.mymemory.translated.net/get?q=' + encodeURIComponent(text) +
  '&langpair=' + encodeURIComponent('en|' + target) +
  '&de=clickra@yandex.com';
const req = https.get(url, res => {
  let data = '';
  res.on('data', chunk => data += chunk);
  res.on('end', () => {
    try {
      const parsed = JSON.parse(data);
      const translated = parsed && parsed.responseData && parsed.responseData.translatedText;
      if (!translated) process.exit(2);
      process.stdout.write(translated);
    } catch {
      process.exit(3);
    }
  });
});
req.on('error', () => process.exit(4));
req.setTimeout(20000, () => { req.destroy(); process.exit(5); });
";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _nodePath!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-e");
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add(text);
        process.StartInfo.ArgumentList.Add(NormalizeLanguageCode(targetLanguage));

        try
        {
            process.Start();
        }
        catch (Exception ex) // skipcq: CS-R1008, CS-W1100
        {
            throw new InvalidOperationException("Node.js MyMemory fallback is unavailable.", ex);
        }

        using var registration = cancellationToken.Register(() => // skipcq: CS-W1100
        {
            try { process.Kill(true); } catch { /* Process may have already exited */ }
        });

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        string output = await outputTask;
        string error = await errorTask;
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException($"Node.js MyMemory fallback failed: {process.ExitCode} {error}");

        return output;
    }

    public async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        if (texts == null || texts.Count == 0) return results;

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await TranslateAsync(text, targetLanguage, cancellationToken));
            await Task.Delay(150, cancellationToken);
        }

        return results;
    }

    private static string NormalizeLanguageCode(string code) => LanguageCodeHelper.Normalize(code);
}
