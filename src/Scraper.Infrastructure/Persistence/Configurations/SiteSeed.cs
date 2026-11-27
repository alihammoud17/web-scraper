using Scraper.Domain.Sites;
using Scraper.Domain.Urls;

namespace Scraper.Infrastructure.Persistence.Configurations;

/// <summary>
/// The three target sites, seeded by migration. Rates are deliberately conservative (see
/// docs/compliance/sites.md); seed URLs and limits may be tuned in a later migration once the
/// crawler and parsers exist.
/// </summary>
internal static class SiteSeed
{
    public static readonly SiteConfig[] All =
    [
        Site("gtacars", "GTACars.net", "https://gtacars.net/", [], FetchMode.Http, 0.5m, maxDepth: 3, maxPages: 1000),
        Site("newswire", "Rockstar Newswire", "https://www.rockstargames.com/newswire", ["/newswire"], FetchMode.Browser, 0.2m, maxDepth: 2, maxPages: 200),
        Site("gta5mods", "GTA5-Mods.com", "https://www.gta5-mods.com/vehicles", ["/vehicles"], FetchMode.Http, 0.5m, maxDepth: 2, maxPages: 1000),

        // Local test site (tools/Scraper.TestSite, the "testsite" compose service): rate-limit proof, failure demos
        // and the 585-page benchmark tree. Only reachable inside the compose network.
        Site("testsite", "Scraper Test Site", "http://testsite:8080/", [], FetchMode.Http, 5m, maxDepth: 3, maxPages: 1000),
    ];

    private static SiteConfig Site(
        string id, string name, string baseUrl, string[] allowedPathPrefixes, FetchMode fetchMode,
        decimal requestsPerSecond, int maxDepth, int maxPages)
    {
        var url = NormalizedUrl.Create(baseUrl);
        return new SiteConfig(id, name, url, [url], allowedPathPrefixes, fetchMode, new RateLimit(requestsPerSecond), maxDepth, maxPages);
    }
}
