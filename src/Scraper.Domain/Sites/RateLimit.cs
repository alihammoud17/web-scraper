namespace Scraper.Domain.Sites;

/// <summary>Maximum request rate for one site, shared by all workers (enforced in Redis, ADR 0004).</summary>
public sealed record RateLimit
{
    /// <summary>Hard ceiling, so a typo in a site's configuration can't hammer a small site.</summary>
    public const decimal MaxRequestsPerSecond = 5m;

    public RateLimit(decimal requestsPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestsPerSecond, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requestsPerSecond, MaxRequestsPerSecond);
        RequestsPerSecond = requestsPerSecond;
    }

    public decimal RequestsPerSecond { get; }
}
