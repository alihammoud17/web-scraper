using Scraper.Domain.Urls;

namespace Scraper.Domain.Crawling;

/// <summary>
/// A queryable copy of a dead-lettered message, so the dashboard can list and re-queue failures
/// (RabbitMQ's DLQ itself can't be queried or joined). One entry per crawl task.
/// </summary>
public sealed class DeadLetterEntry
{
    private DeadLetterEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid CrawlTaskId { get; private set; }

    public string SiteId { get; private set; } = null!;

    public NormalizedUrl Url { get; private set; } = null!;

    public string LastError { get; private set; } = null!;

    public int AttemptCount { get; private set; }

    public DateTimeOffset DeadLetteredAt { get; private set; }

    /// <summary>When an operator re-queued the task; null while the entry is still open.</summary>
    public DateTimeOffset? RetriedAt { get; private set; }

    public void MarkRetried(DateTimeOffset at) => RetriedAt = at;

    public static DeadLetterEntry Record(
        Guid crawlTaskId, string siteId, NormalizedUrl url, string lastError, int attemptCount, DateTimeOffset deadLetteredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastError);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptCount);
        return new DeadLetterEntry
        {
            Id = Guid.CreateVersion7(),
            CrawlTaskId = crawlTaskId,
            SiteId = siteId,
            Url = url,
            LastError = lastError,
            AttemptCount = attemptCount,
            DeadLetteredAt = deadLetteredAt,
        };
    }
}
