using Scraper.Application.Crawling;
using Scraper.Domain.Urls;
using Scraper.Infrastructure.Crawling;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Tests;

public sealed class RedisRobotsPolicyTests : IDisposable
{
    private static readonly CrawlerOptions Options = new() { UserAgent = "DistributedScraperBot/1.0 (+mailto:test@example.com)" };

    private readonly ConnectionMultiplexer? _redis = string.IsNullOrWhiteSpace(TestRedis.ConnectionString) ? null : TestRedis.Connect();
    private readonly string _host = $"robots-{Guid.NewGuid():N}.example";
    private readonly FakeFetcher _fetcher = new();

    [RedisFact]
    public async Task Robots_txt_is_fetched_once_and_then_read_from_the_cache()
    {
        _fetcher.Respond(200, "User-agent: *\nDisallow: /private");
        var policy = Policy();

        Assert.True(await policy.IsAllowedAsync(Url("/public"), CancellationToken.None));
        Assert.False(await policy.IsAllowedAsync(Url("/private/x"), CancellationToken.None));
        Assert.True(await Policy().IsAllowedAsync(Url("/other"), CancellationToken.None)); // another "worker"

        var request = Assert.Single(_fetcher.Requests);
        Assert.Equal($"https://{_host}/robots.txt", request.Url.Value);
    }

    [RedisFact]
    public async Task Our_product_token_selects_our_group()
    {
        _fetcher.Respond(200, "User-agent: *\nDisallow: /\n\nUser-agent: DistributedScraperBot\nAllow: /");

        Assert.True(await Policy().IsAllowedAsync(Url("/page"), CancellationToken.None));
    }

    [RedisFact]
    public async Task A_successful_fetch_is_cached_for_24_hours()
    {
        _fetcher.Respond(200, "User-agent: *\nDisallow:");

        await Policy().IsAllowedAsync(Url("/"), CancellationToken.None);

        Assert.InRange(await Ttl(), RedisRobotsPolicy.AvailableTtl - TimeSpan.FromMinutes(1), RedisRobotsPolicy.AvailableTtl);
    }

    [RedisFact]
    public async Task A_missing_robots_txt_allows_everything_for_24_hours()
    {
        _fetcher.Respond(404, null);

        Assert.True(await Policy().IsAllowedAsync(Url("/anything"), CancellationToken.None));
        Assert.InRange(await Ttl(), RedisRobotsPolicy.AvailableTtl - TimeSpan.FromMinutes(1), RedisRobotsPolicy.AvailableTtl);
    }

    [RedisFact]
    public async Task A_server_error_disallows_everything_for_5_minutes()
    {
        _fetcher.Respond(503, null);

        Assert.False(await Policy().IsAllowedAsync(Url("/anything"), CancellationToken.None));
        Assert.InRange(await Ttl(), RedisRobotsPolicy.UnreachableTtl - TimeSpan.FromSeconds(30), RedisRobotsPolicy.UnreachableTtl);
    }

    [RedisFact]
    public async Task A_network_error_disallows_everything_for_5_minutes()
    {
        _fetcher.Throw = new HttpRequestException("connection refused");

        Assert.False(await Policy().IsAllowedAsync(Url("/anything"), CancellationToken.None));
        Assert.InRange(await Ttl(), RedisRobotsPolicy.UnreachableTtl - TimeSpan.FromSeconds(30), RedisRobotsPolicy.UnreachableTtl);
    }

    public void Dispose()
    {
        _redis?.GetDatabase().KeyDelete(RedisRobotsPolicy.Key(Url("/")));
        _redis?.Dispose();
    }

    private RedisRobotsPolicy Policy() => new(_redis!, _fetcher, Options);

    private NormalizedUrl Url(string path) => NormalizedUrl.Create($"https://{_host}{path}");

    private async Task<TimeSpan> Ttl() => await _redis!.GetDatabase().KeyTimeToLiveAsync(RedisRobotsPolicy.Key(Url("/"))) ?? TimeSpan.Zero;

    private sealed class FakeFetcher : IFetcher
    {
        private FetchResult? _next;

        public Exception? Throw { get; set; }

        public List<FetchRequest> Requests { get; } = [];

        public void Respond(int status, string? body) =>
            _next = new FetchResult(status, new Dictionary<string, string>(), body, null, null, null, null, TimeSpan.Zero);

        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Throw is null ? Task.FromResult(_next!) : Task.FromException<FetchResult>(Throw);
        }
    }
}
