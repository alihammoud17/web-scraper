namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// HTTP politeness and resilience limits. These are code defaults, not environment settings; tests
/// pass smaller values so retries run in milliseconds.
/// </summary>
public sealed class CrawlerHttpOptions
{
    /// <summary>Retries after the first attempt (4 attempts in total).</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>First backoff delay; doubles per retry, with jitter.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    public bool UseJitter { get; init; } = true;

    /// <summary>One attempt (after its rate-limit token) may take this long.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Everything for one URL: attempts, backoff, rate-limit waits.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>A longer Retry-After isn't waited out in a worker; it's returned so the URL can be re-queued.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(60);

    public long MaxResponseBytes { get; init; } = 10 * 1024 * 1024;
}
