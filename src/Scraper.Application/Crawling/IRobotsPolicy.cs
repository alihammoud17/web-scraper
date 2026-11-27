using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

/// <summary>Answers "may our crawler fetch this URL?" from the site's robots.txt (RFC 9309).</summary>
public interface IRobotsPolicy
{
    Task<bool> IsAllowedAsync(NormalizedUrl url, CancellationToken cancellationToken);
}
