using Scraper.Domain.Urls;
using Scraper.Infrastructure.Crawling;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Tests;

public sealed class RedisCrawlBudgetTests : IDisposable
{
    private readonly ConnectionMultiplexer? _redis = string.IsNullOrWhiteSpace(TestRedis.ConnectionString) ? null : TestRedis.Connect();
    private readonly Guid _job = Guid.CreateVersion7();

    [RedisFact]
    public async Task Concurrent_reservations_never_exceed_the_budget()
    {
        var connections = Enumerable.Range(0, 4).Select(_ => TestRedis.Connect()).ToList();
        try
        {
            // 40 workers each try to reserve 7 pages of a 100-page budget.
            var granted = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
                new RedisCrawlBudget(connections[i % connections.Count]).TryReserveAsync(_job, 100, 7, CancellationToken.None)));

            Assert.Equal(100, granted.Sum());
            Assert.All(granted, g => Assert.InRange(g, 0, 7));
        }
        finally
        {
            connections.ForEach(c => c.Dispose());
        }
    }

    [RedisFact]
    public async Task A_spent_budget_grants_nothing()
    {
        var budget = new RedisCrawlBudget(_redis!);

        Assert.Equal(3, await budget.TryReserveAsync(_job, 5, 3, CancellationToken.None));
        Assert.Equal(2, await budget.TryReserveAsync(_job, 5, 3, CancellationToken.None));
        Assert.Equal(0, await budget.TryReserveAsync(_job, 5, 3, CancellationToken.None));
    }

    [RedisFact]
    public async Task Seen_set_batch_operations_filter_and_mark()
    {
        var seen = new RedisSeenUrlStore(_redis!);
        NormalizedUrl[] urls = [NormalizedUrl.Create("http://testsite:8080/a"), NormalizedUrl.Create("http://testsite:8080/b")];

        Assert.Equal(urls, await seen.FilterUnseenAsync(_job, urls, CancellationToken.None));

        await seen.MarkSeenAsync(_job, [urls[0]], CancellationToken.None);

        Assert.Equal([urls[1]], await seen.FilterUnseenAsync(_job, urls, CancellationToken.None));
        Assert.False(await seen.TryMarkSeenAsync(_job, urls[0], CancellationToken.None));
        Assert.NotNull(await _redis!.GetDatabase().KeyTimeToLiveAsync(RedisSeenUrlStore.Key(_job)));
    }

    public void Dispose()
    {
        _redis?.GetDatabase().KeyDelete([RedisCrawlBudget.Key(_job), RedisSeenUrlStore.Key(_job)]);
        _redis?.Dispose();
    }
}
