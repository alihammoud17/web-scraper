using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Scraper.Infrastructure.Messaging;

namespace Scraper.Infrastructure.Tests;

/// <summary>Broker behaviour the fault tolerance relies on, against a real RabbitMQ (ADR 0012).</summary>
public sealed class RabbitMqConsumerTests : IAsyncLifetime
{
    private readonly TestRabbit _rabbit = new();

    [RabbitFact]
    public async Task An_unacked_message_is_redelivered_to_another_worker_when_the_first_one_dies()
    {
        await PublishAsync("task-1");
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var killed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionA = await _rabbit.ConnectAsync();
        var workerA = new RabbitMqConsumer(connectionA, _rabbit.Topology.WorkQueue, NullLogger.Instance);
        await workerA.StartAsync(Declare, async (delivery, _, _) =>
        {
            received.TrySetResult(delivery.Redelivered);
            await killed.Task; // busy with the task when the worker dies: never acks
        }, CancellationToken.None);
        Assert.False(await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        // Like docker kill: the connection just disappears. (In-process, the client waits for the running
        // handler before finishing the abort, so the fake handler is released as the abort starts.)
        var abort = connectionA.AbortAsync();
        killed.SetResult();
        await abort;

        var redelivered = new TaskCompletionSource<(string Body, bool Redelivered)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerB = new RabbitMqConsumer(await _rabbit.ConnectAsync(), _rabbit.Topology.WorkQueue, NullLogger.Instance);
        await workerB.StartAsync(Declare, async (delivery, channel, _) =>
        {
            redelivered.TrySetResult((Encoding.UTF8.GetString(delivery.Body.Span), delivery.Redelivered));
            await channel.BasicAckAsync(delivery.DeliveryTag, false);
        }, CancellationToken.None);

        var result = await redelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(("task-1", true), result);
        await workerB.StopAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0u, await _rabbit.CountAsync(_rabbit.Topology.WorkQueue));
    }

    [RabbitFact]
    public async Task With_prefetch_one_a_worker_holds_only_one_message_at_a_time()
    {
        await PublishAsync("a");
        await PublishAsync("b");
        var deliveries = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new RabbitMqConsumer(await _rabbit.ConnectAsync(), _rabbit.Topology.WorkQueue, NullLogger.Instance);
        await worker.StartAsync(Declare, async (_, _, ct) =>
        {
            Interlocked.Increment(ref deliveries);
            first.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }, CancellationToken.None);

        await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);

