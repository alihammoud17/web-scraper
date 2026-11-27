namespace Scraper.Infrastructure.Messaging;

/// <summary>Ready messages in the work queue (not counting unacked ones being processed), for <c>/health</c>.</summary>
public sealed class RabbitMqQueueStats(RabbitMqConnection connection, RabbitMqTopology topology)
{
    public async Task<long> GetQueueLengthAsync(CancellationToken cancellationToken)
    {
        var amqp = await connection.GetAsync(cancellationToken);
        await using var channel = await amqp.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await channel.QueueDeclarePassiveAsync(topology.WorkQueue, cancellationToken);
        return queue.MessageCount;
    }
}
