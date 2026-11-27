using Scraper.Domain.Crawling;
using Scraper.Domain.Sites;
using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

public sealed record StartCrawlCommand(string SiteId, CrawlMode Mode = CrawlMode.Full, IReadOnlyList<string>? Seeds = null);

public abstract record StartCrawlResult
{
    private StartCrawlResult()
    {
    }

    public sealed record Started(Guid JobId, int SeedCount) : StartCrawlResult;

    public sealed record SiteNotFound : StartCrawlResult;

    public sealed record Unsupported(string Reason) : StartCrawlResult;

    public sealed record InvalidSeeds(IReadOnlyList<string> Seeds) : StartCrawlResult;

    public sealed record QueueUnavailable(Guid JobId) : StartCrawlResult;
}

/// <summary>
/// Starts a crawl: validates the seeds, stores the job and its seed tasks in one transaction, then publishes
/// the seeds (confirmed). If publishing fails the job is marked failed instead of silently never running.
/// </summary>
public sealed class StartCrawl(ICrawlStore store, ISiteCatalog sites, ITaskQueue queue, ISeenUrlStore seen, ICrawlBudget budget, TimeProvider time)
{
    public async Task<StartCrawlResult> ExecuteAsync(StartCrawlCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var site = await sites.FindByIdAsync(command.SiteId, cancellationToken);
        if (site is null)
        {
            return new StartCrawlResult.SiteNotFound();
        }
        if (site.FetchMode == FetchMode.Browser)
        {
            return new StartCrawlResult.Unsupported("Browser (JS-rendered) sites need the Playwright fetcher, which arrives in P6.");
        }

        var requested = command.Seeds is { Count: > 0 } given ? given : [.. site.SeedUrls.Select(u => u.Value)];
        var invalid = requested.Where(s => !NormalizedUrl.TryCreate(s, null, out var url) || !site.IsInScope(url)).ToList();
        if (invalid.Count > 0)
        {
            return new StartCrawlResult.InvalidSeeds(invalid);
        }

        var now = time.GetUtcNow();
        var job = CrawlJob.Request(site.Id, command.Mode, now);
        job.Start(now);
        var seeds = requested.Select(NormalizedUrl.Create).Distinct().Take(site.MaxPages).ToList();
        var tasks = seeds.Select(url => CrawlTask.Enqueue(job.Id, url, depth: 0, now)).ToList();

        await store.CreateJobAsync(job, tasks, cancellationToken);
        await budget.TryReserveAsync(job.Id, site.MaxPages, tasks.Count, cancellationToken);
        await seen.MarkSeenAsync(job.Id, seeds, cancellationToken);
        try
        {
            await queue.PublishAsync([.. tasks.Select(t => new CrawlTaskMessage(t.Id, job.Id, site.Id, t.Url.Value, 0, 1))], cancellationToken);
        }
#pragma warning disable CA1031 // Any publish failure means the job can't run; record that and report it.
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            await store.MarkJobFailedAsync(job.Id, cancellationToken);
            return new StartCrawlResult.QueueUnavailable(job.Id);
        }
        return new StartCrawlResult.Started(job.Id, tasks.Count);
    }
}
