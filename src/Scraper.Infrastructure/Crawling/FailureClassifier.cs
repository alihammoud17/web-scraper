using Npgsql;
using Polly.Timeout;
using RabbitMQ.Client.Exceptions;
using Scraper.Application.Crawling;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// Transient: network errors, timeouts, and the database, cache or broker being briefly unavailable — worth a
/// delayed retry. Permanent: responses over the size limit and hosts that aren't configured sites. Anything
/// else is unknown (probably a bug) and gets rejected to the dead-letter queue instead of retried in a loop.
/// </summary>
public sealed class FailureClassifier : IFailureClassifier
{
    public FailureKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ResponseTooLargeException or UnknownSiteException => FailureKind.Permanent,
            HttpRequestException or TimeoutRejectedException or TimeoutException or OperationCanceledException => FailureKind.Transient,
            NpgsqlException { IsTransient: true } => FailureKind.Transient,
            RedisConnectionException or RedisTimeoutException => FailureKind.Transient,
            BrokerUnreachableException or AlreadyClosedException or OperationInterruptedException => FailureKind.Transient,
            _ when exception.InnerException is { } inner => Classify(inner),
            _ => FailureKind.Unknown,
        };
    }
}
