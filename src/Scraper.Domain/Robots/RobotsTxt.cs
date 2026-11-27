using System.Text;

namespace Scraper.Domain.Robots;

/// <summary>
/// A parsed robots.txt, following RFC 9309. Group selection: the group(s) naming our product token
/// (case-insensitive), otherwise the <c>*</c> group(s), otherwise everything is allowed. Within the
/// selected rules the longest matching pattern wins, and Allow wins a tie.
/// </summary>
public sealed class RobotsTxt
{
    /// <summary>RFC 9309 asks crawlers to parse at least 500 KiB; anything after that is ignored.</summary>
    public const int MaxBytes = 500 * 1024;

    private const string RobotsPath = "/robots.txt";

    private readonly IReadOnlyList<Group> _groups;
    private readonly bool? _blanket;

    private RobotsTxt(IReadOnlyList<Group> groups, bool? blanket)
    {
        _groups = groups;
        _blanket = blanket;
    }

    /// <summary>No restrictions (robots.txt missing, 4xx).</summary>
    public static RobotsTxt AllowAll { get; } = new([], blanket: true);

    /// <summary>Everything forbidden (robots.txt unreachable: 5xx or network error).</summary>
    public static RobotsTxt DisallowAll { get; } = new([], blanket: false);

    /// <summary>
    /// Maps a robots.txt response to rules: 2xx is parsed, 4xx (including 404) and an unfollowed
    /// redirect mean "unavailable" (allow all), and anything else means "unreachable" (disallow all).
    /// </summary>
    public static RobotsTxt FromResponse(int statusCode, string? body) => statusCode switch
    {
        >= 200 and <= 299 => Parse(body ?? string.Empty),
        >= 300 and <= 499 => AllowAll,
        _ => DisallowAll,
    };

    /// <summary>The product token RFC 9309 matches on: the part of a User-Agent before the first '/' or space.</summary>
    public static string ProductTokenOf(string userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        var token = userAgent.Trim();
        var end = token.IndexOfAny(['/', ' ']);
        return end < 0 ? token : token[..end];
    }

    public static RobotsTxt Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Encoding.UTF8.GetByteCount(content) > MaxBytes)
        {
            content = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(content), 0, MaxBytes);
        }

        var groups = new List<Group>();
        Group? current = null;
        var lastLineWasAgent = false;

        foreach (var rawLine in content.TrimStart('﻿').Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var comment = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = (comment < 0 ? rawLine : rawLine[..comment]).Trim();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "user-agent":
                    // Consecutive user-agent lines share one group; one after rules starts a new group.
                    if (current is null || !lastLineWasAgent)
                    {
                        current = new Group();
                        groups.Add(current);
                    }
                    current.Agents.Add(value);
                    lastLineWasAgent = true;
                    break;
                case "allow" or "disallow":
                    // Rules before any user-agent line belong to no group. An empty path matches nothing.
                    if (current is not null && value.Length > 0)
                    {
                        current.Rules.Add(new Rule(key == "allow", new RobotsPattern(value)));
                    }
                    lastLineWasAgent = false;
                    break;
                default:
                    // sitemap, crawl-delay (not in the RFC) and unknown keys are ignored.
                    break;
            }
        }

        return new RobotsTxt(groups, blanket: null);
    }

    /// <summary>Whether a crawler with this product token may fetch the given path (with query).</summary>
    public bool IsAllowed(string productToken, string pathAndQuery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productToken);
        var path = string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;
        if (path == RobotsPath)
        {
            return true;
        }
        if (_blanket is { } blanket)
        {
            return blanket;
        }

        var rules = SelectRules(productToken.Trim());
        var normalized = RobotsPattern.NormalizeEncoding(path);
        Rule? best = null;
        foreach (var rule in rules)
        {
            if (!rule.Pattern.Matches(normalized))
            {
                continue;
            }
            if (best is null
                || rule.Pattern.Value.Length > best.Pattern.Value.Length
                || (rule.Pattern.Value.Length == best.Pattern.Value.Length && rule.Allow))
            {
                best = rule;
            }
        }
        return best?.Allow ?? true;
    }

    private IEnumerable<Rule> SelectRules(string productToken)
    {
        var named = _groups.Where(g => g.Agents.Any(a => a.Equals(productToken, StringComparison.OrdinalIgnoreCase))).ToList();
        var selected = named.Count > 0 ? named : _groups.Where(g => g.Agents.Contains("*")).ToList();
        return selected.SelectMany(g => g.Rules);
    }

    private sealed class Group
    {
        public List<string> Agents { get; } = [];

        public List<Rule> Rules { get; } = [];
    }

    private sealed record Rule(bool Allow, RobotsPattern Pattern);
}
