using Microsoft.EntityFrameworkCore;
using Scraper.Application.Crawling;
using Scraper.Domain.Crawling;
using Scraper.Domain.Sites;
using Scraper.Domain.Urls;
using Scraper.Infrastructure.Persistence.Stores;

namespace Scraper.Infrastructure.Tests;

public sealed class PostgresCrawlStoreTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task Completing_the_same_task_twice_leaves_the_same_rows()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/idem");
        var completion = Completion(job, task, links: ["http://testsite:8080/a", "http://testsite:8080/b", "http://testsite:8080/a"],
            children: ["http://testsite:8080/a", "http://testsite:8080/b"]);

        var first = await Store().CompleteTaskAsync(completion, CancellationToken.None);
        var second = await Store().CompleteTaskAsync(completion, CancellationToken.None); // redelivered message

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Pages.CountAsync(p => p.Url == task.Url));
        var pageId = await db.Pages.Where(p => p.Url == task.Url).Select(p => p.Id).SingleAsync();
        Assert.Equal(1, await db.PageFetches.CountAsync(f => f.CrawlTaskId == task.Id));
        Assert.Equal(2, await db.Links.CountAsync(l => l.SourcePageId == pageId));
        Assert.Equal(2, await db.CrawlTasks.CountAsync(t => t.DiscoveredByTaskId == task.Id));
        Assert.Equal(2, first.Count);
        Assert.Equal(first.Select(m => m.TaskId), second.Select(m => m.TaskId)); // the same queued children, re-publishable
        Assert.All(first, m => Assert.Equal((1, 1), (m.Depth, m.Attempt)));
        Assert.Equal(CrawlTaskStatus.Succeeded, await StatusAsync(task.Id));
    }

    [PostgresFact]
    public async Task A_child_url_already_in_the_job_is_not_enqueued_twice()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/parent-1");
        var other = CrawlTask.Enqueue(job.Id, NormalizedUrl.Create("http://testsite:8080/shared"), 1, Now);
        await using (var db = database.CreateContext())
        {
            db.CrawlTasks.Add(other);
            await db.SaveChangesAsync();
        }

        var queued = await Store().CompleteTaskAsync(Completion(job, task, children: ["http://testsite:8080/shared"]), CancellationToken.None);

        Assert.Empty(queued); // the URL belongs to the task that discovered it first
        await using var check = database.CreateContext();
        Assert.Equal(1, await check.CrawlTasks.CountAsync(t => t.CrawlJobId == job.Id && t.Url == other.Url));
    }

    [PostgresFact]
    public async Task Terminal_tasks_cannot_be_started_but_a_stale_one_can()
    {
        var (_, task) = await NewJobAsync("http://testsite:8080/start");

        Assert.True(await Store().TryStartTaskAsync(task.Id, CancellationToken.None));
        Assert.True(await Store().TryStartTaskAsync(task.Id, CancellationToken.None)); // worker killed; redelivered
        await using (var db = database.CreateContext())
        {
            Assert.Equal(2, await db.CrawlTasks.Where(t => t.Id == task.Id).Select(t => t.AttemptCount).SingleAsync());
        }

        await Store().CompleteTaskAsync(Completion(await JobAsync(task), task), CancellationToken.None);

        Assert.False(await Store().TryStartTaskAsync(task.Id, CancellationToken.None));
        Assert.False(await Store().TryStartTaskAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [PostgresFact]
    public async Task A_job_completes_only_when_nothing_is_pending()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/complete");
        await Store().TryStartTaskAsync(task.Id, CancellationToken.None);

        Assert.False(await Store().TryCompleteJobAsync(job.Id, CancellationToken.None));

        await Store().CompleteTaskAsync(Completion(job, task), CancellationToken.None);

        Assert.True(await Store().TryCompleteJobAsync(job.Id, CancellationToken.None));
        Assert.False(await Store().TryCompleteJobAsync(job.Id, CancellationToken.None)); // already completed
    }

    [PostgresFact]
    public async Task A_retrying_task_keeps_the_job_open()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/retrying");
        await Store().TryStartTaskAsync(task.Id, CancellationToken.None);

        await Store().RecordRetryAsync(task.Id, "HTTP 503", CancellationToken.None);

        Assert.Equal(CrawlTaskStatus.Failed, await StatusAsync(task.Id));
        Assert.False(await Store().TryCompleteJobAsync(job.Id, CancellationToken.None));
    }

    [PostgresFact]
    public async Task Recording_a_dead_letter_twice_keeps_one_updated_row()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/dead");
        var message = Message(job, task) with { Attempt = 3 };

        await Store().RecordDeadLetterAsync(new DeadLetterNotice(message, "first"), Now, CancellationToken.None);
        await Store().RecordDeadLetterAsync(new DeadLetterNotice(message with { Attempt = 4 }, "gave up after 4 attempts: HTTP 500"), Now.AddMinutes(1), CancellationToken.None);

        await using var db = database.CreateContext();
        var entry = await db.DeadLetterEntries.SingleAsync(d => d.CrawlTaskId == task.Id);
        Assert.Equal((4, "gave up after 4 attempts: HTTP 500"), (entry.AttemptCount, entry.LastError));
        Assert.Equal(CrawlTaskStatus.DeadLettered, await StatusAsync(task.Id));
        Assert.True(await Store().TryCompleteJobAsync(job.Id, CancellationToken.None));
    }

    [PostgresFact]
    public async Task A_dead_letter_for_a_task_that_already_succeeded_is_ignored()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/late-duplicate");
        await Store().CompleteTaskAsync(Completion(job, task), CancellationToken.None);

        await Store().RecordDeadLetterAsync(new DeadLetterNotice(Message(job, task), "stale copy"), Now, CancellationToken.None);

        await using var db = database.CreateContext();
        Assert.False(await db.DeadLetterEntries.AnyAsync(d => d.CrawlTaskId == task.Id));
        Assert.Equal(CrawlTaskStatus.Succeeded, await StatusAsync(task.Id));
    }

    [PostgresFact]
    public async Task Retrying_a_dead_letter_requeues_the_task_and_reopens_the_job()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/retry-me");
        await Store().RecordDeadLetterAsync(new DeadLetterNotice(Message(job, task) with { Attempt = 4 }, "HTTP 500"), Now, CancellationToken.None);
        await Store().TryCompleteJobAsync(job.Id, CancellationToken.None);

        var result = await Store().ResetDeadLetterAsync(task.Id, Now.AddMinutes(5), CancellationToken.None);

        var requeued = Assert.IsType<RetryDeadLetterResult.Requeued>(result);
        Assert.Equal((task.Id, 1), (requeued.Message.TaskId, requeued.Message.Attempt));
        await using (var db = database.CreateContext())
        {
            var reset = await db.CrawlTasks.SingleAsync(t => t.Id == task.Id);
            Assert.Equal((CrawlTaskStatus.Queued, 0), (reset.Status, reset.AttemptCount));
            Assert.Equal(Now.AddMinutes(5), (await db.DeadLetterEntries.SingleAsync(d => d.CrawlTaskId == task.Id)).RetriedAt);
            Assert.Equal(CrawlJobStatus.Running, (await db.CrawlJobs.SingleAsync(j => j.Id == job.Id)).Status);
        }
        Assert.IsType<RetryDeadLetterResult.NotDeadLettered>(await Store().ResetDeadLetterAsync(task.Id, Now, CancellationToken.None));
        Assert.IsType<RetryDeadLetterResult.NotFound>(await Store().ResetDeadLetterAsync(Guid.CreateVersion7(), Now, CancellationToken.None));
    }

    [PostgresFact]
    public async Task A_gone_page_is_marked_removed_and_cleared_when_it_returns()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/comes-and-goes");
        await Store().CompleteTaskAsync(Completion(job, task, status: 404, outcome: TaskOutcome.PageRemoved), CancellationToken.None);

        await using (var db = database.CreateContext())
        {
            Assert.Equal(Now, (await db.Pages.SingleAsync(p => p.Url == task.Url)).RemovedAt);
        }

        var later = CrawlTask.Enqueue(job.Id, NormalizedUrl.Create("http://testsite:8080/comes-and-goes?again=1"), 0, Now);
        var back = Completion(job, task, at: Now.AddHours(1)) with { Message = Message(job, task) with { TaskId = later.Id } };
        await using (var db = database.CreateContext())
        {
            db.CrawlTasks.Add(later);
            await db.SaveChangesAsync();
        }
        await Store().CompleteTaskAsync(back, CancellationToken.None);

        await using var check = database.CreateContext();
        var page = await check.Pages.SingleAsync(p => p.Url == task.Url);
        Assert.Null(page.RemovedAt);
        Assert.Equal(200, page.LastStatusCode);
    }

    [PostgresFact]
    public async Task The_task_context_carries_the_page_validators()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/validators", CrawlMode.Incremental);
        await Store().CompleteTaskAsync(Completion(job, task), CancellationToken.None);

        var context = await Store().GetTaskContextAsync(task.Id, CancellationToken.None);

        Assert.Equal((CrawlJobStatus.Running, CrawlMode.Incremental, "\"v1\""), (context.JobStatus, context.Mode, context.ETag));
    }

    [PostgresFact]
    public async Task A_skipped_task_writes_no_page()
    {
        var (job, task) = await NewJobAsync("http://testsite:8080/private/x");

        await Store().CompleteTaskAsync(Completion(job, task, outcome: TaskOutcome.Skipped) with { Result = null, SkipReason = "robots.txt disallows this URL" }, CancellationToken.None);

        await using var db = database.CreateContext();
        Assert.False(await db.Pages.AnyAsync(p => p.Url == task.Url));
        var skipped = await db.CrawlTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal((CrawlTaskStatus.Skipped, "robots.txt disallows this URL"), (skipped.Status, skipped.LastError));
    }

    private PostgresCrawlStore Store() => new(database.CreateContext());

    private async Task<(CrawlJob Job, CrawlTask Task)> NewJobAsync(string url, CrawlMode mode = CrawlMode.Full)
    {
        var job = CrawlJob.Request("testsite", mode, Now);
        job.Start(Now);
        var task = CrawlTask.Enqueue(job.Id, NormalizedUrl.Create(url + "-" + Guid.NewGuid().ToString("N")[..8]), 0, Now);
        await Store().CreateJobAsync(job, [task], CancellationToken.None);
        return (job, task);
    }

    private async Task<CrawlJob> JobAsync(CrawlTask task)
    {
        await using var db = database.CreateContext();
        return await db.CrawlJobs.SingleAsync(j => j.Id == task.CrawlJobId);
    }

    private async Task<CrawlTaskStatus> StatusAsync(Guid taskId)
    {
        await using var db = database.CreateContext();
        return await db.CrawlTasks.Where(t => t.Id == taskId).Select(t => t.Status).SingleAsync();
    }

    private static CrawlTaskMessage Message(CrawlJob job, CrawlTask task) => new(task.Id, job.Id, "testsite", task.Url.Value, task.Depth, 1);

    private static TaskCompletion Completion(
        CrawlJob job, CrawlTask task, string[]? links = null, string[]? children = null, int status = 200,
        TaskOutcome outcome = TaskOutcome.Succeeded, DateTimeOffset? at = null) =>
        new(
            Message(job, task),
            outcome,
            new FetchResult(status, new Dictionary<string, string> { ["content-type"] = "text/html" }, "<html></html>", "\"v1\"", null, null, null, TimeSpan.FromMilliseconds(5)),
            FetchMode.Http,
            [.. (links ?? []).Select(l => new DiscoveredLink(NormalizedUrl.Create(l), "link text"))],
            [.. (children ?? []).Select(c => new ChildTask(NormalizedUrl.Create(c), task.Depth + 1))],
            null,
            at ?? Now);
}
