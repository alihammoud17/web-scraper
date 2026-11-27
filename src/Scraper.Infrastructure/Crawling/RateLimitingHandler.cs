using Scraper.Application.Crawling;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// Sits inside the retry pipeline, so <b>every attempt</b> (first try, each retry, robots.txt fetches)
/// first takes a token from the site's shared budget. Hosts that aren't configured sites are refused.
/// </summary>
public sealed class RateLimitingHandler(ISiteCatalog sites, IRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authority = request.RequestUri?.Authority ?? throw new InvalidOperationException("The request has no URL.");
        var site = await sites.FindByAuthorityAsync(authority, cancellationToken) ?? throw new UnknownSiteException(authority);
        await limiter.AcquireAsync(authority, site.RateLimit, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
