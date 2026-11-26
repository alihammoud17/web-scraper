using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Scraper.Infrastructure.Persistence.Converters;

/// <summary>
/// Maps a value to a <c>jsonb</c> column with System.Text.Json. The comparer compares serialized JSON,
/// so EF detects changes inside lists and dictionaries, not just a swapped reference.
/// </summary>
internal static class JsonColumn
{
    public const string ColumnType = "jsonb";

    public static ValueConverter<T, string> Converter<T>(JsonSerializerOptions options) =>
        new(value => JsonSerializer.Serialize(value, options), json => JsonSerializer.Deserialize<T>(json, options)!);

    public static ValueComparer<T> Comparer<T>(JsonSerializerOptions options) =>
        new(
            (a, b) => JsonSerializer.Serialize(a, options) == JsonSerializer.Serialize(b, options),
            value => JsonSerializer.Serialize(value, options).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!);
}
