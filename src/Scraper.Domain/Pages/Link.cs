using Scraper.Domain.Urls;

namespace Scraper.Domain.Pages;

/// <summary>
/// A link from a page to a URL, unique per (source page, target URL), with the latest anchor text.
/// Links no longer on the page keep their row; their <see cref="LastSeenAt"/> just stops moving.
/// </summary>
public sealed class Link
{
    public const int MaxTextLength = 500;

    private Link()
    {
    }

    public Guid Id { get; private set; }

    public Guid SourcePageId { get; private set; }

    /// <summary>Plain normalized URL, not a foreign key: the target may be off-site or not crawled yet.</summary>
    public NormalizedUrl TargetUrl { get; private set; } = null!;

    public string Text { get; private set; } = string.Empty;

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public static Link Discover(Guid sourcePageId, NormalizedUrl targetUrl, string? text, DateTimeOffset seenAt)
    {
        ArgumentNullException.ThrowIfNull(targetUrl);
        return new Link
        {
            Id = Guid.CreateVersion7(),
            SourcePageId = sourcePageId,
            TargetUrl = targetUrl,
            Text = CleanText(text),
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
        };
    }

    public void SeenAgain(string? text, DateTimeOffset seenAt)
    {
        Text = CleanText(text);
        if (seenAt > LastSeenAt)
        {
            LastSeenAt = seenAt;
        }
    }

    private static string CleanText(string? text)
    {
        var clean = Content.CanonicalWriter.NormalizeText(text ?? string.Empty);
        return clean.Length <= MaxTextLength ? clean : clean[..MaxTextLength];
    }
}
