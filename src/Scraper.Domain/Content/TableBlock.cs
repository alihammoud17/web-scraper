namespace Scraper.Domain.Content;

/// <summary>A table kept as rows and columns (e.g. GTACars stats tables), never flattened to text.</summary>
public sealed class TableBlock : Block
{
    private TableBlock()
    {
    }

    public TableBlock(string? caption, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);
        Caption = caption;
        Header = [.. header];
        Rows = [.. rows.Select(row => (IReadOnlyList<string>)[.. row])];
    }

    public string? Caption { get; private set; }

    /// <summary>Column headings; empty if the table has no header row.</summary>
    public IReadOnlyList<string> Header { get; private set; } = [];

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; private set; } = [];

    internal override void WriteCanonical(CanonicalWriter writer)
    {
        writer.Add("block", "Table");
        writer.Add("caption", Caption);
        writer.AddList("header", Header);
        writer.Add("rows", Rows.Count);
        foreach (var row in Rows)
        {
            writer.AddList("row", row);
        }
    }
}
