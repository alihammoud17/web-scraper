using Scraper.Domain.Urls;

namespace Scraper.Domain.Crawling;

/// <summary>
/// One URL to crawl within one job. (job, URL) is unique, so enqueuing the same URL twice in a job
/// is a no-op even if Redis's seen set was lost (ADR 0004).
/// </summary>
public sealed class CrawlTask
{
    private CrawlTask()
    {
    }

    public Guid Id { get; private set; }

    public Guid CrawlJobId { get; private set; }

    public NormalizedUrl Url { get; private set; } = null!;

    /// <summary>Link hops from a seed URL (0 for seeds).</summary>
    public int Depth { get; private set; }

    public CrawlTaskStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>The page this task fetched; set once the page row exists.</summary>
    public Guid? PageId { get; private set; }

    public DateTimeOffset EnqueuedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static CrawlTask Enqueue(Guid crawlJobId, NormalizedUrl url, int depth, DateTimeOffset enqueuedAt)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        return new CrawlTask
        {
            Id = Guid.CreateVersion7(),
            CrawlJobId = crawlJobId,
            Url = url,
            Depth = depth,
            Status = CrawlTaskStatus.Queued,
            EnqueuedAt = enqueuedAt,
        };
    }

    /// <summary>A worker picked the task up (first attempt or a retry).</summary>
    public void BeginAttempt()
    {
        Require(CrawlTaskStatus.Queued, CrawlTaskStatus.Failed);
        Status = CrawlTaskStatus.InProgress;
        AttemptCount++;
    }

    public void Succeed(Guid pageId, DateTimeOffset at)
    {
        Require(CrawlTaskStatus.InProgress);
        Status = CrawlTaskStatus.Succeeded;
        PageId = pageId;
        LastError = null;
        CompletedAt = at;
    }

    /// <summary>The attempt failed but may be retried.</summary>
    public void Fail(string error)
    {
        Require(CrawlTaskStatus.InProgress);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Status = CrawlTaskStatus.Failed;
        LastError = error;
    }

    /// <summary>Retries are exhausted; the message went to the dead-letter queue.</summary>
    public void DeadLetter(string error, DateTimeOffset at)
    {
        Require(CrawlTaskStatus.InProgress, CrawlTaskStatus.Failed);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Status = CrawlTaskStatus.DeadLettered;
        LastError = error;
        CompletedAt = at;
    }

    private void Require(params CrawlTaskStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"Crawl task {Id} is {Status}; expected {string.Join(" or ", allowed)}.");
        }
    }
}
