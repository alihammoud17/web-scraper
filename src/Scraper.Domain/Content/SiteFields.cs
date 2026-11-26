using System.Globalization;

namespace Scraper.Domain.Content;

/// <summary>
/// Fields only one site has (stored as jsonb, written only through these typed records). They are
/// content, so they're part of the hash. Volatile counters are not: those go to <see cref="MetricsSnapshot"/>.
/// </summary>
public abstract record SiteFields
{
    /// <summary>The site key these fields belong to (matches <c>SiteConfig.Id</c>).</summary>
    public abstract string Site { get; }

    /// <summary>
    /// The fields as key/value text for hashing. Order between different keys doesn't matter (the
    /// hash sorts by key); repeated keys keep their order, which is how list items are written.
    /// </summary>
    public abstract IEnumerable<KeyValuePair<string, string?>> CanonicalFields();

    protected static KeyValuePair<string, string?> Field(string key, string? value) => new(key, value);

    protected static string? Format(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture);

    protected static string? Format(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    protected static string? Format(TimeSpan? value) => value?.ToString("c", CultureInfo.InvariantCulture);

    protected static string? Format(DateTimeOffset? value) => value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    protected static IEnumerable<KeyValuePair<string, string?>> ListFields(string key, IReadOnlyList<string> values) =>
        values.Select(value => Field(key + ".item", value))
            .Prepend(Field(key, values.Count.ToString(CultureInfo.InvariantCulture)));
}
