using Scraper.Application.Crawling;
using Scraper.Domain.Crawling;

namespace Scraper.Application.Tests;

public class ProcessCrawlTaskTests
{
    private readonly CrawlFakes _fakes = new();

    [Fact]
    public async Task Ack_comes_only_after_the_commit_and_the_published_children()
    {
        _fakes.Extractor.Links.AddRange(["http://testsite:8080/pages/2", "http://testsite:8080/pages/3"]);

        var disposition = await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None);

        Assert.Same(TaskDisposition.Ack, disposition);
        Assert.Equal(["start", "fetch", "commit", "mark-seen", "publish:2", "complete-job"], _fakes.Events);
        Assert.All(_fakes.Queue.Published, m => Assert.Equal(1, m.Depth));
    }

    [Fact]
    public async Task A_failing_commit_is_retried_and_nothing_is_published_or_marked_seen()
    {
        _fakes.Extractor.Links.Add("http://testsite:8080/pages/2");
        _fakes.Store.CompleteThrows = new TimeoutException("database timeout");

        var disposition = await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None);

        var retry = Assert.IsType<TaskDisposition.Retry>(disposition);
        Assert.Equal(TimeSpan.FromSeconds(5), retry.Delay);
        Assert.DoesNotContain("mark-seen", _fakes.Events);
        Assert.Empty(_fakes.Queue.Published);
    }

    [Fact]
    public async Task A_duplicate_delivery_republishes_queued_children_without_fetching()
    {
        _fakes.Store.StartSucceeds = false;
        _fakes.Store.AlreadyQueuedChildren.Add(CrawlFakes.Message("http://testsite:8080/pages/2", depth: 1));

        var disposition = await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None);

        Assert.Same(TaskDisposition.Ack, disposition);
        Assert.Empty(_fakes.Fetcher.Requests);
        Assert.Single(_fakes.Queue.Published);
    }

    [Fact]
    public async Task Children_are_filtered_by_scope_seen_set_and_budget()
    {
        _fakes.Extractor.Links.AddRange([
            "http://testsite:8080/new-1",
            "http://testsite:8080/new-2",
            "http://testsite:8080/already-seen",
            "http://other.example/off-site",
        ]);
        _fakes.Seen.Seen.Add("http://testsite:8080/already-seen");
        _fakes.Budget.Remaining = 1;

        await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None);

        var child = Assert.Single(_fakes.Store.Completed!.Children);
        Assert.Equal("http://testsite:8080/new-1", child.Url.Value);
        Assert.Equal(4, _fakes.Store.Completed.Links.Count); // every link is stored, only one is enqueued
    }

    [Fact]
    public async Task At_the_maximum_depth_links_are_stored_but_not_followed()
    {
        _fakes.Extractor.Links.Add("http://testsite:8080/deeper");

        await _fakes.Processor().HandleAsync(CrawlFakes.Message(depth: CrawlFakes.TestSite.MaxDepth), CancellationToken.None);

        Assert.Empty(_fakes.Store.Completed!.Children);
        Assert.Single(_fakes.Store.Completed.Links);
    }

    [Fact]
    public async Task A_redirect_target_is_enqueued_at_the_same_depth()
    {
        _fakes.Fetcher.Next = CrawlFakes.Html(status: 301, location: "/moved");

        await _fakes.Processor().HandleAsync(CrawlFakes.Message(depth: 1), CancellationToken.None);

        var child = Assert.Single(_fakes.Store.Completed!.Children);
        Assert.Equal("http://testsite:8080/moved", child.Url.Value);
        Assert.Equal(1, child.Depth);
    }

    [Fact]
    public async Task A_gone_page_is_completed_as_removed()
    {
        _fakes.Fetcher.Next = CrawlFakes.Html(status: 404);

        Assert.Same(TaskDisposition.Ack, await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None));
        Assert.Equal(TaskOutcome.PageRemoved, _fakes.Store.Completed!.Outcome);
    }

    [Fact]
    public async Task A_robots_disallowed_url_is_skipped_without_a_request()
    {
        _fakes.Robots.Allowed = false;

        Assert.Same(TaskDisposition.Ack, await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None));
        Assert.Equal(TaskOutcome.Skipped, _fakes.Store.Completed!.Outcome);
        Assert.Equal("robots.txt disallows this URL", _fakes.Store.Completed.SkipReason);
        Assert.Empty(_fakes.Fetcher.Requests);
    }

    [Fact]
    public async Task Incremental_crawls_send_the_stored_validators()
    {
        var lastModified = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        _fakes.Store.Context = new TaskContext(CrawlJobStatus.Running, CrawlMode.Incremental, "\"v1\"", lastModified);
        _fakes.Fetcher.Next = CrawlFakes.Html(status: 304);

        await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None);

        var request = Assert.Single(_fakes.Fetcher.Requests);
        Assert.Equal("\"v1\"", request.ETag);
        Assert.Equal(lastModified, request.LastModified);
    }

    [Fact]
    public async Task A_retryable_status_records_the_retry_and_asks_for_a_delayed_attempt()
    {
        _fakes.Fetcher.Next = CrawlFakes.Html(status: 429, retryAfter: TimeSpan.FromSeconds(90));

        var retry = Assert.IsType<TaskDisposition.Retry>(await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromMinutes(2), retry.Delay);
        Assert.Equal(["HTTP 429"], _fakes.Store.Retries);
        Assert.Null(_fakes.Store.Completed);
    }

    [Fact]
    public async Task The_last_attempt_is_dead_lettered()
    {
        _fakes.Fetcher.Next = CrawlFakes.Html(status: 500);

        var deadLetter = Assert.IsType<TaskDisposition.DeadLetter>(
            await _fakes.Processor(new RetryOptions(3, [TimeSpan.FromSeconds(1)])).HandleAsync(CrawlFakes.Message(attempt: 3), CancellationToken.None));

        Assert.Equal("gave up after 3 attempts: HTTP 500", deadLetter.Reason);
        Assert.Empty(_fakes.Store.Retries);
    }

    [Fact]
    public async Task Transient_and_permanent_exceptions_become_retry_and_dead_letter()
    {
        _fakes.Fetcher.Throws = new HttpRequestException("connection reset");
        Assert.IsType<TaskDisposition.Retry>(await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None));

        _fakes.Fetcher.Throws = new InvalidDataException("response too large");
        Assert.IsType<TaskDisposition.DeadLetter>(await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_exceptions_propagate_so_the_consumer_rejects_the_message()
    {
        _fakes.Fetcher.Throws = new NotSupportedException("bug");

        await Assert.ThrowsAsync<NotSupportedException>(() => _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None));
    }

    [Fact]
    public async Task Tasks_of_a_cancelled_job_are_skipped()
    {
        _fakes.Store.Context = new TaskContext(CrawlJobStatus.Cancelled, CrawlMode.Full, null, null);

        await _fakes.Processor().HandleAsync(CrawlFakes.Message(), CancellationToken.None);

        Assert.Equal(TaskOutcome.Skipped, _fakes.Store.Completed!.Outcome);
        Assert.Empty(_fakes.Fetcher.Requests);
    }
}
