using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

/// <summary>A URL to fetch, with the validators from the last fetch for a conditional request.</summary>
public sealed record FetchRequest(NormalizedUrl Url, string? ETag = null, DateTimeOffset? LastModified = null);
