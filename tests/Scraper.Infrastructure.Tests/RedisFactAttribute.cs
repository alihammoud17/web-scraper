using StackExchange.Redis;

namespace Scraper.Infrastructure.Tests;

/// <summary>
/// A test that needs a real Redis (Lua scripts and atomic commands can't be mocked meaningfully).
/// Runs when <c>SCRAPER_TEST_REDIS</c> is set (CI sets it for its Redis service container; locally use
/// <c>localhost:6381</c> after <c>docker compose up -d redis</c>), and is skipped otherwise.
/// </summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(TestRedis.ConnectionString))
        {
            Skip = $"Set {TestRedis.Variable} (e.g. localhost:6381) to run Redis-backed tests.";
        }
    }
}

internal static class TestRedis
{
    public const string Variable = "SCRAPER_TEST_REDIS";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(Variable);

    /// <summary>A new connection each call, so tests can simulate several workers.</summary>
    public static ConnectionMultiplexer Connect() => ConnectionMultiplexer.Connect(ConnectionString!);
}
