using System.Text;

namespace Scraper.Infrastructure.Messaging;

/// <summary>
/// Why a message reached the dead-letter queue. A worker that gives up sets <c>x-reason</c>; when the broker
/// dead-letters a message itself it adds <c>x-death</c> with a reason such as <c>delivery_limit</c> (the message
/// kept killing its workers) or <c>rejected</c> (a worker hit an unexpected error).
/// </summary>
public static class DeadLetterReason
{
    public static string From(IDictionary<string, object?>? headers)
    {
        if (headers is null)
        {
            return "unknown";
        }
        if (headers.TryGetValue(RabbitMqTaskQueue.ReasonHeader, out var reason) && Text(reason) is { Length: > 0 } given)
        {
            return given;
        }
        if (headers.TryGetValue("x-death", out var deaths) && deaths is IList<object> { Count: > 0 } list
            && list[0] is IDictionary<string, object> first && first.TryGetValue("reason", out var brokerReason))
        {
            return Text(brokerReason) switch
            {
                "delivery_limit" => $"delivered {RabbitMqTopology.DeliveryLimit}+ times without an ack (workers kept crashing on it)",
                "rejected" => "rejected by a worker after an unexpected error",
                var other => $"dead-lettered by the broker: {other}",
            };
        }
        return "unknown";
    }

    private static string? Text(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        string text => text,
        _ => value?.ToString(),
    };
}
