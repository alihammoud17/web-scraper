using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

/// <summary>A link found on a page: normalized target and its anchor text.</summary>
public sealed record DiscoveredLink(NormalizedUrl Url, string Text);

/// <summary>Finds the crawlable links in an HTML page (absolute, normalized, no mailto/javascript/nofollow).</summary>
public interface ILinkExtractor
{
    IReadOnlyList<DiscoveredLink> Extract(string html, NormalizedUrl pageUrl);
}
