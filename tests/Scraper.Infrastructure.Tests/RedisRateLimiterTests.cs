using System.Diagnostics;
using Scraper.Domain.Sites;
using Scraper.Infrastructure.Crawling;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Tests;

public sealed class RedisRateLimiterTests : IDisposable
{
    private readonly string _authority = $"rate-test-{Guid.NewGuid():N}.example";
    private readonly List<ConnectionMultiplexer> _connections = [];

    [RedisFact]
    public async Task Eight_workers_together_stay_within_the_site_rate()
    {
        // 8 limiters with their own connections act like 8 worker containers sharing one site budget.
        var limit = new RateLimit(5m);
        var duration = TimeSpan.FromSeconds(3);
        var clock = Stopwatch.StartNew();
        var grants = new List<TimeSpan>();

        var workers = Enumerable.Range(0, 8).Select(async _ =>
        {
            var limiter = new RedisRateLimiter(Connect());
            using var stop = new CancellationTokenSource(duration);
            try
            {
                while (true)
                {
                    await limiter.AcquireAsync(_authority, limit, stop.Token);
                    lock (grants)
                    {
                        grants.Add(clock.Elapsed);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Time's up for this worker.
            }
        });
        await Task.WhenAll(workers);

        // A full bucket (1 token) plus 3 s × 5 tokens/s = at most 16 grants.
        Assert.InRange(grants.Count, 12, 16);
        var gaps = grants.Order().Zip(grants.Order().Skip(1), (a, b) => b - a).ToList();
        Assert.True(gaps.Min() >= TimeSpan.FromMilliseconds(190), $"Smallest gap between grants was {gaps.Min().TotalMilliseconds:F0} ms.");
    }

    [RedisFact]
    public async Task Different_sites_have_separate_budgets()
    {
        var limiter = new RedisRateLimiter(Connect());
        var limit = new RateLimit(0.2m); // one request per 5 s
        var otherAuthority = _authority + ".other";
        var clock = Stopwatch.StartNew();

        await limiter.AcquireAsync(_authority, limit, CancellationToken.None);
        await limiter.AcquireAsync(otherAuthority, limit, CancellationToken.None);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "The second site waited for the first site's budget.");
        await _connections[0].GetDatabase().KeyDeleteAsync(RedisRateLimiter.Key(otherAuthority));
    }

    public void Dispose()
    {
        if (_connections.Count > 0)
        {
            _connections[0].GetDatabase().KeyDelete(RedisRateLimiter.Key(_authority));
        }
        _connections.ForEach(c => c.Dispose());
    }

    private ConnectionMultiplexer Connect()
    {
        var connection = TestRedis.Connect();
        lock (_connections)
        {
            _connections.Add(connection);
        }
        return connection;
    }
}
