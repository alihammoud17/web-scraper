namespace Scraper.Domain.Content;

/// <summary>
/// An immutable snapshot of a document's content. (document, content hash) is unique: a re-crawl
/// with unchanged content creates no new version (ADR 0009).
/// </summary>
public sealed class DocumentVersion
{
    private readonly List<Block> _blocks = [];

    private DocumentVersion()
    {
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public ContentHash ContentHash { get; private set; } = null!;

    public DateTimeOffset CapturedAt { get; private set; }

    public string Title { get; private set; } = null!;

    /// <summary>The cross-site join key for Phase 2; indexed.</summary>
    public string? VehicleName { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public SiteFields SiteFields { get; private set; } = null!;

    /// <summary>Blocks in document order (<see cref="Block.Ordinal"/> 0, 1, 2, …).</summary>
    public IReadOnlyList<Block> Blocks => _blocks;

    public static DocumentVersion Create(Guid documentId, DocumentContent content, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(content);
        var version = new DocumentVersion
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            ContentHash = content.ComputeHash(),
            CapturedAt = capturedAt,
            Title = content.Title,
            VehicleName = content.VehicleName,
            PublishedAt = content.PublishedAt,
            SiteFields = content.SiteFields,
        };
        for (var i = 0; i < content.Blocks.Count; i++)
        {
            content.Blocks[i].Ordinal = i;
            version._blocks.Add(content.Blocks[i]);
        }
        return version;
    }
}
