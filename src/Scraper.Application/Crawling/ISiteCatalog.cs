using Scraper.Domain.Sites;

namespace Scraper.Application.Crawling;

/// <summary>The configured sites, looked up by authority (host plus non-default port).</summary>
public interface ISiteCatalog
{
    Task<SiteConfig?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken);

    Task<SiteConfig?> FindByIdAsync(string siteId, CancellationToken cancellationToken);
}
