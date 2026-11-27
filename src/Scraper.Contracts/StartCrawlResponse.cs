namespace Scraper.Contracts;

/// <summary>Body of a 202 from <c>POST /crawls</c>: the job id and how many seed tasks were enqueued.</summary>
public sealed record StartCrawlResponse(Guid JobId, int SeedCount);
