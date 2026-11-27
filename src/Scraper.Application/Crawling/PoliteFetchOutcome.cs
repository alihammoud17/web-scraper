using Scraper.Domain.Urls;

namespace Scraper.Application.Crawling;

public enum PoliteFetchKind
{
    Fetched,
    NotModified,
    Redirected,
    Failed,
    Blocked,
}

public enum BlockReason
{
    UnknownSite,
    OutOfScope,
    RobotsDisallowed,
}

/// <summary>The result of <see cref="PoliteFetcher"/>. A blocked URL was never requested.</summary>
public sealed record PoliteFetchOutcome(
    PoliteFetchKind Kind,
    FetchResult? Result = null,
    BlockReason? BlockReason = null,
    NormalizedUrl? RedirectTarget = null)
{
    public static PoliteFetchOutcome Blocked(BlockReason reason) => new(PoliteFetchKind.Blocked, BlockReason: reason);
}
