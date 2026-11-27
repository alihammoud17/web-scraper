using System.Globalization;

namespace Scraper.Application.Crawling;

/// <summary>
/// How often and how long to wait before retrying a task through the queue. Each delay is one retry tier
/// (one RabbitMQ queue with that TTL), so the list is also the queue topology.
/// </summary>
public sealed record RetryOptions
{
    public static readonly RetryOptions Default = new(5, [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)]);

    public RetryOptions(int maxAttempts, IReadOnlyList<TimeSpan> delays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentNullException.ThrowIfNull(delays);
        if (delays.Count == 0 || delays.Any(d => d <= TimeSpan.Zero) || delays.Zip(delays.Skip(1)).Any(p => p.Second <= p.First))
        {
            throw new ArgumentException("Retry delays must be positive and strictly increasing.", nameof(delays));
        }
        MaxAttempts = maxAttempts;
        Delays = delays;
    }

    /// <summary>Attempts in total, including the first; after the last one the task is dead-lettered.</summary>
    public int MaxAttempts { get; }

    /// <summary>Delay before retry n is <c>Delays[n - 1]</c> (the last one repeats).</summary>
    public IReadOnlyList<TimeSpan> Delays { get; }

    /// <summary>Reads <c>Queue__MaxAttempts</c> and <c>Queue__RetryDelays</c> ("5,30,120,600" in seconds); null keeps the default.</summary>
    public static RetryOptions From(string? maxAttempts, string? delaysInSeconds)
    {
        var attempts = string.IsNullOrWhiteSpace(maxAttempts) ? Default.MaxAttempts : int.Parse(maxAttempts, CultureInfo.InvariantCulture);
        var delays = string.IsNullOrWhiteSpace(delaysInSeconds)
            ? Default.Delays
            : delaysInSeconds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => TimeSpan.FromSeconds(int.Parse(s, CultureInfo.InvariantCulture)))
                .ToList();
        return new RetryOptions(attempts, delays);
    }

    /// <summary>A short, stable name for a tier, used in queue names and routing keys: 5s, 2m, 1h.</summary>
    public static string TierName(TimeSpan delay)
    {
        var seconds = (long)delay.TotalSeconds;
        return seconds % 3600 == 0 ? $"{seconds / 3600}h" : seconds % 60 == 0 ? $"{seconds / 60}m" : $"{seconds}s";
    }
}
