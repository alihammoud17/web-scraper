using RabbitMQ.Client;
using Scraper.Application.Crawling;
using Scraper.Infrastructure.Messaging;

namespace Scraper.Infrastructure.Tests;

public sealed class RabbitMqTopologyTests : IAsyncLifetime
{
    private static readonly TimeSpan OneSecondTier = TimeSpan.FromSeconds(1);

    private readonly TestRabbit _rabbit = new();
    private RabbitMqConnection? _connection;

    [RabbitFact]
    public async Task Declaring_the_topology_twice_is_harmless()
    {
        await using var channel = await (await _rabbit.ConnectAsync()).CreateChannelAsync();

        await _rabbit.Topology.DeclareCoreAsync(channel, CancellationToken.None);
        await _rabbit.Topology.DeclareCoreAsync(channel, CancellationToken.None);
        await _rabbit.Topology.DeclareRetryTiersAsync(channel, [OneSecondTier], CancellationToken.None);
        await _rabbit.Topology.DeclareRetryTiersAsync(channel, [OneSecondTier], CancellationToken.None);

        Assert.Equal(0u, await _rabbit.CountAsync(_rabbit.Topology.WorkQueue));
        Assert.Equal(0u, await _rabbit.CountAsync(_rabbit.Topology.DeadLetterQueue));
    }

    [RabbitFact]
    public async Task Published_tasks_are_confirmed_persistent_and_land_in_the_work_queue()
    {
        var queue = Queue();
        var message = Message();

        await queue.PublishAsync([message], CancellationToken.None);

        await using var channel = await (await _rabbit.ConnectAsync()).CreateChannelAsync();
        var delivery = await channel.BasicGetAsync(_rabbit.Topology.WorkQueue, autoAck: true);
        Assert.NotNull(delivery);
        Assert.Equal(message, RabbitMqTaskQueue.Deserialize(delivery.Body));
        Assert.True(delivery.BasicProperties.Persistent);
        Assert.Equal($"{message.TaskId}:1", delivery.BasicProperties.MessageId);
    }

    [RabbitFact]
    public async Task A_retry_waits_in_its_tier_then_returns_to_the_work_queue_as_the_next_attempt()
    {
        var queue = Queue();
        await DeclareTierAsync();

        await queue.PublishRetryAsync(Message(), OneSecondTier, "HTTP 503", CancellationToken.None);

        Assert.Equal(1u, await _rabbit.CountAsync(_rabbit.Topology.RetryQueue(OneSecondTier)));
        Assert.Equal(0u, await _rabbit.CountAsync(_rabbit.Topology.WorkQueue));
        await _rabbit.WaitForCountAsync(_rabbit.Topology.WorkQueue, 1, TimeSpan.FromSeconds(10));

        await using var channel = await (await _rabbit.ConnectAsync()).CreateChannelAsync();
        var delivery = await channel.BasicGetAsync(_rabbit.Topology.WorkQueue, autoAck: true);
        Assert.Equal(2, RabbitMqTaskQueue.Deserialize(delivery!.Body).Attempt);
    }

    [RabbitFact]
    public async Task A_dead_letter_carries_its_reason_to_the_dead_letter_queue()
    {
        var queue = Queue();

        await queue.PublishDeadLetterAsync(Message() with { Attempt = 4 }, "gave up after 4 attempts: HTTP 500", CancellationToken.None);

        await using var channel = await (await _rabbit.ConnectAsync()).CreateChannelAsync();
        var delivery = await channel.BasicGetAsync(_rabbit.Topology.DeadLetterQueue, autoAck: true);
        Assert.NotNull(delivery);
        Assert.Equal("gave up after 4 attempts: HTTP 500", System.Text.Encoding.UTF8.GetString((byte[])delivery.BasicProperties.Headers![RabbitMqTaskQueue.ReasonHeader]!));
        Assert.Equal(4, RabbitMqTaskQueue.Deserialize(delivery.Body).Attempt);
    }

    [RabbitFact]
    public async Task Publishing_to_a_tier_that_does_not_exist_fails_instead_of_losing_the_message()
    {
        var queue = Queue(); // the 1 s tier was never declared: the message would be unroutable

        await Assert.ThrowsAnyAsync<Exception>(() => queue.PublishRetryAsync(Message(), OneSecondTier, "HTTP 503", CancellationToken.None));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        if (TestRabbit.Uri is not null)
        {
            await _rabbit.DisposeAsync();
        }
    }

    private RabbitMqTaskQueue Queue()
    {
        _connection ??= new RabbitMqConnection(TestRabbit.Factory());
        return new RabbitMqTaskQueue(_connection, _rabbit.Topology);
    }

    private async Task DeclareTierAsync()
    {
        await using var channel = await (await _rabbit.ConnectAsync()).CreateChannelAsync();
        await _rabbit.Topology.DeclareCoreAsync(channel, CancellationToken.None);
        await _rabbit.Topology.DeclareRetryTiersAsync(channel, [OneSecondTier], CancellationToken.None);
    }

    private static CrawlTaskMessage Message() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "testsite", "http://testsite:8080/pages/1", 0, 1);
}
