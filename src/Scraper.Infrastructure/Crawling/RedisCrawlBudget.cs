using Scraper.Application.Crawling;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// The page budget of a crawl job as one Redis counter. <c>INCRBY</c> is atomic, so concurrent workers can
/// never reserve more than <c>MaxPages</c> together. A reservation that isn't used (rolled-back transaction)
/// is simply lost: the job ends with fewer pages, never more.
/// </summary>
public sealed class RedisCrawlBudget(IConnectionMultiplexer redis) : ICrawlBudget
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public static string Key(Guid crawlJobId) => $"crawl:{crawlJobId:N}:budget";

    public async Task<int> TryReserveAsync(Guid crawlJobId, int maxPages, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        cancellationToken.ThrowIfCancellationRequested();
        if (count == 0)
        {
            return 0;
        }

        var database = redis.GetDatabase();
        var key = Key(crawlJobId);
        var total = await database.StringIncrementAsync(key, count);
        await database.KeyExpireAsync(key, Ttl, ExpireWhen.HasNoExpiry);

        // Pages already reserved before this call = total - count; whatever is left of maxPages is ours.
        var alreadyReserved = total - count;
        return (int)Math.Clamp(maxPages - alreadyReserved, 0, count);
    }
}
