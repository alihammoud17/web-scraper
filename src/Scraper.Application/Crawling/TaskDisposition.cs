namespace Scraper.Application.Crawling;

/// <summary>What the consumer does with the message once processing returns (ADR 0012).</summary>
public abstract record TaskDisposition
{
    private TaskDisposition()
    {
    }

    public static TaskDisposition Ack { get; } = new Acknowledge();

    /// <summary>Everything is committed: acknowledge the message.</summary>
    public sealed record Acknowledge : TaskDisposition;

    /// <summary>Publish the next attempt to the retry tier for <paramref name="Delay"/>, then acknowledge.</summary>
    public sealed record Retry(TimeSpan Delay, string Error) : TaskDisposition;

    /// <summary>Publish to the dead-letter exchange, then acknowledge.</summary>
    public sealed record DeadLetter(string Reason) : TaskDisposition;
}
