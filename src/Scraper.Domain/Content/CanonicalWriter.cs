using System.Globalization;
using System.Text;

namespace Scraper.Domain.Content;

/// <summary>
/// Builds the canonical string that <see cref="ContentHash"/> is computed over. Every part is written
/// as <c>tag:length:value</c>, so part boundaries can't be confused (("ab","c") ≠ ("a","bc")), and a
/// null value is written as <c>tag:~</c>, so null and empty differ. Text is Unicode NFC with
/// whitespace runs collapsed to one space and trimmed, so markup-only whitespace changes are not
/// content changes.
/// </summary>
internal sealed class CanonicalWriter
{
    /// <summary>Bump this when the rules change; every page then gets exactly one new version.</summary>
    private const string FormatVersion = "v1";

    private readonly StringBuilder _builder = new(FormatVersion + "\n");

    public void Add(string tag, string? value)
    {
        _builder.Append(tag).Append(':');
        if (value is null)
        {
            _builder.Append('~');
        }
        else
        {
            var text = NormalizeText(value);
            _builder.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
        }
        _builder.Append('\n');
    }

    public void Add(string tag, int value) => Add(tag, value.ToString(CultureInfo.InvariantCulture));

    public void AddList(string tag, IReadOnlyList<string> values)
    {
        Add(tag, values.Count);
        foreach (var value in values)
        {
            Add(tag + ".item", value);
        }
    }

    public override string ToString() => _builder.ToString();

    internal static string NormalizeText(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }
}
