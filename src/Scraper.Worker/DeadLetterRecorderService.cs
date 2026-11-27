using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Scraper.Application.Crawling;
using Scraper.Infrastructure.Messaging;

namespace Scraper.Worker;

/// <summary>
/// Consumes <c>crawl.dead</c> and writes a <c>dead_letter_entries</c> row for every message, whichever way it got
/// there: a worker gave up (reason in <c>x-reason</c>) or the broker dead-lettered it (delivery limit, rejection;
/// reason in <c>x-death</c>). The table is the queryable list an operator retries from (ADR 0010, ADR 0012).
/// </summary>
public sealed class DeadLetterRecorderService(
    RabbitMqConnection connection,
    RabbitMqTopology topology,
    IServiceScopeFactory scopes,
    ILoggerFactory loggers) : BackgroundService
{
    private static readonly TimeSpan PauseAfterFailure = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger = loggers.CreateLogger<DeadLetterRecorderService>();
    private RabbitMqConsumer? _consumer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var amqp = await connection.GetAsync(stoppingToken);
        _consumer = new RabbitMqConsumer(amqp, topology.DeadLetterQueue, loggers.CreateLogger<RabbitMqConsumer>());
        await _consumer.StartAsync(topology.DeclareCoreAsync, HandleAsync, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_consumer is not null)
        {
            await _consumer.StopAsync(TimeSpan.FromSeconds(5));
            await _consumer.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task HandleAsync(BasicDeliverEventArgs delivery, IChannel channel, CancellationToken cancellationToken)
    {
        CrawlTaskMessage message;
        try
        {
            message = RabbitMqTaskQueue.Deserialize(delivery.Body);
        }
        catch (System.Text.Json.JsonException exception)
        {
            // Nothing to record without a task id; drop it rather than loop on it.
            _logger.LogError(exception, "Unreadable dead-lettered message {DeliveryTag}; dropping it", delivery.DeliveryTag);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
            return;
        }

        var reason = DeadLetterReason.From(delivery.BasicProperties.Headers);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RecordDeadLetter>().ExecuteAsync(new DeadLetterNotice(message, reason), cancellationToken);
            _logger.LogWarning("Recorded dead letter for task {TaskId} {Url} after {Attempts} attempt(s): {Reason}",
                message.TaskId, message.Url, message.Attempt, reason);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Database briefly down: keep the message and try again later.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Could not record dead letter for task {TaskId}; retrying in {Pause}", message.TaskId, PauseAfterFailure);
            await Task.Delay(PauseAfterFailure, CancellationToken.None);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }
}
