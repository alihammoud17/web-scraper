using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Scraper.Infrastructure.Messaging;

/// <summary>
/// One consumer on one queue with <b>prefetch 1</b> and <b>manual acks</b>: the broker hands this worker one
/// message at a time and keeps it (unacked) until the handler acks it. If the process dies, the connection
/// drops and the broker redelivers that message to another consumer (ADR 0012).
/// <para>
/// The handler acks or nacks through the channel it is given. <see cref="StopAsync"/> is the graceful shutdown:
/// cancel the subscription (no new deliveries), let the in-flight message finish and ack, then close; if the
/// deadline passes first, processing is cancelled without an ack, so the message is redelivered.
/// </para>
/// </summary>
public sealed class RabbitMqConsumer(IConnection connection, string queue, ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _processing = new();
    private readonly object _gate = new();
    private IChannel? _channel;
    private string? _consumerTag;
    private Task _inFlight = Task.CompletedTask;

    /// <summary>Declares (via <paramref name="declare"/>) and starts consuming.</summary>
    public async Task StartAsync(
        Func<IChannel, CancellationToken, Task> declare,
        Func<BasicDeliverEventArgs, IChannel, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declare);
        ArgumentNullException.ThrowIfNull(handler);
        _channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await declare(_channel, cancellationToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var work = handler(delivery, _channel, _processing.Token);
            lock (_gate)
            {
                _inFlight = work;
            }
            try
            {
                await work;
            }
            catch (OperationCanceledException) when (_processing.IsCancellationRequested)
            {
                logger.LogWarning("Shutdown deadline reached; message {DeliveryTag} left unacked and will be redelivered", delivery.DeliveryTag);
            }
        };
        _consumerTag = await _channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken);
    }

    /// <summary>Graceful stop: no new deliveries, finish (and ack) the current one, then close.</summary>
    public async Task StopAsync(TimeSpan deadline)
    {
        if (_channel is null || _consumerTag is null)
        {
            return;
        }
        await _channel.BasicCancelAsync(_consumerTag);

        Task inFlight;
        lock (_gate)
        {
            inFlight = _inFlight;
        }
        if (!inFlight.IsCompleted)
        {
            logger.LogInformation("Stopping: finishing the in-flight task first (up to {Deadline})", deadline);
            if (await Task.WhenAny(inFlight, Task.Delay(deadline)) != inFlight)
            {
                await _processing.CancelAsync(); // abandon it: no ack, the broker redelivers it
            }
            await inFlight.ContinueWith(_ => { }, TaskScheduler.Default);
            if (!_processing.IsCancellationRequested)
            {
                logger.LogInformation("Finished in-flight task, acked; stopping");
            }
        }
        await _channel.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
        _processing.Dispose();
    }
}
