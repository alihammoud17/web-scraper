using AngleSharp.Html.Parser;
using Scraper.Application.Crawling;
using Scraper.Domain.Urls;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// Finds <c>&lt;a href&gt;</c> links with AngleSharp: resolved against <c>&lt;base href&gt;</c> or the page
/// URL, normalized, without <c>mailto:</c>/<c>javascript:</c>/<c>tel:</c> links, links back to the same page,
/// or <c>rel="nofollow"</c> links (a site's hint not to follow them, which a polite crawler respects).
/// </summary>
public sealed class AngleSharpLinkExtractor : ILinkExtractor
{
    private static readonly HtmlParser Parser = new();

    public IReadOnlyList<DiscoveredLink> Extract(string html, NormalizedUrl pageUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pageUrl);
        using var document = Parser.ParseDocument(html);

        var baseUri = new Uri(pageUrl.Value);
        if (document.QuerySelector("base[href]")?.GetAttribute("href") is { } baseHref
            && Uri.TryCreate(baseUri, baseHref.Trim(), out var declared)
            && declared.Scheme is "http" or "https")
        {
            baseUri = declared;
        }

        var links = new List<DiscoveredLink>();
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var rel = anchor.GetAttribute("rel") ?? string.Empty;
            if (rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("nofollow", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            // NormalizedUrl rejects mailto:, javascript:, tel: and anything else that isn't http(s).
            if (NormalizedUrl.TryCreate(anchor.GetAttribute("href"), baseUri, out var url) && url != pageUrl)
            {
                links.Add(new DiscoveredLink(url, anchor.TextContent));
            }
        }
        return links;
    }
}
