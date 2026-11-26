namespace Scraper.Domain.Crawling;

public enum CrawlJobStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed,
}
