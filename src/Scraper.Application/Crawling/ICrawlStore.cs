using Scraper.Domain.Crawling;

namespace Scraper.Application.Crawling;

/// <summary>
/// Crawl state in the database. Every write is idempotent (natural-key unique constraints plus guarded
/// updates), so processing the same message twice leaves the same rows (ADR 0010, ADR 0012).
/// </summary>
public interface ICrawlStore
{
    /// <summary>Creates the job and its seed tasks in one transaction.</summary>
    Task CreateJobAsync(CrawlJob job, IReadOnlyList<CrawlTask> seeds, CancellationToken cancellationToken);

    Task MarkJobFailedAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Claims the task for an attempt (status InProgress, attempt count + 1). False when the task is
    /// already terminal (a duplicate delivery) or doesn't exist. A stale InProgress can be claimed again.
    /// </summary>
    Task<bool> TryStartTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<TaskContext> GetTaskContextAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>
    /// One transaction: page, fetch, links, child tasks and the task's final status. Returns this task's
    /// children that are still queued, so the caller can publish them after the commit.
    /// </summary>
    Task<IReadOnlyList<CrawlTaskMessage>> CompleteTaskAsync(TaskCompletion completion, CancellationToken cancellationToken);

    /// <summary>Children of a task that are still queued (re-published when the parent is redelivered).</summary>
    Task<IReadOnlyList<CrawlTaskMessage>> GetQueuedChildrenAsync(Guid parentTaskId, CancellationToken cancellationToken);

    /// <summary>The attempt failed and will be retried through the queue.</summary>
    Task RecordRetryAsync(Guid taskId, string error, CancellationToken cancellationToken);

    /// <summary>Upserts the dead-letter entry (one per task) and marks the task dead-lettered.</summary>
    Task RecordDeadLetterAsync(DeadLetterNotice notice, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Re-queues a dead-lettered task from attempt 1 and reopens its job.</summary>
    Task<RetryDeadLetterResult> ResetDeadLetterAsync(Guid taskId, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Marks a running job completed when none of its tasks is queued, in progress or awaiting a retry.</summary>
    Task<bool> TryCompleteJobAsync(Guid jobId, CancellationToken cancellationToken);
}

/// <summary>What processing needs to know about a task's job and its page's last validators.</summary>
public sealed record TaskContext(CrawlJobStatus JobStatus, CrawlMode Mode, string? ETag, DateTimeOffset? LastModified);

public enum TaskOutcome
{
    Succeeded,
    PageRemoved,
    Skipped,
}

/// <summary>A child URL to enqueue: a link one level deeper, or a redirect target at the same depth.</summary>
public sealed record ChildTask(Domain.Urls.NormalizedUrl Url, int Depth);

/// <summary>Everything one successful (or skipped) attempt writes, in a single transaction.</summary>
public sealed record TaskCompletion(
    CrawlTaskMessage Message,
    TaskOutcome Outcome,
    FetchResult? Result,
    Domain.Sites.FetchMode FetchMode,
    IReadOnlyList<DiscoveredLink> Links,
    IReadOnlyList<ChildTask> Children,
    string? SkipReason,
    DateTimeOffset At);

/// <summary>Why and after how many attempts a task was given up.</summary>
public sealed record DeadLetterNotice(CrawlTaskMessage Message, string Reason);

public abstract record RetryDeadLetterResult
{
    private RetryDeadLetterResult()
    {
    }

    public sealed record Requeued(CrawlTaskMessage Message) : RetryDeadLetterResult;

    public sealed record NotFound : RetryDeadLetterResult;

    public sealed record NotDeadLettered(CrawlTaskStatus Status) : RetryDeadLetterResult;
}
