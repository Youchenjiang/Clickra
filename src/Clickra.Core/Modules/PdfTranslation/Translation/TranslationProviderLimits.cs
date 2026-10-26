using System;
using System.Linq;
using System.Net.Http;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

/// <summary>
/// A provider asked Clickra to slow down: HTTP 429, or a verification page
/// returned in place of a translation. Waiting may help; hammering will not.
/// </summary>
public sealed class TranslationRateLimitException : Exception
{
    public TranslationRateLimitException()
        : base("The translation provider rate limited the request.")
    {
    }

    public TranslationRateLimitException(string message)
        : base(message)
    {
    }

    public TranslationRateLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TranslationRateLimitException(TimeSpan retryAfter)
        : base($"The translation provider rate limited the request; retry after {retryAfter.TotalSeconds:0}s.")
    {
        RetryAfter = retryAfter;
    }

    public TranslationRateLimitException(string message, TimeSpan retryAfter)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    public TranslationRateLimitException(string message, TimeSpan retryAfter, Exception innerException)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>The wait the provider suggested; <see cref="TimeSpan.Zero"/> when it gave none.</summary>
    public TimeSpan RetryAfter { get; }
}

/// <summary>
/// A provider's free daily quota is used up. Retrying the same provider today
/// will not succeed, so callers must not retry it in a loop.
/// </summary>
public sealed class TranslationQuotaExceededException : Exception
{
    public TranslationQuotaExceededException()
        : base("The translation provider's free quota is used up.")
    {
    }

    public TranslationQuotaExceededException(string message)
        : base(message)
    {
    }

    public TranslationQuotaExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Shared rules for recognising and waiting out provider limits.</summary>
internal static class TranslationProviderLimits
{
    internal static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan MinimumRetryAfter = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>True when the exception, or anything it wraps, is a rate limit or an exhausted quota.</summary>
    public static bool IsLimit(Exception? exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is TranslationRateLimitException or TranslationQuotaExceededException)
                return true;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(inner => IsLimit(inner)))
                return true;
        }

        return false;
    }

    /// <summary>The wait to honour before one retry: the provider's suggestion, clamped to 1–30 s.</summary>
    public static TimeSpan SuggestedWait(Exception? exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is TranslationRateLimitException { RetryAfter: var retryAfter } && retryAfter > TimeSpan.Zero)
                return Clamp(retryAfter);
        }

        return DefaultRetryAfter;
    }

    /// <summary>Reads Retry-After (seconds or an HTTP date); falls back when the header is absent.</summary>
    public static TimeSpan ReadRetryAfter(HttpResponseMessage response, TimeSpan fallback)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta)
            return delta;
        if (retryAfter?.Date is DateTimeOffset date)
        {
            TimeSpan wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return fallback;
    }

    public static TimeSpan Clamp(TimeSpan delay) =>
        TimeSpan.FromTicks(Math.Clamp(delay.Ticks, MinimumRetryAfter.Ticks, MaximumRetryAfter.Ticks));
}
