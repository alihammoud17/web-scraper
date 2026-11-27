namespace Scraper.Application.Crawling;

/// <summary>
/// Classifies the result of one attempt (ADR 0012). Retryable: timeouts, connection errors, 408, 429, 500,
/// 502, 503, 504. Permanent: 404/410 (page removed), robots.txt / scope blocks (skipped), other 4xx and
/// oversized responses (dead-lettered at once). Retryable failures get <see cref="RetryOptions.MaxAttempts"/>
/// attempts in total; the delay honours Retry-After by picking the first tier that is long enough.
/// </summary>
public sealed class RetryPolicy(RetryOptions options)
{
    private static readonly int[] RetryableStatuses = [408, 429, 500, 502, 503, 504];

    public RetryOptions Options => options;

    public TaskDecision Decide(PoliteFetchOutcome outcome, int attempt)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        switch (outcome.Kind)
        {
            case PoliteFetchKind.Fetched or PoliteFetchKind.NotModified or PoliteFetchKind.Redirected:
                return new TaskDecision.Succeed();
            case PoliteFetchKind.Blocked:
                return new TaskDecision.Skip(outcome.BlockReason switch
                {
                    BlockReason.RobotsDisallowed => "robots.txt disallows this URL",
                    BlockReason.OutOfScope => "outside the site's crawl scope",
                    _ => "not a configured site",
                });
        }

        var status = outcome.Result?.StatusCode ?? 0;
        if (status is 404 or 410)
        {
            return new TaskDecision.Succeed(PageRemoved: true);
        }
        return RetryableStatuses.Contains(status)
            ? Retryable($"HTTP {status}", outcome.Result?.RetryAfter, attempt)
            : new TaskDecision.DeadLetter($"HTTP {status} (permanent)");
    }

    /// <summary>For failures thrown instead of returned (network, timeout, database, cache).</summary>
    public TaskDecision DecideFailure(string error, bool transient, int attempt) =>
        transient ? Retryable(error, retryAfter: null, attempt) : new TaskDecision.DeadLetter($"{error} (permanent)");

    /// <summary>
    /// The retry tier for a given attempt: the exponential backoff tier for that attempt, or a longer
    /// one if the server asked for more time. Above the largest tier, the largest tier is used.
    /// </summary>
    public TimeSpan DelayFor(int attempt, TimeSpan? retryAfter)
    {
        var delays = options.Delays;
        var backoff = delays[Math.Clamp(attempt - 1, 0, delays.Count - 1)];
        var wanted = retryAfter is { } requested && requested > backoff ? requested : backoff;
        return delays.FirstOrDefault(d => d >= wanted, delays[^1]);
    }

    private TaskDecision Retryable(string error, TimeSpan? retryAfter, int attempt) =>
        attempt >= options.MaxAttempts
            ? new TaskDecision.DeadLetter($"gave up after {attempt} attempts: {error}")
            : new TaskDecision.Retry(DelayFor(attempt, retryAfter), error);
}
