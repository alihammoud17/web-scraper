using Scraper.Domain.Urls;

namespace Scraper.Domain.Pages;

/// <summary>
/// One URL of one site, unique per (site, normalized URL). Holds crawl state only; extracted content
/// lives in <c>Document</c>. A page that disappears is marked removed, never deleted.
/// </summary>
public sealed class Page
{
    private Page()
    {
    }

    public Guid Id { get; private set; }

    public string SiteId { get; private set; } = null!;

    public NormalizedUrl Url { get; private set; } = null!;

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset? LastFetchedAt { get; private set; }

    public int? LastStatusCode { get; private set; }

    /// <summary>Latest validators, sent back as If-None-Match / If-Modified-Since by incremental crawls.</summary>
    public string? ETag { get; private set; }

    public DateTimeOffset? LastModified { get; private set; }

    /// <summary>When the page was found gone (e.g. 404 or 410); null while it exists.</summary>
    public DateTimeOffset? RemovedAt { get; private set; }

    public bool IsRemoved => RemovedAt is not null;

    public static Page Discover(string siteId, NormalizedUrl url, DateTimeOffset firstSeenAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentNullException.ThrowIfNull(url);
        return new Page { Id = Guid.CreateVersion7(), SiteId = siteId, Url = url, FirstSeenAt = firstSeenAt };
    }

    /// <summary>Records a successful (or not-modified) fetch. A page that comes back is no longer removed.</summary>
    public void MarkSeen(DateTimeOffset fetchedAt, int statusCode, string? eTag, DateTimeOffset? lastModified)
    {
        LastFetchedAt = fetchedAt;
        LastStatusCode = statusCode;
        ETag = eTag;
        LastModified = lastModified;
        RemovedAt = null;
    }

    /// <summary>Marks the page as gone. Keeps the first removal time if it was already removed.</summary>
    public void MarkRemoved(DateTimeOffset at, int statusCode)
    {
        LastFetchedAt = at;
        LastStatusCode = statusCode;
        RemovedAt ??= at;
    }
}
