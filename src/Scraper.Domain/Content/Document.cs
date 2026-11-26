namespace Scraper.Domain.Content;

/// <summary>
/// The extracted content of one page (1:1 with a content page). Listing pages, such as GTA5-Mods
/// <c>/vehicles/N</c>, have no document. The content itself lives in immutable versions.
/// </summary>
public sealed class Document
{
    private Document()
    {
    }

    public Guid Id { get; private set; }

    public Guid PageId { get; private set; }

    public DocumentKind Kind { get; private set; }

    /// <summary>The latest version seen; null until the first version is stored.</summary>
    public Guid? CurrentVersionId { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    public static Document Create(Guid pageId, DocumentKind kind, DateTimeOffset firstSeenAt) =>
        new() { Id = Guid.CreateVersion7(), PageId = pageId, Kind = kind, FirstSeenAt = firstSeenAt };

    /// <summary>Points the document at a version (a new one, or an earlier one after a content revert).</summary>
    public void SetCurrentVersion(DocumentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.DocumentId != Id)
        {
            throw new ArgumentException("The version belongs to another document.", nameof(version));
        }
        CurrentVersionId = version.Id;
    }
}
