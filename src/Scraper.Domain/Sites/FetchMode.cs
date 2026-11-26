namespace Scraper.Domain.Sites;

/// <summary>How a site's pages are fetched: plain HTTP, or a headless browser for JS-rendered sites (ADR 0005).</summary>
public enum FetchMode
{
    Http,
    Browser,
}
