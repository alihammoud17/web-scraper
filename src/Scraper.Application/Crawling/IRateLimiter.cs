using Scraper.Domain.Sites;

namespace Scraper.Application.Crawling;

/// <summary>A request budget per site authority, shared by all workers.</summary>
public interface IRateLimiter
{
    /// <summary>Waits until a request to <paramref name="authority"/> is allowed, then consumes it.</summary>
    Task AcquireAsync(string authority, RateLimit limit, CancellationToken cancellationToken);
}
