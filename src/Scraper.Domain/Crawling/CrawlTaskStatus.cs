namespace Scraper.Domain.Crawling;

public enum CrawlTaskStatus
{
    Queued,
    InProgress,
    Succeeded,
    Failed,
    DeadLettered,

    /// <summary>Never fetched on purpose: disallowed by robots.txt, out of scope or not a configured site.</summary>
    Skipped,
}
