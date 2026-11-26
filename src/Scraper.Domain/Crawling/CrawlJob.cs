namespace Scraper.Domain.Crawling;

/// <summary>One crawl of one site, started by an admin. Status changes only along the allowed transitions.</summary>
public sealed class CrawlJob
{
    private CrawlJob()
    {
    }

    public Guid Id { get; private set; }

    public string SiteId { get; private set; } = null!;

    public CrawlMode Mode { get; private set; }

    public CrawlJobStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public static CrawlJob Request(string siteId, CrawlMode mode, DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        return new CrawlJob
        {
            Id = Guid.CreateVersion7(),
            SiteId = siteId,
            Mode = mode,
            Status = CrawlJobStatus.Pending,
            RequestedAt = requestedAt,
        };
    }

    public void Start(DateTimeOffset at)
    {
        Require(CrawlJobStatus.Pending);
        Status = CrawlJobStatus.Running;
        StartedAt = at;
    }

    public void Complete(DateTimeOffset at) => Finish(CrawlJobStatus.Completed, at, CrawlJobStatus.Running);

    public void Fail(DateTimeOffset at) => Finish(CrawlJobStatus.Failed, at, CrawlJobStatus.Pending, CrawlJobStatus.Running);

    public void Cancel(DateTimeOffset at) => Finish(CrawlJobStatus.Cancelled, at, CrawlJobStatus.Pending, CrawlJobStatus.Running);

    private void Finish(CrawlJobStatus target, DateTimeOffset at, params CrawlJobStatus[] allowedFrom)
    {
        Require(allowedFrom);
        Status = target;
        FinishedAt = at;
    }

    private void Require(params CrawlJobStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"Crawl job {Id} is {Status}; expected {string.Join(" or ", allowed)}.");
        }
    }
}
