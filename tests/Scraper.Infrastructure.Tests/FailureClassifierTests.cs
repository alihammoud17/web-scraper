using Polly.Timeout;
using Scraper.Application.Crawling;
using Scraper.Infrastructure.Crawling;
using StackExchange.Redis;

namespace Scraper.Infrastructure.Tests;

public class FailureClassifierTests
{
    private readonly FailureClassifier _classifier = new();

    public static TheoryData<Exception, FailureKind> Cases() => new()
    {
        { new HttpRequestException("connection reset"), FailureKind.Transient },
        { new TimeoutRejectedException(), FailureKind.Transient },
        { new TaskCanceledException("attempt timed out"), FailureKind.Transient },
        { new RedisTimeoutException(CommandFlags.None, "redis timeout", CommandStatus.Unknown), FailureKind.Transient },
        { new InvalidOperationException("wrapper", new HttpRequestException("inner")), FailureKind.Transient },
        { new ResponseTooLargeException(new Uri("http://testsite:8080/big"), 10), FailureKind.Permanent },
        { new UnknownSiteException("other.example"), FailureKind.Permanent },
        { new NotSupportedException("bug"), FailureKind.Unknown },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classifies_failures(Exception exception, FailureKind expected)
    {
        Assert.Equal(expected, _classifier.Classify(exception));
    }
}
