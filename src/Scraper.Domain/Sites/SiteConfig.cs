using System.Text.RegularExpressions;
using Scraper.Domain.Urls;

namespace Scraper.Domain.Sites;

/// <summary>
/// How one target site is crawled. Stored as a database row (seeded by a migration) so crawl jobs
/// can reference it and the dashboard can show it.
/// </summary>
public sealed partial class SiteConfig
{
    private SiteConfig()
    {
    }

    public SiteConfig(
        string id,
        string name,
        NormalizedUrl baseUrl,
        IReadOnlyList<NormalizedUrl> seedUrls,
        IReadOnlyList<string> allowedPathPrefixes,
        FetchMode fetchMode,
        RateLimit rateLimit,
        int maxDepth,
        int maxPages)
    {
        if (!SiteKey().IsMatch(id ?? string.Empty))
        {
            throw new ArgumentException("A site id is a short lowercase key such as 'gtacars'.", nameof(id));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(rateLimit);
        if (seedUrls is null || seedUrls.Count == 0)
        {
            throw new ArgumentException("A site needs at least one seed URL.", nameof(seedUrls));
        }
        ArgumentNullException.ThrowIfNull(allowedPathPrefixes);
        if (allowedPathPrefixes.Any(p => !p.StartsWith('/')))
        {
            throw new ArgumentException("Path prefixes start with '/'.", nameof(allowedPathPrefixes));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);

        Id = id!;
        Name = name;
        BaseUrl = baseUrl;
        SeedUrls = seedUrls;
        AllowedPathPrefixes = allowedPathPrefixes;
        FetchMode = fetchMode;
        RateLimit = rateLimit;
        MaxDepth = maxDepth;
        MaxPages = maxPages;
    }

    /// <summary>Readable natural key, e.g. <c>gtacars</c>, <c>newswire</c>, <c>gta5mods</c>.</summary>
    public string Id { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public NormalizedUrl BaseUrl { get; private set; } = null!;

    public IReadOnlyList<NormalizedUrl> SeedUrls { get; private set; } = [];

    /// <summary>Only URLs under these paths are crawled (e.g. Newswire stays on <c>/newswire</c>). Empty means the whole site.</summary>
    public IReadOnlyList<string> AllowedPathPrefixes { get; private set; } = [];

    public FetchMode FetchMode { get; private set; }

    public RateLimit RateLimit { get; private set; } = null!;

    public int MaxDepth { get; private set; }

    public int MaxPages { get; private set; }

    /// <summary>
    /// Whether a URL belongs to this site's crawl: same scheme and authority (host and port) as
    /// <see cref="BaseUrl"/>, and a path under one of <see cref="AllowedPathPrefixes"/> (all paths when
    /// the list is empty). A prefix matches whole segments: <c>/newswire</c> covers <c>/newswire/…</c>
    /// but not <c>/newswirefoo</c>.
    /// </summary>
    public bool IsInScope(NormalizedUrl url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != BaseUrl.Scheme || url.Authority != BaseUrl.Authority)
        {
            return false;
        }
        return AllowedPathPrefixes.Count == 0 || AllowedPathPrefixes.Any(prefix => IsUnder(url.Path, prefix));
    }

    private static bool IsUnder(string path, string prefix)
    {
        var trimmed = prefix.TrimEnd('/');
        return trimmed.Length == 0
            || path.Equals(trimmed, StringComparison.Ordinal)
            || path.StartsWith(trimmed + "/", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex SiteKey();
}