        Assert.Equal(1, deliveries);
        Assert.Equal(1u, await _rabbit.CountAsync(_rabbit.Topology.WorkQueue)); // the other one waits, ready for another worker
        await worker.StopAsync(TimeSpan.FromMilliseconds(100));
    }

    [RabbitFact]
    public async Task A_message_that_keeps_killing_its_worker_is_dead_lettered_at_the_delivery_limit()
    {
        await PublishAsync("poison");

        // A crash loop: each "worker" receives the message and dies before acking it.
        for (var crash = 0; crash <= RabbitMqTopology.DeliveryLimit; crash++)
        {
            var connection = await _rabbit.ConnectAsync();
            var channel = await connection.CreateChannelAsync();
            BasicGetResult? delivery = null;
            for (var wait = 0; delivery is null && wait < 50; wait++)
            {
                delivery = await channel.BasicGetAsync(_rabbit.Topology.WorkQueue, autoAck: false);
                if (delivery is null)
                {
                    await Task.Delay(100);
                }
            }
            Assert.NotNull(delivery);
            await connection.AbortAsync();
        }

        await _rabbit.WaitForCountAsync(_rabbit.Topology.DeadLetterQueue, 1, TimeSpan.FromSeconds(10));
        Assert.Equal(0u, await _rabbit.CountAsync(_rabbit.Topology.WorkQueue));
        Assert.Equal("delivery_limit", await DeathReasonAsync());
    }

    [RabbitFact]
    public async Task A_nack_with_requeue_does_not_count_toward_the_delivery_limit()
    {
        // RabbitMQ 4: only deliveries that come back because the consumer died count; an explicit requeue doesn't.
        await PublishAsync("requeued");
        var deliveries = 0;
        var acked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new RabbitMqConsumer(await _rabbit.ConnectAsync(), _rabbit.Topology.WorkQueue, NullLogger.Instance);
        await worker.StartAsync(Declare, async (delivery, channel, _) =>
        {
            if (Interlocked.Increment(ref deliveries) <= RabbitMqTopology.DeliveryLimit + 2)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: true);
            }
            else
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, false);
                acked.TrySetResult();
            }
        }, CancellationToken.None);

        await acked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(RabbitMqTopology.DeliveryLimit + 3, deliveries);
        Assert.Equal(0u, await _rabbit.CountAsync(_rabbit.Topology.DeadLetterQueue));
    }

    [RabbitFact]
    public async Task A_rejected_message_goes_straight_to_the_dead_letter_queue()
    {
        await PublishAsync("bug");
        var worker = new RabbitMqConsumer(await _rabbit.ConnectAsync(), _rabbit.Topology.WorkQueue, NullLogger.Instance);
        await worker.StartAsync(Declare, (delivery, channel, _) =>
            channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: false).AsTask(), CancellationToken.None);

        await _rabbit.WaitForCountAsync(_rabbit.Topology.DeadLetterQueue, 1, TimeSpan.FromSeconds(10));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("rejected", await DeathReasonAsync());
    }

    [RabbitFact]
    public async Task A_graceful_stop_finishes_and_acks_the_in_flight_message()
    {
        await PublishAsync("in-flight");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acked = false;
        var worker = new RabbitMqConsumer(await _rabbit.ConnectAsync(), _rabbit.Topology.WorkQueue, NullLogger.Instance);
        await worker.StartAsync(Declare, async (delivery, channel, _) =>
        {
            started.TrySetResult();
            await Task.Delay(1000); // still working when SIGTERM arrives
            await channel.BasicAckAsync(delivery.DeliveryTag, false);
            acked = true;
        }, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await worker.StopAsync(TimeSpan.FromSeconds(10));

        Assert.True(acked);
        Assert.Equal(0u, await _rabbit.CountAsync(_rabbit.Topology.WorkQueue)); // nothing to redeliver
    }

    [RabbitFact]
    public async Task A_stop_past_its_deadline_leaves_the_message_for_redelivery()
    {
        await PublishAsync("too-slow");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new RabbitMqConsumer(await _rabbit.ConnectAsync(), _rabbit.Topology.WorkQueue, NullLogger.Instance);
        await worker.StartAsync(Declare, async (_, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await worker.StopAsync(TimeSpan.FromMilliseconds(300));

        await _rabbit.WaitForCountAsync(_rabbit.Topology.WorkQueue, 1, TimeSpan.FromSeconds(10));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (TestRabbit.Uri is not null)
        {
            await _rabbit.DisposeAsync();
        }
    }

    private Task Declare(IChannel channel, CancellationToken cancellationToken) => _rabbit.Topology.DeclareCoreAsync(channel, cancellationToken);

    private async Task PublishAsync(string body)
    {
        await using var channel = await (await _rabbit.ConnectAsync()).CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        await _rabbit.Topology.DeclareCoreAsync(channel, CancellationToken.None);
        await channel.BasicPublishAsync(_rabbit.Topology.WorkExchange, RabbitMqTopology.TaskRoutingKey, mandatory: true,
            new BasicProperties { Persistent = true }, Encoding.UTF8.GetBytes(body));
    }

    private async Task<string> DeathReasonAsync()
    {
        await using var channel = await (await _rabbit.ConnectAsync()).CreateChannelAsync();
        var dead = await channel.BasicGetAsync(_rabbit.Topology.DeadLetterQueue, autoAck: true);
        var deaths = (IList<object>)dead!.BasicProperties.Headers!["x-death"]!;
        var first = (IDictionary<string, object>)deaths[0];
        return Encoding.UTF8.GetString((byte[])first["reason"]);
    }
}
