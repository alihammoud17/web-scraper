using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Content;
using Scraper.Domain.Pages;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Kind).IsEnumText();

        // 1:1 with a content page: ON CONFLICT (page_id) DO NOTHING.
        builder.HasIndex(d => d.PageId).IsUnique();

        builder.HasOne<Page>().WithOne().HasForeignKey<Document>(d => d.PageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentVersion>().WithMany().HasForeignKey(d => d.CurrentVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
