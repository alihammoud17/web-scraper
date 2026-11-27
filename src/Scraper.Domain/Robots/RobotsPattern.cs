using System.Text;
using System.Text.RegularExpressions;

namespace Scraper.Domain.Robots;

/// <summary>
/// One Allow/Disallow path pattern (RFC 9309 §2.2.2): <c>*</c> matches any sequence of characters,
/// a trailing <c>$</c> anchors the end, everything else matches literally and case-sensitively from
/// the start of the path. Patterns and paths are compared after the same percent-encoding normalization.
/// </summary>
internal sealed class RobotsPattern
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Regex _regex;

    public RobotsPattern(string pattern)
    {
        Value = NormalizeEncoding(pattern);
        _regex = new Regex(ToRegex(Value), RegexOptions.CultureInvariant, MatchTimeout);
    }

    /// <summary>The normalized pattern; its length is the rule's priority (longest match wins).</summary>
    public string Value { get; }

    public bool Matches(string normalizedPath) => _regex.IsMatch(normalizedPath);

    /// <summary>
    /// Makes equivalent spellings comparable: escapes of unreserved characters are decoded, other
    /// escapes get uppercase hex, and non-ASCII characters are percent-encoded as UTF-8.
    /// </summary>
    internal static string NormalizeEncoding(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '%' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
            {
                var decoded = (char)Convert.ToByte(text.Substring(i + 1, 2), 16);
                if (IsUnreserved(decoded))
                {
                    builder.Append(decoded);
                }
                else
                {
                    builder.Append('%').Append(char.ToUpperInvariant(text[i + 1])).Append(char.ToUpperInvariant(text[i + 2]));
                }
                i += 2;
            }
            else if (c > 0x7F)
            {
                var length = char.IsHighSurrogate(c) && i + 1 < text.Length ? 2 : 1;
                foreach (var b in Encoding.UTF8.GetBytes(text.Substring(i, length)))
                {
                    builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
                i += length - 1;
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';

    private static string ToRegex(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;
        var regex = "^" + string.Join(".*", body.Split('*').Select(Regex.Escape));
        return anchored ? regex + "$" : regex;
    }
}
