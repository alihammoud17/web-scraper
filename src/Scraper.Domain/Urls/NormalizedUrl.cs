using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Scraper.Domain.Urls;

/// <summary>
/// An absolute http(s) URL in one canonical form, so that equivalent spellings of the same page
/// (host case, default port, fragment, trailing slash, tracking parameters, query order) compare
/// equal. It is the identity of a page per site and the key of the seen set.
/// </summary>
public sealed partial record NormalizedUrl
{
    /// <summary>Longest accepted URL. Also keeps the value well inside PostgreSQL's B-tree index row limit.</summary>
    public const int MaxLength = 2048;

    private static readonly string[] TrackingParameters = ["fbclid", "gclid"];

    private NormalizedUrl(string value) => Value = value;

    public string Value { get; }

    /// <summary><c>http</c> or <c>https</c>.</summary>
    public string Scheme => AsUri.Scheme;

    /// <summary>Lowercase host (punycode for international names), without the port.</summary>
    public string Host => AsUri.Host;

    /// <summary>Host plus the port when it isn't the scheme's default, e.g. <c>testsite:8080</c>.</summary>
    public string Authority => AsUri.IsDefaultPort ? AsUri.Host : AsUri.Authority;

    /// <summary>Path without the query, always starting with <c>/</c>.</summary>
    public string Path => AsUri.AbsolutePath;

    /// <summary>Path plus query, as robots.txt rules are matched against.</summary>
    public string PathAndQuery => AsUri.PathAndQuery;

    // Not cached: a record compares all its fields, so a cached Uri would break value equality.
    private Uri AsUri => new(Value, UriKind.Absolute);

    /// <summary>Normalizes an absolute URL, or throws <see cref="ArgumentException"/> if it isn't crawlable.</summary>
    public static NormalizedUrl Create(string url) =>
        TryCreate(url, null, out var result)
            ? result
            : throw new ArgumentException($"'{url}' is not a crawlable absolute http(s) URL.", nameof(url));

    /// <summary>
    /// Normalizes <paramref name="url"/>. A relative URL (e.g. a link's href) is resolved against
    /// <paramref name="baseUri"/>; without a base it is rejected.
    /// </summary>
    public static bool TryCreate(string? url, Uri? baseUri, [NotNullWhen(true)] out NormalizedUrl? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        Uri? uri;
        if (baseUri is null)
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }
        }
        else if (!baseUri.IsAbsoluteUri || !Uri.TryCreate(baseUri, url.Trim(), out uri))
        {
            return false;
        }

        // Only web pages; never credentials embedded in a URL.
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.UserInfo.Length > 0)
        {
            return false;
        }

        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost.ToLowerInvariant();
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);

        // Uri has already removed dot segments. The fragment is dropped by not copying it.
        var path = UppercaseEscapes(uri.AbsolutePath);
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }
        if (path.Length == 0)
        {
            path = "/";
        }

        var value = $"{uri.Scheme}://{host}{port}{path}{NormalizeQuery(uri.Query)}";
        if (value.Length > MaxLength)
        {
            return false;
        }

        result = new NormalizedUrl(value);
        return true;
    }

    public override string ToString() => Value;

    // Drops empty and tracking parameters, then sorts by key and value so parameter order doesn't matter.
    private static string NormalizeQuery(string query)
    {
        var parameters = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(UppercaseEscapes)
            .Select(part =>
            {
                var separator = part.IndexOf('=', StringComparison.Ordinal);
                return separator < 0 ? (Key: part, Value: string.Empty, Text: part) : (Key: part[..separator], Value: part[(separator + 1)..], Text: part);
            })
            .Where(p => !IsTrackingParameter(p.Key))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => p.Text)
            .ToList();

        return parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters);
    }

    private static bool IsTrackingParameter(string key) =>
        key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
        || TrackingParameters.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static string UppercaseEscapes(string text) =>
        PercentEscape().Replace(text, m => m.Value.ToUpperInvariant());

    [GeneratedRegex("%[0-9a-fA-F]{2}")]
    private static partial Regex PercentEscape();
}
