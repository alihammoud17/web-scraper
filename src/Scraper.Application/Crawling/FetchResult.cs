namespace Scraper.Application.Crawling;

/// <summary>What one HTTP fetch returned. Header names are lowercase.</summary>
public sealed record FetchResult(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? ETag,
    DateTimeOffset? LastModified,
    string? RedirectLocation,
    TimeSpan? RetryAfter,
    TimeSpan Elapsed)
{
    public bool IsSuccess => StatusCode is >= 200 and <= 299;

    public bool IsNotModified => StatusCode == 304;

    public bool IsRedirect => StatusCode is >= 300 and <= 399 && !IsNotModified && RedirectLocation is not null;
}
