using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Content;
using Scraper.Domain.Urls;

namespace Scraper.Infrastructure.Persistence.Converters;

/// <summary>Shared column mappings for the domain's value objects and enums.</summary>
internal static class Conversions
{
    public const int EnumLength = 32;

    /// <summary>NormalizedUrl ↔ text. Re-normalizing on read is safe because normalization is idempotent.</summary>
    public static PropertyBuilder<NormalizedUrl> IsNormalizedUrl(this PropertyBuilder<NormalizedUrl> property) =>
        property.HasConversion(url => url.Value, value => NormalizedUrl.Create(value)).HasMaxLength(NormalizedUrl.MaxLength);

    public static PropertyBuilder<ContentHash> IsContentHash(this PropertyBuilder<ContentHash> property) =>
        property.HasConversion(hash => hash.Value, value => ContentHash.Parse(value))
            .HasMaxLength(ContentHash.Length)
            .IsFixedLength();

    /// <summary>Enums are stored as their names: readable in psql and safe to reorder.</summary>
    public static PropertyBuilder<TEnum> IsEnumText<TEnum>(this PropertyBuilder<TEnum> property) =>
        property.HasConversion<string>().HasMaxLength(EnumLength);

    public static PropertyBuilder<TEnum?> IsEnumText<TEnum>(this PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(EnumLength);

    /// <summary>A read-only string list ↔ <c>text[]</c>.</summary>
    public static PropertyBuilder<IReadOnlyList<string>> IsTextArray(this PropertyBuilder<IReadOnlyList<string>> property) =>
        property.HasConversion(
                list => list.ToArray(),
                array => array.ToList(),
                StringListComparer)
            .HasColumnType("text[]");

    public static PropertyBuilder<IReadOnlyList<string>?> IsNullableTextArray(this PropertyBuilder<IReadOnlyList<string>?> property) =>
        property.HasConversion(
                list => list == null ? null : list.ToArray(),
                array => array == null ? null : array.ToList(),
                NullableStringListComparer)
            .HasColumnType("text[]");

    private static readonly ValueComparer<IReadOnlyList<string>> StringListComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        list => list.ToList());

    private static readonly ValueComparer<IReadOnlyList<string>?> NullableStringListComparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        list => list == null ? 0 : list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        list => list == null ? null : list.ToList());
}
