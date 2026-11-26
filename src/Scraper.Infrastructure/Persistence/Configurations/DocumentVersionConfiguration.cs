using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Content;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.ContentHash).IsContentHash();
        builder.Property(v => v.VehicleName).HasMaxLength(200);
        builder.Property(v => v.SiteFields)
            .HasConversion(
                JsonColumn.Converter<SiteFields>(SiteFieldsJsonConverter.Options),
                JsonColumn.Comparer<SiteFields>(SiteFieldsJsonConverter.Options))
            .HasColumnType(JsonColumn.ColumnType);

        // Unchanged content creates no new version: ON CONFLICT (document_id, content_hash) DO NOTHING.
        builder.HasIndex(v => new { v.DocumentId, v.ContentHash }).IsUnique();
        // Phase 2 joins the three sites on the vehicle name.
        builder.HasIndex(v => v.VehicleName);

        builder.HasOne<Document>().WithMany().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(v => v.Blocks).WithOne().HasForeignKey(BlockConfiguration.VersionForeignKey).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(v => v.Blocks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
