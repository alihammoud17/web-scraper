using Scraper.Domain.Crawling;
using Scraper.Domain.Sites;
using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

/// <summary>
/// Processes one task message and tells the consumer what to do with it. The rule that makes crashes safe:
/// <see cref="TaskDisposition.Ack"/> is returned only after the database transaction has committed and the
/// task's children have been published (confirmed). Anything earlier is lost with the worker and redone
/// on redelivery; the database writes are idempotent, so redoing them changes nothing.
/// </summary>
public sealed class ProcessCrawlTask(
    ICrawlStore store,
    ISiteCatalog sites,
    PoliteFetcher fetcher,
    ILinkExtractor links,
    ISeenUrlStore seen,
    ICrawlBudget budget,
    ITaskQueue queue,
    RetryPolicy policy,
    IFailureClassifier failures,
    TimeProvider time)
{
    public async Task<TaskDisposition> HandleAsync(CrawlTaskMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!await store.TryStartTaskAsync(message.TaskId, cancellationToken))
        {
            // Duplicate delivery of a finished task. If the previous holder crashed after its commit but
            // before publishing, its children are still queued: publish them again (the outbox rule).
            await queue.PublishAsync(await store.GetQueuedChildrenAsync(message.TaskId, cancellationToken), cancellationToken);
            return TaskDisposition.Ack;
        }

        try
        {
            return await ProcessAsync(message, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var kind = failures.Classify(exception);
            if (kind == FailureKind.Unknown)
            {
                throw; // a bug: the consumer rejects the message and the broker dead-letters it
            }
            return await ToDispositionAsync(message, policy.DecideFailure(Describe(exception), kind == FailureKind.Transient, message.Attempt), cancellationToken);
        }
    }

    private async Task<TaskDisposition> ProcessAsync(CrawlTaskMessage message, CancellationToken cancellationToken)
    {
        var context = await store.GetTaskContextAsync(message.TaskId, cancellationToken);
        var site = await sites.FindByIdAsync(message.SiteId, cancellationToken);
        if (context.JobStatus is CrawlJobStatus.Cancelled or CrawlJobStatus.Failed || site is null)
        {
            return await CompleteAsync(message, TaskOutcome.Skipped, null, site, [], [], site is null ? "site no longer configured" : "job stopped", cancellationToken);
        }

        var url = NormalizedUrl.Create(message.Url);
        var request = context.Mode == CrawlMode.Incremental
            ? new FetchRequest(url, context.ETag, context.LastModified)
            : new FetchRequest(url);
        var outcome = await fetcher.FetchAsync(request, cancellationToken);

        var decision = policy.Decide(outcome, message.Attempt);
        switch (decision)
        {
            case TaskDecision.Succeed succeed:
                var found = outcome.Kind == PoliteFetchKind.Fetched ? ExtractLinks(outcome.Result!, url) : [];
                var children = await ChooseChildrenAsync(message, site, outcome, found, cancellationToken);
                return await CompleteAsync(
                    message, succeed.PageRemoved ? TaskOutcome.PageRemoved : TaskOutcome.Succeeded, outcome.Result, site, found, children, null, cancellationToken);
            case TaskDecision.Skip skip:
                return await CompleteAsync(message, TaskOutcome.Skipped, null, site, [], [], skip.Reason, cancellationToken);
            default:
                return await ToDispositionAsync(message, decision, cancellationToken);
        }
    }

    private async Task<TaskDisposition> CompleteAsync(
        CrawlTaskMessage message, TaskOutcome outcome, FetchResult? result, SiteConfig? site,
        IReadOnlyList<DiscoveredLink> found, IReadOnlyList<ChildTask> children, string? skipReason, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletion(message, outcome, result, site?.FetchMode ?? FetchMode.Http, found, children, skipReason, time.GetUtcNow());

        // 1. Commit (one transaction). 2. Mark seen, publish children. 3. Only then ack.
        var queued = await store.CompleteTaskAsync(completion, cancellationToken);
        await seen.MarkSeenAsync(message.JobId, [.. children.Select(c => c.Url)], cancellationToken);
        await queue.PublishAsync(queued, cancellationToken);
        await store.TryCompleteJobAsync(message.JobId, cancellationToken);
        return TaskDisposition.Ack;
    }

    private IReadOnlyList<DiscoveredLink> ExtractLinks(FetchResult result, NormalizedUrl url)
    {
        var isHtml = !result.Headers.TryGetValue("content-type", out var type) || type.Contains("html", StringComparison.OrdinalIgnoreCase);
        return isHtml && result.Body is { Length: > 0 } body ? links.Extract(body, url) : [];
    }

    /// <summary>In scope, within the depth limit, not seen in this job, and within the page budget.</summary>
    private async Task<IReadOnlyList<ChildTask>> ChooseChildrenAsync(
        CrawlTaskMessage message, SiteConfig site, PoliteFetchOutcome outcome, IReadOnlyList<DiscoveredLink> found, CancellationToken cancellationToken)
    {
        // A redirect target replaces the page at the same depth; links go one level deeper.
        var (candidates, depth) = outcome.Kind == PoliteFetchKind.Redirected && outcome.RedirectTarget is { } target
            ? ((IReadOnlyList<NormalizedUrl>)[target], message.Depth)
            : (message.Depth + 1 <= site.MaxDepth ? [.. found.Select(l => l.Url)] : [], message.Depth + 1);

        var inScope = candidates.Where(site.IsInScope).Distinct().ToList();
        if (inScope.Count == 0)
        {
            return [];
        }
        var unseen = await seen.FilterUnseenAsync(message.JobId, inScope, cancellationToken);
        var allowed = unseen.Count == 0 ? 0 : await budget.TryReserveAsync(message.JobId, site.MaxPages, unseen.Count, cancellationToken);
        return [.. unseen.Take(allowed).Select(u => new ChildTask(u, depth))];
    }

    private async Task<TaskDisposition> ToDispositionAsync(CrawlTaskMessage message, TaskDecision decision, CancellationToken cancellationToken)
    {
        switch (decision)
        {
            case TaskDecision.Retry retry:
                await TryRecordRetryAsync(message, retry.Error, cancellationToken);
                return new TaskDisposition.Retry(retry.Delay, retry.Error);
            case TaskDecision.DeadLetter deadLetter:
                return new TaskDisposition.DeadLetter(deadLetter.Reason); // the dead-letter recorder writes the entry
            default:
                throw new InvalidOperationException($"Unexpected decision {decision}.");
        }
    }

    // Best effort: when the database itself is down, the retry still goes through the queue.
    private async Task TryRecordRetryAsync(CrawlTaskMessage message, string error, CancellationToken cancellationToken)
    {
        try
        {
            await store.RecordRetryAsync(message.TaskId, error, cancellationToken);
        }
        catch (Exception exception) when (failures.Classify(exception) == FailureKind.Transient)
        {
        }
    }

    private static string Describe(Exception exception) => exception switch
    {
        TimeoutException or OperationCanceledException => "timeout",
        _ => $"{exception.GetType().Name}: {exception.Message}",
    };
}
