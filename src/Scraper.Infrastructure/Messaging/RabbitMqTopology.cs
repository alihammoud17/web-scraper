using RabbitMQ.Client;
using Scraper.Application.Crawling;

namespace Scraper.Infrastructure.Messaging;

/// <summary>
/// Exchange and queue names plus their declaration (ADR 0012). Declaring is idempotent, so the API and every
/// worker declare on start-up. The prefix (default "crawl") lets tests use their own isolated topology.
/// <code>
/// crawl (direct) --task--> crawl.tasks (quorum, delivery limit 5, DLX crawl.dlx)
/// crawl.retry (direct) --5s--> crawl.retry.5s (TTL 5 s, DLX crawl/task) ... one queue per tier
/// crawl.dlx (fanout) --> crawl.dead (quorum)
/// </code>
/// </summary>
public sealed class RabbitMqTopology(string prefix = "crawl")
{
    /// <summary>Deliveries of one message (crash, nack) before the broker dead-letters it: guards against crash loops.</summary>
    public const int DeliveryLimit = 5;

    public const string TaskRoutingKey = "task";

    public string WorkExchange => prefix;

    public string WorkQueue => $"{prefix}.tasks";

    public string RetryExchange => $"{prefix}.retry";

    public string DeadLetterExchange => $"{prefix}.dlx";

    public string DeadLetterQueue => $"{prefix}.dead";

    public static string TierRoutingKey(TimeSpan delay) => RetryOptions.TierName(delay);

    public string RetryQueue(TimeSpan delay) => $"{prefix}.retry.{TierRoutingKey(delay)}";

    /// <summary>The work queue, its exchange, the dead-letter exchange and the dead-letter queue.</summary>
    public async Task DeclareCoreAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        await channel.ExchangeDeclareAsync(WorkExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: string.Empty, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(WorkQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: DeadLetteringQueue(DeadLetterExchange, deadLetterRoutingKey: null, extra: new() { ["x-delivery-limit"] = DeliveryLimit }),
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(WorkQueue, WorkExchange, TaskRoutingKey, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// One queue per delay with that TTL and no consumers: an expired message is dead-lettered back to the work
    /// queue. Every message in a tier has the same TTL, so expiry follows FIFO order: no head-of-line blocking.
    /// </summary>
    public async Task DeclareRetryTiersAsync(IChannel channel, IEnumerable<TimeSpan> delays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(delays);
        await channel.ExchangeDeclareAsync(RetryExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        foreach (var delay in delays)
        {
            await channel.QueueDeclareAsync(RetryQueue(delay), durable: true, exclusive: false, autoDelete: false,
                arguments: DeadLetteringQueue(WorkExchange, TaskRoutingKey, extra: new() { ["x-message-ttl"] = (long)delay.TotalMilliseconds }),
                cancellationToken: cancellationToken);
            await channel.QueueBindAsync(RetryQueue(delay), RetryExchange, TierRoutingKey(delay), cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Quorum queue whose dead-lettering is at-least-once: a message leaves only once the target queue has
    /// confirmed it, so nothing is lost between queues (this mode requires <c>x-overflow: reject-publish</c>).
    /// </summary>
    private static Dictionary<string, object?> DeadLetteringQueue(string deadLetterExchange, string? deadLetterRoutingKey, Dictionary<string, object?> extra)
    {
        var arguments = new Dictionary<string, object?>(extra)
        {
            ["x-queue-type"] = "quorum",
            ["x-dead-letter-exchange"] = deadLetterExchange,
            ["x-dead-letter-strategy"] = "at-least-once",
            ["x-overflow"] = "reject-publish",
        };
        if (deadLetterRoutingKey is not null)
        {
            arguments["x-dead-letter-routing-key"] = deadLetterRoutingKey;
        }
        return arguments;
    }
}
