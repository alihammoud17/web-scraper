using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

/// <summary>The set of URLs already discovered in a crawl job, shared by all workers.</summary>
public interface ISeenUrlStore
{
    /// <summary>Marks the URL as seen in this job; true only for the first caller (atomic check-and-mark).</summary>
    Task<bool> TryMarkSeenAsync(Guid crawlJobId, NormalizedUrl url, CancellationToken cancellationToken);
}
