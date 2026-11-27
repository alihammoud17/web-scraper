using RabbitMQ.Client;
using Scraper.Contracts;
using Scraper.Infrastructure.Messaging;
using Scraper.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Scraper.Api;

public static class HealthEndpoint
{
    private const string Healthy = "healthy";
    private const string Unhealthy = "unhealthy";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealth).AllowAnonymous().WithName("GetHealth");
        return app;
    }

    private static async Task<IResult> GetHealth(
        ScraperDbContext db, IConnectionMultiplexer redis, IConnectionFactory rabbit, RabbitMqQueueStats queue, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);
        var token = timeout.Token;

        var postgresOk = await Check(async () => await db.Database.CanConnectAsync(token));
        var redisOk = await Check(async () => { await redis.GetDatabase().PingAsync(); return true; });
        var rabbitOk = await Check(async () =>
        {
            await using var connection = await rabbit.CreateConnectionAsync(token);
            return connection.IsOpen;
        });
        var workers = redisOk ? await CountActiveWorkers(redis) : 0;
        var queueLength = rabbitOk ? await QueueLength(queue, token) : null;

        var allOk = postgresOk && redisOk && rabbitOk;
        var body = new HealthResponse(allOk ? Healthy : Unhealthy, State(postgresOk), State(redisOk), State(rabbitOk), workers, queueLength);
        return allOk ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // IServer.KeysAsync pages through the keyspace with SCAN, so it never blocks Redis the way KEYS does.
    private static async Task<int> CountActiveWorkers(IConnectionMultiplexer redis)
    {
        var count = 0;
        foreach (var endpoint in redis.GetEndPoints())
        {
            await foreach (var _ in redis.GetServer(endpoint).KeysAsync(pattern: "worker:heartbeat:*"))
            {
                count++;
            }
        }
        return count;
    }

    // Null when the work queue doesn't exist yet (no worker or API publish has declared it).
    private static async Task<long?> QueueLength(RabbitMqQueueStats queue, CancellationToken ct)
    {
        try
        {
            return await queue.GetQueueLengthAsync(ct);
        }
#pragma warning disable CA1031 // Queue length is informational; /health must not fail because of it.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static async Task<bool> Check(Func<Task<bool>> probe)
    {
        try
        {
            return await probe();
        }
#pragma warning disable CA1031 // Any failure means "unhealthy"; the endpoint itself must not fail.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private static string State(bool ok) => ok ? Healthy : Unhealthy;
}
