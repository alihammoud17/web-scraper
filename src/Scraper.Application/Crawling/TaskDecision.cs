namespace Scraper.Application.Crawling;

/// <summary>What to do with a task after one attempt, decided by <see cref="RetryPolicy"/>.</summary>
public abstract record TaskDecision
{
    private TaskDecision()
    {
    }

    /// <summary>Done. <paramref name="PageRemoved"/> is true for 404/410: the page is marked removed, never deleted.</summary>
    public sealed record Succeed(bool PageRemoved = false) : TaskDecision;

    /// <summary>Never fetched on purpose (robots.txt, scope, unknown site). Permanent, not a failure.</summary>
    public sealed record Skip(string Reason) : TaskDecision;

    /// <summary>Try again later through the retry tier for <paramref name="Delay"/>.</summary>
    public sealed record Retry(TimeSpan Delay, string Error) : TaskDecision;

    /// <summary>Give up: the task goes to the dead-letter queue.</summary>
    public sealed record DeadLetter(string Reason) : TaskDecision;
}
