using RabbitMQ.Client;
using Scraper.Infrastructure.Messaging;

namespace Scraper.Infrastructure.Tests;

/// <summary>
/// A test that needs a real RabbitMQ (quorum queues, TTL, dead-lettering and redelivery are broker behaviour).
/// Runs when <c>SCRAPER_TEST_RABBITMQ</c> holds an AMQP URI (CI sets it for its service container; locally
/// <c>amqp://user:pass@localhost:5673/</c>); skipped otherwise.
/// </summary>
public sealed class RabbitFactAttribute : FactAttribute
{
    public RabbitFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(TestRabbit.Uri))
        {
            Skip = $"Set {TestRabbit.Variable} to run RabbitMQ-backed tests.";
        }
    }
}

/// <summary>A topology with a unique prefix per test, deleted afterwards.</summary>
public sealed class TestRabbit : IAsyncDisposable
{
    public const string Variable = "SCRAPER_TEST_RABBITMQ";

    private readonly List<IConnection> _connections = [];

    public TestRabbit() => Topology = new RabbitMqTopology($"t{Guid.NewGuid():N}"[..13]);

    public static string? Uri => Environment.GetEnvironmentVariable(Variable);

    public RabbitMqTopology Topology { get; }

    public static ConnectionFactory Factory() => new() { Uri = new Uri(Uri!) };

    /// <summary>A new connection each call, so a test can "kill" one worker's connection and keep another.</summary>
    public async Task<IConnection> ConnectAsync()
    {
        var connection = await Factory().CreateConnectionAsync();
        _connections.Add(connection);
        return connection;
    }

    public async Task<uint> CountAsync(string queue)
    {
        await using var channel = await (await ConnectAsync()).CreateChannelAsync();
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    /// <summary>Waits until the queue holds <paramref name="count"/> ready messages, or fails after the timeout.</summary>
    public async Task WaitForCountAsync(string queue, uint count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (await CountAsync(queue) != count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{queue} still has {await CountAsync(queue)} messages, expected {count}.");
            }
            await Task.Delay(100);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var cleanup = await (await ConnectAsync()).CreateChannelAsync();
            foreach (var queue in new[] { Topology.WorkQueue, Topology.DeadLetterQueue, Topology.RetryQueue(TimeSpan.FromSeconds(1)) })
            {
                await cleanup.QueueDeleteAsync(queue);
            }
            foreach (var exchange in new[] { Topology.WorkExchange, Topology.RetryExchange, Topology.DeadLetterExchange })
            {
                await cleanup.ExchangeDeleteAsync(exchange);
            }
        }
        finally
        {
            foreach (var connection in _connections.Where(c => c.IsOpen))
            {
                await connection.CloseAsync();
            }
        }
    }
}
