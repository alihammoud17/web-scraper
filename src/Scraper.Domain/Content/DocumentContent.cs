using System.Globalization;

namespace Scraper.Domain.Content;

/// <summary>
/// Everything that defines a document version's content, and nothing else: it is exactly the hash
/// input (ADR 0009). Metrics, links, raw HTML, headers and fetch times are deliberately not members,
/// so they can't change the hash.
/// </summary>
public sealed class DocumentContent
{
    public DocumentContent(
        string title,
        string? vehicleName,
        DateTimeOffset? publishedAt,
        SiteFields siteFields,
        IReadOnlyList<Block> blocks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(siteFields);
        ArgumentNullException.ThrowIfNull(blocks);
        Title = title;
        VehicleName = vehicleName;
        PublishedAt = publishedAt;
        SiteFields = siteFields;
        Blocks = blocks;
    }

    public string Title { get; }

    /// <summary>The cross-site join key for Phase 2; null when the page isn't about one vehicle.</summary>
    public string? VehicleName { get; }

    public DateTimeOffset? PublishedAt { get; }

    public SiteFields SiteFields { get; }

    public IReadOnlyList<Block> Blocks { get; }

    public ContentHash ComputeHash()
    {
        var writer = new CanonicalWriter();
        writer.Add("title", Title);
        writer.Add("vehicle", VehicleName);
        writer.Add("published", PublishedAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        writer.Add("site", SiteFields.Site);

        // OrderBy is stable: different keys get sorted, repeated keys (list items) keep their order.
        foreach (var (key, value) in SiteFields.CanonicalFields().OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            writer.Add("field." + key, value);
        }

        writer.Add("blocks", Blocks.Count);
        foreach (var block in Blocks)
        {
            block.WriteCanonical(writer);
        }

        return ContentHash.Compute(writer.ToString());
    }
}
