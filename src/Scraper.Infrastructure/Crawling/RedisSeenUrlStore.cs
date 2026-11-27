using Scraper.Application.Crawling;
using Scraper.Domain.Urls;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// One Redis set per crawl job. <c>SADD</c> returns 1 only for a new member, so "is it new?" and
/// "mark it seen" are a single atomic step, even with many workers (ADR 0004, ADR 0011).
/// </summary>
public sealed class RedisSeenUrlStore(IConnectionMultiplexer redis) : ISeenUrlStore
{
    /// <summary>Kept for a week after the job's first URL, then Redis frees the memory.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public static string Key(Guid crawlJobId) => $"crawl:{crawlJobId:N}:seen";

    public async Task<bool> TryMarkSeenAsync(Guid crawlJobId, NormalizedUrl url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();

        var database = redis.GetDatabase();
        var key = Key(crawlJobId);
        var added = await database.SetAddAsync(key, url.Value);
        if (added)
        {
            // NX: only the first write sets the expiry, so the set lives a week from the job's start.
            await database.KeyExpireAsync(key, Ttl, ExpireWhen.HasNoExpiry);
        }
        return added;
    }

    /// <summary>SMISMEMBER: one round trip, read-only, so a rolled-back transaction can't leave URLs marked seen.</summary>
    public async Task<IReadOnlyList<NormalizedUrl>> FilterUnseenAsync(Guid crawlJobId, IReadOnlyList<NormalizedUrl> urls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(urls);
        cancellationToken.ThrowIfCancellationRequested();
        if (urls.Count == 0)
        {
            return [];
        }
        var members = await redis.GetDatabase().SetContainsAsync(Key(crawlJobId), [.. urls.Select(u => (RedisValue)u.Value)]);
        return [.. urls.Where((_, i) => !members[i])];
    }

    /// <summary>SADD after the database commit; Postgres's unique (job, url) stays the arbiter.</summary>
    public async Task MarkSeenAsync(Guid crawlJobId, IReadOnlyList<NormalizedUrl> urls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(urls);
        cancellationToken.ThrowIfCancellationRequested();
        if (urls.Count == 0)
        {
            return;
        }
        var database = redis.GetDatabase();
        var key = Key(crawlJobId);
        await database.SetAddAsync(key, [.. urls.Select(u => (RedisValue)u.Value)]);
        await database.KeyExpireAsync(key, Ttl, ExpireWhen.HasNoExpiry);
    }
}
