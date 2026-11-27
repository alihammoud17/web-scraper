using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Scraper.Application.Crawling;
using Scraper.Domain.Crawling;
using Scraper.Domain.Pages;

namespace Scraper.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="ICrawlStore"/> on PostgreSQL. Every write is an <c>INSERT … ON CONFLICT</c> on a natural key
/// or a guarded <c>UPDATE … WHERE status …</c>, so running the same task twice leaves the same rows
/// (ADR 0010, ADR 0012). Statuses are stored as their enum names.
/// </summary>
public sealed class PostgresCrawlStore(ScraperDbContext db) : ICrawlStore
{
    private static readonly string[] TerminalStatuses = [.. CrawlTask.TerminalStatuses.Select(s => s.ToString())];

    private static readonly string[] PendingStatuses =
        [nameof(CrawlTaskStatus.Queued), nameof(CrawlTaskStatus.InProgress), nameof(CrawlTaskStatus.Failed)];

    public async Task CreateJobAsync(CrawlJob job, IReadOnlyList<CrawlTask> seeds, CancellationToken cancellationToken)
    {
        db.CrawlJobs.Add(job);
        db.CrawlTasks.AddRange(seeds);
        await db.SaveChangesAsync(cancellationToken); // one transaction
    }

    public Task MarkJobFailedAsync(Guid jobId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE crawl_jobs SET status = {nameof(CrawlJobStatus.Failed)}, finished_at = now()
            WHERE id = {jobId} AND status IN ({nameof(CrawlJobStatus.Pending)}, {nameof(CrawlJobStatus.Running)})
            """, cancellationToken);

    public async Task<bool> TryStartTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE crawl_tasks SET status = {nameof(CrawlTaskStatus.InProgress)}, attempt_count = attempt_count + 1
            WHERE id = {taskId} AND NOT (status = ANY ({TerminalStatuses}))
            """, cancellationToken) == 1;

