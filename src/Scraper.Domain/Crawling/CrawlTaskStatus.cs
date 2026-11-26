namespace Scraper.Domain.Crawling;

public enum CrawlTaskStatus
{
    Queued,
    InProgress,
    Succeeded,
    Failed,
    DeadLettered,
}
