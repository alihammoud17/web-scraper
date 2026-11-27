using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Scraper.Application.Crawling;
using Scraper.Infrastructure.Messaging;

namespace Scraper.Worker;

/// <summary>
/// Consumes <c>crawl.tasks</c> (prefetch 1, manual ack) and runs <see cref="ProcessCrawlTask"/> for each message,
/// in its own DI scope. The message is acked only after processing returns, i.e. after the database commit:
/// <list type="bullet">
/// <item>Ack → ack.</item>
/// <item>Retry → publish the next attempt to its delay tier (confirmed), then ack.</item>
/// <item>DeadLetter → publish to the dead-letter exchange (confirmed), then ack.</item>
/// <item>Unknown exception → nack without requeue: the broker dead-letters it (reason "rejected").</item>
/// </list>
/// SIGTERM stops gracefully (finish and ack the current task); SIGKILL leaves it unacked and the broker redelivers it.
/// </summary>
public sealed class CrawlTaskConsumerService(
    RabbitMqConnection connection,
    RabbitMqTopology topology,
    RabbitMqTaskQueue publisher,
    RetryOptions retry,
    IServiceScopeFactory scopes,
    IOptions<HostOptions> host,
    ILoggerFactory loggers) : BackgroundService
{
    private static readonly TimeSpan RequeuePause = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger = loggers.CreateLogger<CrawlTaskConsumerService>();
    private RabbitMqConsumer? _consumer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var amqp = await connection.GetAsync(stoppingToken);
        _consumer = new RabbitMqConsumer(amqp, topology.WorkQueue, loggers.CreateLogger<RabbitMqConsumer>());
        await _consumer.StartAsync(
            async (channel, ct) =>
            {
                await topology.DeclareCoreAsync(channel, ct);
                await topology.DeclareRetryTiersAsync(channel, retry.Delays, ct);
            },
            HandleAsync,
            stoppingToken);
        _logger.LogInformation("Consuming {Queue} (prefetch 1, manual ack); retry tiers {Tiers}, max attempts {MaxAttempts}",
            topology.WorkQueue, string.Join(", ", retry.Delays.Select(RetryOptions.TierName)), retry.MaxAttempts);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_consumer is not null)
        {
            // Leave a few seconds of the host's shutdown timeout for closing the connection.
            await _consumer.StopAsync(host.Value.ShutdownTimeout - TimeSpan.FromSeconds(5));
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
            _logger.LogError(exception, "Unreadable message {DeliveryTag}; rejecting it to the dead-letter queue", delivery.DeliveryTag);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            return;
        }

        _logger.LogInformation("Processing task {TaskId} {Url} attempt={Attempt} redelivered={Redelivered}",
            message.TaskId, message.Url, message.Attempt, delivery.Redelivered);

        TaskDisposition disposition;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            disposition = await scope.ServiceProvider.GetRequiredService<ProcessCrawlTask>().HandleAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown deadline: leave it unacked for redelivery
        }
#pragma warning disable CA1031 // An unexpected failure (a bug) must not crash the worker: reject the message instead.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "Task {TaskId} failed unexpectedly; rejecting it to the dead-letter queue", message.TaskId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            return;
        }

        // Publishing uses its own confirmed channel; only then is this delivery acked.
        try
        {
            switch (disposition)
            {
                case TaskDisposition.Retry retryLater:
                    await publisher.PublishRetryAsync(message, retryLater.Delay, retryLater.Error, CancellationToken.None);
                    _logger.LogInformation("Task {TaskId} will retry in {Tier} (attempt {Next}): {Error}",
                        message.TaskId, RetryOptions.TierName(retryLater.Delay), message.Attempt + 1, retryLater.Error);
                    break;
                case TaskDisposition.DeadLetter deadLetter:
                    await publisher.PublishDeadLetterAsync(message, deadLetter.Reason, CancellationToken.None);
                    _logger.LogWarning("Task {TaskId} dead-lettered: {Reason}", message.TaskId, deadLetter.Reason);
                    break;
                default:
                    _logger.LogInformation("Task {TaskId} done", message.TaskId);
                    break;
            }
        }
#pragma warning disable CA1031 // If the retry/dead-letter publish fails, hand the message back instead of leaving it stuck unacked.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Could not publish the outcome of task {TaskId}; requeueing the message", message.TaskId);
            // A requeue doesn't count toward the delivery limit (RabbitMQ 4), so pause to avoid a hot loop.
            await Task.Delay(RequeuePause, CancellationToken.None);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            return;
        }
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
    }
}
