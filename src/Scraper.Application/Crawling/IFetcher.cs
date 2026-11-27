namespace Scraper.Application.Crawling;

/// <summary>
/// Fetches one URL over HTTP. Implementations handle retries, timeouts and rate limiting; transport
/// failures that survive the retries are thrown, so the caller can re-queue the URL.
/// </summary>
public interface IFetcher
{
    Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken);
}
