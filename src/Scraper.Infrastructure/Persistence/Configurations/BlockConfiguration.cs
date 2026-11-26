using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Content;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

/// <summary>
/// All blocks share one table (table-per-hierarchy): a <c>block_type</c> discriminator separates
/// content blocks from tables, and <c>kind</c> says heading / paragraph / list / code. Each block is a
/// row with a stable id for Phase 2; only table cells are jsonb.
/// </summary>
internal sealed class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public const string VersionForeignKey = "DocumentVersionId";

    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.ToTable("content_blocks");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property<Guid>(VersionForeignKey);
        builder.HasDiscriminator<string>("block_type")
            .HasValue<ContentBlock>("content")
            .HasValue<TableBlock>("table");
        builder.Property("block_type").HasMaxLength(16);

        // Written once, together with their (new) version.
        builder.HasIndex(VersionForeignKey, nameof(Block.Ordinal)).IsUnique();
    }
}

internal sealed class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        builder.Property(b => b.Kind).IsEnumText();
        builder.Property(b => b.ListItems).IsNullableTextArray();
        builder.Property(b => b.CodeLanguage).HasMaxLength(50);
    }
}

internal sealed class TableBlockConfiguration : IEntityTypeConfiguration<TableBlock>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<TableBlock> builder)
    {
        builder.Property(b => b.Header)
            .HasConversion(
                JsonColumn.Converter<IReadOnlyList<string>>(Json),
                JsonColumn.Comparer<IReadOnlyList<string>>(Json))
            .HasColumnType(JsonColumn.ColumnType);
        builder.Property(b => b.Rows)
            .HasConversion(
                JsonColumn.Converter<IReadOnlyList<IReadOnlyList<string>>>(Json),
                JsonColumn.Comparer<IReadOnlyList<IReadOnlyList<string>>>(Json))
            .HasColumnType(JsonColumn.ColumnType);
    }
}
