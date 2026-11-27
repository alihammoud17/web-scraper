using System.Text;
using Scraper.Infrastructure.Messaging;

namespace Scraper.Infrastructure.Tests;

public class DeadLetterReasonTests
{
    [Fact]
    public void A_workers_own_reason_wins()
    {
        var headers = new Dictionary<string, object?>
        {
            [RabbitMqTaskQueue.ReasonHeader] = Encoding.UTF8.GetBytes("gave up after 4 attempts: HTTP 500"),
            ["x-death"] = Death("rejected"),
        };

        Assert.Equal("gave up after 4 attempts: HTTP 500", DeadLetterReason.From(headers));
    }

    [Theory]
    [InlineData("delivery_limit", "delivered 5+ times without an ack (workers kept crashing on it)")]
    [InlineData("rejected", "rejected by a worker after an unexpected error")]
    [InlineData("expired", "dead-lettered by the broker: expired")]
    public void Broker_reasons_are_explained(string brokerReason, string expected)
    {
        Assert.Equal(expected, DeadLetterReason.From(new Dictionary<string, object?> { ["x-death"] = Death(brokerReason) }));
    }

    [Fact]
    public void Missing_headers_give_unknown()
    {
        Assert.Equal("unknown", DeadLetterReason.From(null));
        Assert.Equal("unknown", DeadLetterReason.From(new Dictionary<string, object?>()));
    }

    private static List<object> Death(string reason) =>
        [new Dictionary<string, object> { ["reason"] = Encoding.UTF8.GetBytes(reason), ["count"] = 1L }];
}
