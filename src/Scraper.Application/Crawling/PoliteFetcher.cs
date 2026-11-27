namespace Scraper.Application.Crawling;

/// <summary>
/// The polite single-URL pipeline: known site → in scope → allowed by robots.txt → fetch. The fetcher
/// itself waits for the shared rate limit on every attempt. Any check that fails returns
/// <see cref="PoliteFetchKind.Blocked"/> before a request is made.
/// </summary>
public sealed class PoliteFetcher(ISiteCatalog sites, IRobotsPolicy robots, IFetcher fetcher)
{
    public async Task<PoliteFetchOutcome> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var site = await sites.FindByAuthorityAsync(request.Url.Authority, cancellationToken);
        if (site is null)
        {
            return PoliteFetchOutcome.Blocked(BlockReason.UnknownSite);
        }
        if (!site.IsInScope(request.Url))
        {
            return PoliteFetchOutcome.Blocked(BlockReason.OutOfScope);
        }
        if (!await robots.IsAllowedAsync(request.Url, cancellationToken))
        {
            return PoliteFetchOutcome.Blocked(BlockReason.RobotsDisallowed);
        }

        var result = await fetcher.FetchAsync(request, cancellationToken);
        if (result.IsNotModified)
        {
            return new PoliteFetchOutcome(PoliteFetchKind.NotModified, result);
        }
        if (result.IsRedirect)
        {
            // The target is only reported, never followed here: it must pass these same checks itself.
            var baseUri = new Uri(request.Url.Value);
            return Domain.Urls.NormalizedUrl.TryCreate(result.RedirectLocation, baseUri, out var target)
                ? new PoliteFetchOutcome(PoliteFetchKind.Redirected, result, RedirectTarget: target)
                : new PoliteFetchOutcome(PoliteFetchKind.Failed, result);
        }
        return new PoliteFetchOutcome(result.IsSuccess ? PoliteFetchKind.Fetched : PoliteFetchKind.Failed, result);
    }
}
