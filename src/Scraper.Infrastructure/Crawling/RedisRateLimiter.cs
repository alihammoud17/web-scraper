using System.Globalization;
using Scraper.Application.Crawling;
using Scraper.Domain.Sites;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// Per-site token bucket shared by all workers (<c>token-bucket.lua</c>). Capacity 1 means requests to
/// a site are evenly spaced at 1 / rate seconds, however many workers ask at once.
/// </summary>
public sealed class RedisRateLimiter(IConnectionMultiplexer redis) : IRateLimiter
{
    private const int Capacity = 1;

    /// <summary>Small random extra wait, so workers woken at the same time don't all retry at once.</summary>
    private const int MaxJitterMilliseconds = 25;

    private static readonly string Script = LoadScript();

    public static string Key(string authority) => $"ratelimit:{authority}";

    public async Task AcquireAsync(string authority, RateLimit limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentNullException.ThrowIfNull(limit);

        var database = redis.GetDatabase();
        RedisKey[] keys = [Key(authority)];
        RedisValue[] arguments = [limit.RequestsPerSecond.ToString(CultureInfo.InvariantCulture), Capacity];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waitMilliseconds = (long)await database.ScriptEvaluateAsync(Script, keys, arguments);
            if (waitMilliseconds <= 0)
            {
                return;
            }
#pragma warning disable CA5394 // Jitter only spreads retries; it needs no cryptographic randomness.
            var jitter = Random.Shared.Next(0, MaxJitterMilliseconds);
#pragma warning restore CA5394
            await Task.Delay(TimeSpan.FromMilliseconds(waitMilliseconds + jitter), cancellationToken);
        }
    }

    private static string LoadScript()
    {
        using var stream = typeof(RedisRateLimiter).Assembly.GetManifestResourceStream("Scraper.Infrastructure.Crawling.token-bucket.lua")
            ?? throw new InvalidOperationException("Embedded resource token-bucket.lua is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
