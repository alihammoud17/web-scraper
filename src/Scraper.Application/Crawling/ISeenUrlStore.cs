using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

/// <summary>The set of URLs already discovered in a crawl job, shared by all workers.</summary>
public interface ISeenUrlStore
{
    /// <summary>Marks the URL as seen in this job; true only for the first caller (atomic check-and-mark).</summary>
    Task<bool> TryMarkSeenAsync(Guid crawlJobId, NormalizedUrl url, CancellationToken cancellationToken);

    /// <summary>The URLs not seen yet in this job (read-only; a fast filter before the database insert).</summary>
    Task<IReadOnlyList<NormalizedUrl>> FilterUnseenAsync(Guid crawlJobId, IReadOnlyList<NormalizedUrl> urls, CancellationToken cancellationToken);

    /// <summary>Marks URLs as seen; called after the database commit, because Postgres, not Redis, is the arbiter.</summary>
    Task MarkSeenAsync(Guid crawlJobId, IReadOnlyList<NormalizedUrl> urls, CancellationToken cancellationToken);
}
