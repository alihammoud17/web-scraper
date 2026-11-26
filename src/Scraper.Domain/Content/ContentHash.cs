using System.Security.Cryptography;
using System.Text;

namespace Scraper.Domain.Content;

/// <summary>SHA-256 of a document's canonical content, as 64 lowercase hex characters (ADR 0009).</summary>
public sealed record ContentHash
{
    public const int Length = 64;

    private ContentHash(string value) => Value = value;

    public string Value { get; }

    /// <summary>Hashes an already canonical string. Use <see cref="DocumentContent.ComputeHash"/> for documents.</summary>
    public static ContentHash Compute(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return new ContentHash(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    /// <summary>Restores a stored hash. Accepts upper- or lowercase hex and stores it lowercase.</summary>
    public static ContentHash Parse(string value)
    {
        if (value is null || value.Length != Length || !value.All(char.IsAsciiHexDigit))
        {
            throw new FormatException($"A content hash is {Length} hexadecimal characters.");
        }
        return new ContentHash(value.ToLowerInvariant());
    }

    public override string ToString() => Value;
}
