namespace Scraper.Domain.Content;

/// <summary>A heading, paragraph, list or code block. Tables are <see cref="TableBlock"/>.</summary>
public sealed class ContentBlock : Block
{
    private ContentBlock()
    {
    }

    public ContentBlockKind Kind { get; private set; }

    /// <summary>Text of a heading, paragraph or code block; null for a list.</summary>
    public string? Text { get; private set; }

    public int? HeadingLevel { get; private set; }

    public IReadOnlyList<string>? ListItems { get; private set; }

    public bool? ListOrdered { get; private set; }

    public string? CodeLanguage { get; private set; }

    public static ContentBlock Heading(string text, int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 6);
        return new ContentBlock { Kind = ContentBlockKind.Heading, Text = RequireText(text), HeadingLevel = level };
    }

    public static ContentBlock Paragraph(string text) =>
        new() { Kind = ContentBlockKind.Paragraph, Text = RequireText(text) };

    public static ContentBlock List(IEnumerable<string> items, bool ordered)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ContentBlock { Kind = ContentBlockKind.List, ListItems = [.. items], ListOrdered = ordered };
    }

    public static ContentBlock Code(string text, string? language = null) =>
        new() { Kind = ContentBlockKind.Code, Text = RequireText(text), CodeLanguage = language };

    // Code is normalized like other text: an indentation-only change is not treated as a new version.
    internal override void WriteCanonical(CanonicalWriter writer)
    {
        writer.Add("block", Kind.ToString());
        switch (Kind)
        {
            case ContentBlockKind.Heading:
                writer.Add("level", HeadingLevel ?? 0);
                writer.Add("text", Text);
                break;
            case ContentBlockKind.List:
                writer.Add("ordered", ListOrdered == true ? "1" : "0");
                writer.AddList("items", ListItems ?? []);
                break;
            case ContentBlockKind.Code:
                writer.Add("language", CodeLanguage);
                writer.Add("text", Text);
                break;
            default:
                writer.Add("text", Text);
                break;
        }
    }

    private static string RequireText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text;
    }
}
