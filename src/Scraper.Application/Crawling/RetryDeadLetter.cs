namespace Scraper.Application.Crawling;

/// <summary>An operator re-queues a dead-lettered task: it starts again from attempt 1 and its job reopens.</summary>
public sealed class RetryDeadLetter(ICrawlStore store, ITaskQueue queue, TimeProvider time)
{
    public async Task<RetryDeadLetterResult> ExecuteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var result = await store.ResetDeadLetterAsync(taskId, time.GetUtcNow(), cancellationToken);
        if (result is RetryDeadLetterResult.Requeued requeued)
        {
            await queue.PublishAsync([requeued.Message], cancellationToken);
        }
        return result;
    }
}
