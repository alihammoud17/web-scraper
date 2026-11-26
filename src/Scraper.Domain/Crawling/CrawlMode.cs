namespace Scraper.Domain.Crawling;

/// <summary>Full re-crawls everything; incremental uses ETag / Last-Modified to skip unchanged pages.</summary>
public enum CrawlMode
{
    Full,
    Incremental,
}
