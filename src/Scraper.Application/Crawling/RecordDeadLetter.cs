namespace Scraper.Application.Crawling;

/// <summary>
/// Writes a dead-letter entry for a message that reached the dead-letter queue, whichever way it got there
/// (a worker gave up, or the broker's delivery limit / a rejection), then checks whether the job is done.
/// </summary>
public sealed class RecordDeadLetter(ICrawlStore store, TimeProvider time)
{
    public async Task ExecuteAsync(DeadLetterNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        await store.RecordDeadLetterAsync(notice, time.GetUtcNow(), cancellationToken);
        await store.TryCompleteJobAsync(notice.Message.JobId, cancellationToken);
    }
}
