using StackExchange.Redis;

namespace Scraper.Worker;

/// <summary>
/// Writes <c>worker:heartbeat:{hostname}</c> every 5 s with a 15 s TTL. A worker that dies stops
/// refreshing its key, so it disappears from the API's activeWorkers count within 15 s.
/// </summary>
public sealed class HeartbeatService(IConnectionMultiplexer redis, ILogger<HeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // In Docker the hostname is the container ID, so each scaled replica gets its own key.
        var key = $"worker:heartbeat:{Environment.MachineName}";
        logger.LogInformation("Worker started; heartbeat key {Key}", key);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await redis.GetDatabase().StringSetAsync(key, DateTimeOffset.UtcNow.ToString("O"), Ttl);
            }
            catch (RedisException ex)
            {
                logger.LogWarning(ex, "Heartbeat write failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