    public async Task<TaskContext> GetTaskContextAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<ContextRow>($"""
            SELECT j.status AS job_status, j.mode AS mode, p.e_tag AS e_tag, p.last_modified AS last_modified
            FROM crawl_tasks t
            JOIN crawl_jobs j ON j.id = t.crawl_job_id
            LEFT JOIN pages p ON p.site_id = j.site_id AND p.url = t.url
            WHERE t.id = {taskId}
            """).ToListAsync(cancellationToken);
        var row = rows.Single();
        return new TaskContext(Enum.Parse<CrawlJobStatus>(row.JobStatus), Enum.Parse<CrawlMode>(row.Mode), row.ETag, row.LastModified);
    }

    public async Task<IReadOnlyList<CrawlTaskMessage>> CompleteTaskAsync(TaskCompletion completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var message = completion.Message;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            Guid? pageId = null;
            if (completion.Outcome != TaskOutcome.Skipped && completion.Result is { } result)
            {
                pageId = await UpsertPageAsync(message, result, completion.At, cancellationToken);
                await InsertFetchAsync(pageId.Value, completion, result, cancellationToken);
                await UpsertLinksAsync(pageId.Value, completion.Links, completion.At, cancellationToken);
            }
            await InsertChildrenAsync(message, completion.Children, completion.At, cancellationToken);

            var status = completion.Outcome == TaskOutcome.Skipped ? CrawlTaskStatus.Skipped : CrawlTaskStatus.Succeeded;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE crawl_tasks
                SET status = {status.ToString()}, page_id = COALESCE({pageId}, page_id), last_error = {completion.SkipReason}, completed_at = {completion.At}
                WHERE id = {message.TaskId}
                """, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        return await GetQueuedChildrenAsync(message.TaskId, cancellationToken);
    }

    public async Task<IReadOnlyList<CrawlTaskMessage>> GetQueuedChildrenAsync(Guid parentTaskId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<ChildRow>($"""
            SELECT t.id AS task_id, t.crawl_job_id AS job_id, j.site_id AS site_id, t.url AS url, t.depth AS depth
            FROM crawl_tasks t JOIN crawl_jobs j ON j.id = t.crawl_job_id
            WHERE t.discovered_by_task_id = {parentTaskId} AND t.status = {nameof(CrawlTaskStatus.Queued)}
            ORDER BY t.id
            """).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new CrawlTaskMessage(r.TaskId, r.JobId, r.SiteId, r.Url, r.Depth, Attempt: 1))];
    }

    public Task RecordRetryAsync(Guid taskId, string error, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE crawl_tasks SET status = {nameof(CrawlTaskStatus.Failed)}, last_error = {error}
            WHERE id = {taskId} AND NOT (status = ANY ({TerminalStatuses}))
            """, cancellationToken);

    public async Task RecordDeadLetterAsync(DeadLetterNotice notice, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var message = notice.Message;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // A stale duplicate of a task that already succeeded (or was skipped) is not a failure: record nothing.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dead_letter_entries (id, crawl_task_id, site_id, url, last_error, attempt_count, dead_lettered_at, retried_at)
            SELECT {Guid.CreateVersion7()}, t.id, {message.SiteId}, t.url, {notice.Reason}, {message.Attempt}, {at}, NULL
            FROM crawl_tasks t
            WHERE t.id = {message.TaskId} AND t.status NOT IN ({nameof(CrawlTaskStatus.Succeeded)}, {nameof(CrawlTaskStatus.Skipped)})
            ON CONFLICT (crawl_task_id) DO UPDATE
            SET last_error = excluded.last_error, attempt_count = excluded.attempt_count,
                dead_lettered_at = excluded.dead_lettered_at, retried_at = NULL
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE crawl_tasks SET status = {nameof(CrawlTaskStatus.DeadLettered)}, last_error = {notice.Reason}, completed_at = {at}
            WHERE id = {message.TaskId} AND status NOT IN ({nameof(CrawlTaskStatus.Succeeded)}, {nameof(CrawlTaskStatus.Skipped)})
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<RetryDeadLetterResult> ResetDeadLetterAsync(Guid taskId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.Database.SqlQuery<ResetRow>($"""
            SELECT t.status AS status, t.crawl_job_id AS job_id, j.site_id AS site_id, t.url AS url, t.depth AS depth
            FROM crawl_tasks t JOIN crawl_jobs j ON j.id = t.crawl_job_id
            WHERE t.id = {taskId}
            FOR UPDATE OF t
            """).ToListAsync(cancellationToken); // not composed: EF would wrap FOR UPDATE in a subquery
        var task = rows.SingleOrDefault();
        if (task is null)
        {
            return new RetryDeadLetterResult.NotFound();
        }
        var status = Enum.Parse<CrawlTaskStatus>(task.Status);
        if (status != CrawlTaskStatus.DeadLettered)
        {
            return new RetryDeadLetterResult.NotDeadLettered(status);
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE crawl_tasks SET status = {nameof(CrawlTaskStatus.Queued)}, attempt_count = 0, completed_at = NULL WHERE id = {taskId}
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dead_letter_entries SET retried_at = {at} WHERE crawl_task_id = {taskId}
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE crawl_jobs SET status = {nameof(CrawlJobStatus.Running)}, finished_at = NULL
            WHERE id = {task.JobId} AND status IN ({nameof(CrawlJobStatus.Completed)}, {nameof(CrawlJobStatus.Running)})
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RetryDeadLetterResult.Requeued(new CrawlTaskMessage(taskId, task.JobId, task.SiteId, task.Url, task.Depth, Attempt: 1));
    }

    public async Task<bool> TryCompleteJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE crawl_jobs SET status = {nameof(CrawlJobStatus.Completed)}, finished_at = now()
            WHERE id = {jobId} AND status = {nameof(CrawlJobStatus.Running)}
              AND NOT EXISTS (SELECT 1 FROM crawl_tasks WHERE crawl_job_id = {jobId} AND status = ANY ({PendingStatuses}))
            """, cancellationToken) == 1;

    /// <summary>
    /// One row per (site, URL). A newer fetch overwrites the crawl state; an older one (lost a race) changes
    /// nothing. 404/410 sets <c>removed_at</c> once; any other response clears it (the page came back).
    /// </summary>
    private async Task<Guid> UpsertPageAsync(CrawlTaskMessage message, FetchResult result, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var removedAt = result.StatusCode is 404 or 410 ? at : (DateTimeOffset?)null;
        var ids = await db.Database.SqlQuery<Guid>($"""
            INSERT INTO pages (id, site_id, url, first_seen_at, last_fetched_at, last_status_code, e_tag, last_modified, removed_at)
            VALUES ({Guid.CreateVersion7()}, {message.SiteId}, {message.Url}, {at}, {at}, {result.StatusCode}, {result.ETag}, {result.LastModified}, {removedAt})
            ON CONFLICT (site_id, url) DO UPDATE
            SET last_fetched_at = excluded.last_fetched_at,
                last_status_code = excluded.last_status_code,
                e_tag = COALESCE(excluded.e_tag, pages.e_tag),
                last_modified = COALESCE(excluded.last_modified, pages.last_modified),
                removed_at = CASE WHEN excluded.removed_at IS NULL THEN NULL ELSE COALESCE(pages.removed_at, excluded.removed_at) END
            WHERE pages.last_fetched_at IS NULL OR excluded.last_fetched_at >= pages.last_fetched_at
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);
        if (ids.Count == 1)
        {
            return ids[0];
        }
        return await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM pages WHERE site_id = {message.SiteId} AND url = {message.Url}
            """).ToListAsync(cancellationToken) is [var id] ? id : throw new InvalidOperationException("The page row disappeared.");
    }

    /// <summary>One observation per task: a redelivered message doesn't record the same fetch twice.</summary>
    private Task InsertFetchAsync(Guid pageId, TaskCompletion completion, FetchResult result, CancellationToken cancellationToken)
    {
        var headers = new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(result.Headers) };
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO page_fetches (id, page_id, crawl_task_id, fetched_at, fetch_mode, status_code, headers, e_tag, last_modified, raw_html)
            VALUES ({Guid.CreateVersion7()}, {pageId}, {completion.Message.TaskId}, {completion.At}, {completion.FetchMode.ToString()},
                    {result.StatusCode}, {headers}, {result.ETag}, {result.LastModified}, {result.Body})
            ON CONFLICT (crawl_task_id) DO NOTHING
            """, cancellationToken);
    }

    /// <summary>One edge per (page, target) with the latest anchor text; links no longer present keep their row.</summary>
    private Task UpsertLinksAsync(Guid pageId, IReadOnlyList<DiscoveredLink> links, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var edges = links.GroupBy(l => l.Url.Value).Select(g => Link.Discover(pageId, g.First().Url, g.First().Text, at)).ToList();
        if (edges.Count == 0)
        {
            return Task.CompletedTask;
        }
        Guid[] ids = [.. edges.Select(e => e.Id)];
        string[] urls = [.. edges.Select(e => e.TargetUrl.Value)];
        string[] texts = [.. edges.Select(e => e.Text)];
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO links (id, source_page_id, target_url, text, first_seen_at, last_seen_at)
            SELECT id, {pageId}, url, text, {at}, {at} FROM unnest({ids}, {urls}, {texts}) AS l(id, url, text)
            ON CONFLICT (source_page_id, target_url) DO UPDATE
            SET text = excluded.text, last_seen_at = GREATEST(links.last_seen_at, excluded.last_seen_at)
            """, cancellationToken);
    }

    /// <summary>Child tasks are part of the parent's transaction: they act as its outbox (ADR 0012).</summary>
    private Task InsertChildrenAsync(CrawlTaskMessage parent, IReadOnlyList<ChildTask> children, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (children.Count == 0)
        {
            return Task.CompletedTask;
        }
        Guid[] ids = [.. children.Select(_ => Guid.CreateVersion7())];
        string[] urls = [.. children.Select(c => c.Url.Value)];
        int[] depths = [.. children.Select(c => c.Depth)];
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO crawl_tasks (id, crawl_job_id, url, depth, status, attempt_count, enqueued_at, discovered_by_task_id)
            SELECT id, {parent.JobId}, url, depth, {nameof(CrawlTaskStatus.Queued)}, 0, {at}, {parent.TaskId}
            FROM unnest({ids}, {urls}, {depths}) AS c(id, url, depth)
            ON CONFLICT (crawl_job_id, url) DO NOTHING
            """, cancellationToken);
    }

    private sealed record ContextRow(string JobStatus, string Mode, string? ETag, DateTimeOffset? LastModified);

    private sealed record ChildRow(Guid TaskId, Guid JobId, string SiteId, string Url, int Depth);

    private sealed record ResetRow(string Status, Guid JobId, string SiteId, string Url, int Depth);
}
