namespace Scraper.Domain.Content;

/// <summary>Rockstar Newswire article metadata. The publish date is the shared <c>PublishedAt</c> column.</summary>
public sealed record NewswireFields(string? Category, IReadOnlyList<string> Tags) : SiteFields
{
    public const string SiteKey = "newswire";

    public override string Site => SiteKey;

    public override IEnumerable<KeyValuePair<string, string?>> CanonicalFields() =>
        ListFields("tags", Tags).Prepend(Field("category", Category));
}
