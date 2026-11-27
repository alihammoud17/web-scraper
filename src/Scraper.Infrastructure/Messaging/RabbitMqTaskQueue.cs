using System.Text.Json;
using RabbitMQ.Client;
using Scraper.Application.Crawling;

namespace Scraper.Infrastructure.Messaging;

/// <summary>
/// Publishes task messages with publisher confirms: every publish waits until the broker has the message (and,
/// with <c>mandatory</c>, until it was routed to a queue), and throws otherwise. Messages are persistent.
/// </summary>
public sealed class RabbitMqTaskQueue(RabbitMqConnection connection, RabbitMqTopology topology) : ITaskQueue, IAsyncDisposable
{
    public const string AttemptHeader = "x-attempt";
    public const string ReasonHeader = "x-reason";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(IReadOnlyList<CrawlTaskMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        foreach (var message in messages)
        {
            await PublishAsync(topology.WorkExchange, RabbitMqTopology.TaskRoutingKey, message, reason: null, cancellationToken);
        }
    }

    /// <summary>The next attempt goes to the retry tier for <paramref name="delay"/> and returns to the work queue when it expires.</summary>
    public Task PublishRetryAsync(CrawlTaskMessage message, TimeSpan delay, string error, CancellationToken cancellationToken) =>
        PublishAsync(topology.RetryExchange, RabbitMqTopology.TierRoutingKey(delay), message.NextAttempt(), error, cancellationToken);

    /// <summary>Gives up on the task: it goes to the dead-letter queue, where the recorder writes the entry.</summary>
    public Task PublishDeadLetterAsync(CrawlTaskMessage message, string reason, CancellationToken cancellationToken) =>
        PublishAsync(topology.DeadLetterExchange, string.Empty, message, reason, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
        _lock.Dispose();
    }

    public static byte[] Serialize(CrawlTaskMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);

    public static CrawlTaskMessage Deserialize(ReadOnlyMemory<byte> body) =>
        JsonSerializer.Deserialize<CrawlTaskMessage>(body.Span, JsonSerializerOptions.Web)
            ?? throw new JsonException("Empty task message.");

    private async Task PublishAsync(string exchange, string routingKey, CrawlTaskMessage message, string? reason, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = $"{message.TaskId}:{message.Attempt}",
            Headers = new Dictionary<string, object?> { [AttemptHeader] = message.Attempt },
        };
        if (reason is not null)
        {
            properties.Headers[ReasonHeader] = reason;
        }

        // One channel, used by one publisher at a time; the confirm is awaited inside BasicPublishAsync.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, Serialize(message), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
        var amqp = await connection.GetAsync(cancellationToken);
        _channel = await amqp.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await topology.DeclareCoreAsync(_channel, cancellationToken);
        return _channel;
    }
}
