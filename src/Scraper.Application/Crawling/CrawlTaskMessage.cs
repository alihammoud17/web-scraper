namespace Scraper.Application.Crawling;

/// <summary>
/// The body of a message on the work queue: one URL of one crawl job. <see cref="Attempt"/> is 1 for the
/// first try and grows with every delayed retry; it is ours, not the broker's delivery count.
/// </summary>
public sealed record CrawlTaskMessage(Guid TaskId, Guid JobId, string SiteId, string Url, int Depth, int Attempt)
{
    public CrawlTaskMessage NextAttempt() => this with { Attempt = Attempt + 1 };
}
