using Scraper.Application.Crawling;

namespace Scraper.Application.Tests;

public class RetryDeadLetterTests
{
    private readonly CrawlFakes _fakes = new();

    [Fact]
    public async Task A_dead_lettered_task_is_published_again_from_attempt_one()
    {
        var message = CrawlFakes.Message(attempt: 1);
        _fakes.Store.ResetResult = new RetryDeadLetterResult.Requeued(message);

        var result = await new RetryDeadLetter(_fakes.Store, _fakes.Queue, TimeProvider.System).ExecuteAsync(message.TaskId, CancellationToken.None);

        Assert.IsType<RetryDeadLetterResult.Requeued>(result);
        Assert.Equal([message], _fakes.Queue.Published);
    }

    [Fact]
    public async Task Nothing_is_published_for_an_unknown_task()
    {
        var result = await new RetryDeadLetter(_fakes.Store, _fakes.Queue, TimeProvider.System).ExecuteAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.IsType<RetryDeadLetterResult.NotFound>(result);
        Assert.Empty(_fakes.Queue.Published);
    }

    [Fact]
    public async Task Recording_a_dead_letter_also_checks_job_completion()
    {
        var notice = new DeadLetterNotice(CrawlFakes.Message(attempt: 4), "gave up after 4 attempts: HTTP 500");

        await new RecordDeadLetter(_fakes.Store, TimeProvider.System).ExecuteAsync(notice, CancellationToken.None);

        Assert.Equal([notice], _fakes.Store.DeadLetters);
        Assert.Equal(["record-dead-letter", "complete-job"], _fakes.Events);
    }
}
