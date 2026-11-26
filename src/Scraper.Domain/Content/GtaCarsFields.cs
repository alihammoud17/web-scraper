namespace Scraper.Domain.Content;

/// <summary>GTACars.net vehicle facts. Stats tables are stored as <see cref="TableBlock"/>s, not here.</summary>
public sealed record GtaCarsFields(
    string? Manufacturer,
    string? VehicleClass,
    long? PriceGtaDollars,
    decimal? TopSpeedKmh,
    TimeSpan? LapTime) : SiteFields
{
    public const string SiteKey = "gtacars";

    public override string Site => SiteKey;

    public override IEnumerable<KeyValuePair<string, string?>> CanonicalFields() =>
    [
        Field("manufacturer", Manufacturer),
        Field("class", VehicleClass),
        Field("price", Format(PriceGtaDollars)),
        Field("top_speed_kmh", Format(TopSpeedKmh)),
        Field("lap_time", Format(LapTime)),
    ];
}
