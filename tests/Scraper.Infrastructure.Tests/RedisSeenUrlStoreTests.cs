using Scraper.Domain.Urls;
using Scraper.Infrastructure.Crawling;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Tests;

public sealed class RedisSeenUrlStoreTests : IDisposable
{
    private static readonly NormalizedUrl Adder = NormalizedUrl.Create("https://gtacars.net/gta5/adder");

    private readonly ConnectionMultiplexer? _redis = string.IsNullOrWhiteSpace(TestRedis.ConnectionString) ? null : TestRedis.Connect();
    private readonly Guid _job = Guid.CreateVersion7();

    [RedisFact]
    public async Task First_mark_is_new_and_the_second_is_not()
    {
        var store = new RedisSeenUrlStore(_redis!);

        Assert.True(await store.TryMarkSeenAsync(_job, Adder, CancellationToken.None));
        Assert.False(await store.TryMarkSeenAsync(_job, Adder, CancellationToken.None));
    }

    [RedisFact]
    public async Task Concurrent_workers_see_a_url_as_new_exactly_once()
    {
        // 100 marks of the same URL in parallel, spread over 4 connections ("workers").
        var connections = Enumerable.Range(0, 4).Select(_ => TestRedis.Connect()).ToList();
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
                new RedisSeenUrlStore(connections[i % connections.Count]).TryMarkSeenAsync(_job, Adder, CancellationToken.None)));

            Assert.Equal(1, results.Count(added => added));
        }
        finally
        {
            connections.ForEach(c => c.Dispose());
        }
    }

    [RedisFact]
    public async Task Jobs_do_not_share_their_seen_sets()
    {
        var store = new RedisSeenUrlStore(_redis!);
        var otherJob = Guid.CreateVersion7();

        Assert.True(await store.TryMarkSeenAsync(_job, Adder, CancellationToken.None));
        Assert.True(await store.TryMarkSeenAsync(otherJob, Adder, CancellationToken.None));

        await _redis!.GetDatabase().KeyDeleteAsync(RedisSeenUrlStore.Key(otherJob));
    }

    [RedisFact]
    public async Task The_set_expires_after_a_week()
    {
        await new RedisSeenUrlStore(_redis!).TryMarkSeenAsync(_job, Adder, CancellationToken.None);

        var ttl = await _redis!.GetDatabase().KeyTimeToLiveAsync(RedisSeenUrlStore.Key(_job));

        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, RedisSeenUrlStore.Ttl - TimeSpan.FromMinutes(1), RedisSeenUrlStore.Ttl);
    }

    public void Dispose()
    {
        _redis?.GetDatabase().KeyDelete(RedisSeenUrlStore.Key(_job));
        _redis?.Dispose();
    }
}
