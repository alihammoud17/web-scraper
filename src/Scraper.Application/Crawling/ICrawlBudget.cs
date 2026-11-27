namespace Scraper.Application.Crawling;

/// <summary>The per-job page budget (<c>SiteConfig.MaxPages</c>), shared by all workers.</summary>
public interface ICrawlBudget
{
    /// <summary>Reserves up to <paramref name="count"/> pages; returns how many may be enqueued (0 when spent).</summary>
    Task<int> TryReserveAsync(Guid crawlJobId, int maxPages, int count, CancellationToken cancellationToken);
}
