using Scraper.Domain.Crawling;
using Scraper.Domain.Urls;

namespace Scraper.Domain.Tests;

public class CrawlTaskTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly NormalizedUrl Url = NormalizedUrl.Create("http://testsite:8080/pages/1");

    [Fact]
    public void A_child_remembers_the_task_that_discovered_it()
    {
        var parent = CrawlTask.Enqueue(Guid.CreateVersion7(), Url, 0, Now);
        var child = CrawlTask.Enqueue(parent.CrawlJobId, NormalizedUrl.Create("http://testsite:8080/pages/2"), 1, Now, parent.Id);

        Assert.Null(parent.DiscoveredByTaskId);
        Assert.Equal(parent.Id, child.DiscoveredByTaskId);
    }

    [Fact]
    public void A_stale_in_progress_task_can_be_taken_over_after_a_crash()
    {
        var task = CrawlTask.Enqueue(Guid.CreateVersion7(), Url, 0, Now);
        task.BeginAttempt(); // worker A, then killed before finishing

        task.BeginAttempt(); // worker B gets the redelivered message

        Assert.Equal(CrawlTaskStatus.InProgress, task.Status);
        Assert.Equal(2, task.AttemptCount);
    }

    [Fact]
    public void A_failed_task_can_be_retried()
    {
        var task = CrawlTask.Enqueue(Guid.CreateVersion7(), Url, 0, Now);
        task.BeginAttempt();
        task.Fail("HTTP 503");

        task.BeginAttempt();

        Assert.Equal(2, task.AttemptCount);
    }

    [Theory]
    [InlineData(CrawlTaskStatus.Succeeded)]
    [InlineData(CrawlTaskStatus.Skipped)]
    [InlineData(CrawlTaskStatus.DeadLettered)]
    public void Terminal_tasks_are_never_started_again(CrawlTaskStatus terminal)
    {
        var task = InStatus(terminal);

        Assert.True(task.IsTerminal);
        Assert.Throws<InvalidOperationException>(task.BeginAttempt);
    }

    [Fact]
    public void Skipping_records_the_reason_and_is_terminal()
    {
        var task = CrawlTask.Enqueue(Guid.CreateVersion7(), Url, 0, Now);
        task.BeginAttempt();

        task.Skip("robots.txt disallows this URL", Now);

        Assert.Equal(CrawlTaskStatus.Skipped, task.Status);
        Assert.Equal("robots.txt disallows this URL", task.LastError);
        Assert.True(task.IsTerminal);
    }

    [Fact]
    public void A_dead_lettered_task_restarts_from_attempt_one_when_retried()
    {
        var task = InStatus(CrawlTaskStatus.DeadLettered);

        task.ResetForRetry();

        Assert.Equal(CrawlTaskStatus.Queued, task.Status);
        Assert.Equal(0, task.AttemptCount);
        Assert.False(task.IsTerminal);
        task.BeginAttempt();
        Assert.Equal(1, task.AttemptCount);
    }

    [Fact]
    public void Only_dead_lettered_tasks_can_be_reset()
    {
        Assert.Throws<InvalidOperationException>(InStatus(CrawlTaskStatus.Succeeded).ResetForRetry);
    }

    [Fact]
    public void A_completed_job_can_be_reopened()
    {
        var job = CrawlJob.Request("testsite", CrawlMode.Full, Now);
        job.Start(Now);
        job.Complete(Now);

        job.Reopen();

        Assert.Equal(CrawlJobStatus.Running, job.Status);
        Assert.Null(job.FinishedAt);
    }

    private static CrawlTask InStatus(CrawlTaskStatus status)
    {
        var task = CrawlTask.Enqueue(Guid.CreateVersion7(), Url, 0, Now);
        task.BeginAttempt();
        switch (status)
        {
            case CrawlTaskStatus.Succeeded:
                task.Succeed(Guid.CreateVersion7(), Now);
                break;
            case CrawlTaskStatus.Skipped:
                task.Skip("robots", Now);
                break;
            case CrawlTaskStatus.DeadLettered:
                task.DeadLetter("HTTP 500", Now);
                break;
        }
        return task;
    }
}
