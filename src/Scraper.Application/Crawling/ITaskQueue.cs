namespace Scraper.Application.Crawling;

/// <summary>The work queue. Publishing returns only after the broker confirmed every message.</summary>
public interface ITaskQueue
{
    Task PublishAsync(IReadOnlyList<CrawlTaskMessage> messages, CancellationToken cancellationToken);
}
