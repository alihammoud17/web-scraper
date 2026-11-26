namespace Scraper.Domain.Content;

/// <summary>
/// Volatile counters (GTA5-Mods downloads, likes, views) captured at one point in time. Kept as a
/// separate time series and never part of the content hash, so they can't create fake versions (ADR 0009).
/// One snapshot per crawl task, so a redelivered message doesn't add a second one.
/// </summary>
public sealed class MetricsSnapshot
{
    private MetricsSnapshot()
    {
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid CrawlTaskId { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public long? Downloads { get; private set; }

    public long? Likes { get; private set; }

    public long? Views { get; private set; }

    public static MetricsSnapshot Capture(
        Guid documentId, Guid crawlTaskId, DateTimeOffset capturedAt, long? downloads, long? likes, long? views)
    {
        ThrowIfNegative(downloads, nameof(downloads));
        ThrowIfNegative(likes, nameof(likes));
        ThrowIfNegative(views, nameof(views));
        return new MetricsSnapshot
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            CrawlTaskId = crawlTaskId,
            CapturedAt = capturedAt,
            Downloads = downloads,
            Likes = likes,
            Views = views,
        };
    }

    private static void ThrowIfNegative(long? value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Counters can't be negative.");
        }
    }
}
