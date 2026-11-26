namespace Scraper.Domain.Content;

/// <summary>
/// One structured piece of a document version, kept separate (never flattened) for Phase 2 RAG.
/// Each block is its own row, so later chunks can cite a stable block id.
/// </summary>
public abstract class Block
{
    protected Block() => Id = Guid.CreateVersion7();

    public Guid Id { get; private set; }

    /// <summary>Position within the version, starting at 0. Set when the version is created.</summary>
    public int Ordinal { get; internal set; }

    internal abstract void WriteCanonical(CanonicalWriter writer);
}
