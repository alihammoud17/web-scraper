namespace Scraper.Domain.Content;

/// <summary>
/// GTA5-Mods.com mod metadata. The author is stored as a username only (compliance notes); downloads,
/// likes and views are volatile and live in <see cref="MetricsSnapshot"/>, outside the hash.
/// </summary>
public sealed record Gta5ModsFields(
    string? Author,
    string? ModVersion,
    IReadOnlyList<string> Tags,
    DateTimeOffset? UploadedAt,
    DateTimeOffset? LastUpdatedAt) : SiteFields
{
    public const string SiteKey = "gta5mods";

    public override string Site => SiteKey;

    public override IEnumerable<KeyValuePair<string, string?>> CanonicalFields() =>
        ListFields("tags", Tags).Concat(
        [
            Field("author", Author),
            Field("version", ModVersion),
            Field("uploaded_at", Format(UploadedAt)),
            Field("last_updated_at", Format(LastUpdatedAt)),
        ]);
}
