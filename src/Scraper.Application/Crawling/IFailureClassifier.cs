namespace Scraper.Application.Crawling;

public enum FailureKind
{
    /// <summary>Worth retrying later: network errors, timeouts, database or cache briefly unavailable.</summary>
    Transient,

    /// <summary>Will fail again: e.g. a response larger than the limit.</summary>
    Permanent,

    /// <summary>Not recognised (likely a bug): the message is rejected and dead-lettered by the broker.</summary>
    Unknown,
}

/// <summary>Tells transient from permanent failures for exceptions thrown by infrastructure.</summary>
public interface IFailureClassifier
{
    FailureKind Classify(Exception exception);
}
