using RabbitMQ.Client;

namespace Scraper.Infrastructure.Messaging;

/// <summary>
/// One shared AMQP connection per process (connections are expensive; channels are cheap). The client's
/// automatic recovery reconnects after a broker restart. Named after the host, so the management UI shows
/// which worker container holds which connection.
/// </summary>
public sealed class RabbitMqConnection(IConnectionFactory factory) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } current)
            {
                return current;
            }
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
            _connection = await factory.CreateConnectionAsync(Environment.MachineName, cancellationToken);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        _lock.Dispose();
    }
}
