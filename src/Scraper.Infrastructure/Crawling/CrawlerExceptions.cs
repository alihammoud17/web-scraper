namespace Scraper.Infrastructure.Crawling;

/// <summary>The crawler was asked to contact a host that isn't a configured site (no known rate limit).</summary>
public sealed class UnknownSiteException(string authority)
    : InvalidOperationException($"'{authority}' is not a configured site; the crawler only contacts sites in the sites table.")
{
    public string Authority { get; } = authority;
}

/// <summary>A response body was larger than <see cref="CrawlerHttpOptions.MaxResponseBytes"/>.</summary>
public sealed class ResponseTooLargeException(Uri url, long limit)
    : InvalidOperationException($"The response from {url} is larger than {limit} bytes.");
