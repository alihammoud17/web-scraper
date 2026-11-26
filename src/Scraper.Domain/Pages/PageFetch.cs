using Scraper.Domain.Sites;

namespace Scraper.Domain.Pages;

/// <summary>
/// One observed HTTP response for a page: raw HTML, status and headers. Append-only history, one
/// row per crawl task, so a redelivered message doesn't record the same fetch twice.
/// </summary>
public sealed class PageFetch
{
    private PageFetch()
    {
    }

    public Guid Id { get; private set; }

    public Guid PageId { get; private set; }

    public Guid CrawlTaskId { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    public FetchMode FetchMode { get; private set; }

    public int StatusCode { get; private set; }

    /// <summary>Response headers, names lowercased.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; private set; } = new Dictionary<string, string>();

    public string? ETag { get; private set; }

    public DateTimeOffset? LastModified { get; private set; }

    /// <summary>The body as received or rendered; null for a 304 Not Modified.</summary>
    public string? RawHtml { get; private set; }

    public static PageFetch Record(
        Guid pageId,
        Guid crawlTaskId,
        DateTimeOffset fetchedAt,
        FetchMode fetchMode,
        int statusCode,
        IReadOnlyDictionary<string, string> headers,
        string? eTag,
        DateTimeOffset? lastModified,
        string? rawHtml)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(statusCode, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(statusCode, 599);
        ArgumentNullException.ThrowIfNull(headers);
        return new PageFetch
        {
            Id = Guid.CreateVersion7(),
            PageId = pageId,
            CrawlTaskId = crawlTaskId,
            FetchedAt = fetchedAt,
            FetchMode = fetchMode,
            StatusCode = statusCode,
            Headers = headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value),
            ETag = eTag,
            LastModified = lastModified,
            RawHtml = rawHtml,
        };
    }
}
