namespace Scraper.Contracts;

/// <summary>Body of <c>POST /crawls</c>. Mode is "Full" (default) or "Incremental"; seeds default to the site's own.</summary>
public sealed record StartCrawlRequest(string SiteId, string? Mode = null, IReadOnlyList<string>? Seeds = null);
