using System.Text.Json;
using Scraper.Application.Crawling;
using Scraper.Domain.Robots;
using Scraper.Domain.Urls;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// robots.txt checks with a shared Redis cache per site origin. A miss fetches <c>/robots.txt</c> through
/// <see cref="IFetcher"/>, so that request is rate-limited like any other. Cached for 24 h after a 2xx/4xx
/// (RFC 9309's maximum) and 5 min after a 5xx or network error, so a temporary outage clears quickly.
/// </summary>
public sealed class RedisRobotsPolicy(IConnectionMultiplexer redis, IFetcher fetcher, CrawlerOptions options) : IRobotsPolicy
{
    public static readonly TimeSpan AvailableTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan UnreachableTtl = TimeSpan.FromMinutes(5);

    /// <summary>RFC 9309 asks crawlers to follow at least five redirects for robots.txt.</summary>
    private const int MaxRedirects = 5;

    /// <summary>Status used for "no HTTP response at all" (network error, timeout).</summary>
    private const int Unreachable = 0;

    public static string Key(NormalizedUrl url) => $"robots:{url.Scheme}://{url.Authority}";

    public async Task<bool> IsAllowedAsync(NormalizedUrl url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        var entry = await GetCachedAsync(url) ?? await FetchAndCacheAsync(url, cancellationToken);
        return RobotsTxt.FromResponse(entry.Status, entry.Body).IsAllowed(options.ProductToken, url.PathAndQuery);
    }

    private async Task<CacheEntry?> GetCachedAsync(NormalizedUrl url)
    {
        var cached = await redis.GetDatabase().StringGetAsync(Key(url));
        return cached.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CacheEntry>(cached.ToString());
    }

    private async Task<CacheEntry> FetchAndCacheAsync(NormalizedUrl url, CancellationToken cancellationToken)
    {
        var entry = await FetchAsync(url, cancellationToken);
        var ttl = entry.Status is Unreachable or >= 500 ? UnreachableTtl : AvailableTtl;
        await redis.GetDatabase().StringSetAsync(Key(url), JsonSerializer.Serialize(entry), ttl);
        return entry;
    }

    private async Task<CacheEntry> FetchAsync(NormalizedUrl url, CancellationToken cancellationToken)
    {
        var target = NormalizedUrl.Create($"{url.Scheme}://{url.Authority}/robots.txt");
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                var result = await fetcher.FetchAsync(new FetchRequest(target), cancellationToken);
                if (!result.IsRedirect)
                {
                    return new CacheEntry(result.StatusCode, result.IsSuccess ? result.Body : null);
                }
                // Too many redirects means "unavailable" (allow all). A redirect to another site can't be
                // fetched politely (unknown rate limit), so it fails closed like an unreachable server.
                if (redirects == MaxRedirects)
                {
                    return new CacheEntry(result.StatusCode, null);
                }
                if (!NormalizedUrl.TryCreate(result.RedirectLocation, new Uri(target.Value), out var next) || next.Authority != url.Authority)
                {
                    return new CacheEntry(Unreachable, null);
                }
                target = next;
            }
        }
#pragma warning disable CA1031 // Any failure to get robots.txt (network, timeout, refused host) means "unreachable".
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return new CacheEntry(Unreachable, null);
        }
    }

    private sealed record CacheEntry(int Status, string? Body);
}
