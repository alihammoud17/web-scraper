using System.Text.Json;
using Scraper.Application.Crawling;

namespace Scraper.Application.Tests;

public class RetryPolicyTests
{
    private readonly RetryPolicy _policy = new(RetryOptions.Default); // 5 attempts; 5s, 30s, 2m, 10m

    [Theory]
    [InlineData(PoliteFetchKind.Fetched, 200)]
    [InlineData(PoliteFetchKind.NotModified, 304)]
    [InlineData(PoliteFetchKind.Redirected, 301)]
    public void Success_outcomes_succeed(PoliteFetchKind kind, int status)
    {
        var decision = _policy.Decide(Outcome(kind, status), attempt: 1);

        Assert.Equal(new TaskDecision.Succeed(PageRemoved: false), decision);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public void Gone_pages_succeed_and_are_marked_removed(int status)
    {
        Assert.Equal(new TaskDecision.Succeed(PageRemoved: true), _policy.Decide(Failed(status), attempt: 1));
    }

    [Theory]
    [InlineData(BlockReason.RobotsDisallowed, "robots.txt disallows this URL")]
    [InlineData(BlockReason.OutOfScope, "outside the site's crawl scope")]
    [InlineData(BlockReason.UnknownSite, "not a configured site")]
    public void Blocked_urls_are_skipped(BlockReason reason, string message)
    {
        Assert.Equal(new TaskDecision.Skip(message), _policy.Decide(PoliteFetchOutcome.Blocked(reason), attempt: 1));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Transient_statuses_are_retried(int status)
    {
        var decision = Assert.IsType<TaskDecision.Retry>(_policy.Decide(Failed(status), attempt: 1));

        Assert.Equal(TimeSpan.FromSeconds(5), decision.Delay);
        Assert.Equal($"HTTP {status}", decision.Error);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(405)]
    public void Other_client_errors_are_dead_lettered_at_once(int status)
    {
        var decision = Assert.IsType<TaskDecision.DeadLetter>(_policy.Decide(Failed(status), attempt: 1));

        Assert.Contains($"HTTP {status}", decision.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    [InlineData(4, 600)]
    public void Backoff_grows_with_each_attempt(int attempt, int seconds)
    {
        var decision = Assert.IsType<TaskDecision.Retry>(_policy.Decide(Failed(503), attempt));

        Assert.Equal(TimeSpan.FromSeconds(seconds), decision.Delay);
    }

    [Fact]
    public void The_last_attempt_is_dead_lettered()
    {
        var decision = Assert.IsType<TaskDecision.DeadLetter>(_policy.Decide(Failed(500), attempt: 5));

        Assert.Equal("gave up after 5 attempts: HTTP 500", decision.Reason);
    }

    [Theory]
    [InlineData(2, 5)]      // shorter than the backoff: the backoff tier wins
    [InlineData(90, 120)]   // the first tier that is long enough
    [InlineData(3600, 600)] // longer than every tier: the largest tier
    public void Retry_after_picks_the_first_tier_that_is_long_enough(int retryAfterSeconds, int tierSeconds)
    {
        var decision = Assert.IsType<TaskDecision.Retry>(_policy.Decide(Failed(429, TimeSpan.FromSeconds(retryAfterSeconds)), attempt: 1));

        Assert.Equal(TimeSpan.FromSeconds(tierSeconds), decision.Delay);
    }

    [Fact]
    public void Thrown_failures_are_retried_when_transient_and_dead_lettered_when_permanent()
    {
        Assert.IsType<TaskDecision.Retry>(_policy.DecideFailure("timeout", transient: true, attempt: 1));
        Assert.IsType<TaskDecision.DeadLetter>(_policy.DecideFailure("timeout", transient: true, attempt: 5));
        Assert.IsType<TaskDecision.DeadLetter>(_policy.DecideFailure("response too large", transient: false, attempt: 1));
    }

    [Fact]
    public void Options_are_read_from_settings()
    {
        var options = RetryOptions.From("4", "2, 4,8");

        Assert.Equal(4, options.MaxAttempts);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)], options.Delays);
        Assert.Equal(RetryOptions.Default.Delays, RetryOptions.From(null, "").Delays);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("30,5")]
    [InlineData("5,5")]
    public void Delays_must_be_positive_and_increasing(string delays)
    {
        Assert.Throws<ArgumentException>(() => RetryOptions.From("5", delays));
    }

    [Theory]
    [InlineData(5, "5s")]
    [InlineData(30, "30s")]
    [InlineData(120, "2m")]
    [InlineData(600, "10m")]
    [InlineData(90, "90s")]
    [InlineData(7200, "2h")]
    public void Tier_names_are_short_and_stable(int seconds, string name)
    {
        Assert.Equal(name, RetryOptions.TierName(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void A_task_message_round_trips_through_json()
    {
        var message = new CrawlTaskMessage(Guid.CreateVersion7(), Guid.CreateVersion7(), "testsite", "http://testsite:8080/pages/1", 2, 3);

        var json = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);

        Assert.Equal(message, JsonSerializer.Deserialize<CrawlTaskMessage>(json, JsonSerializerOptions.Web));
        Assert.Equal(4, message.NextAttempt().Attempt);
    }

    private static PoliteFetchOutcome Outcome(PoliteFetchKind kind, int status) =>
        new(kind, new FetchResult(status, new Dictionary<string, string>(), null, null, null, null, null, TimeSpan.Zero));

    private static PoliteFetchOutcome Failed(int status, TimeSpan? retryAfter = null) =>
        new(PoliteFetchKind.Failed, new FetchResult(status, new Dictionary<string, string>(), null, null, null, null, retryAfter, TimeSpan.Zero));
}
