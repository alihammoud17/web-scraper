using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Pages;
using Scraper.Domain.Sites;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("pages");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.SiteId).HasMaxLength(32);
        builder.Property(p => p.Url).IsNormalizedUrl();
        builder.Ignore(p => p.IsRemoved);

        // One row per page: ON CONFLICT (site_id, url) DO UPDATE … RETURNING id.
        builder.HasIndex(p => new { p.SiteId, p.Url }).IsUnique();

        builder.HasOne<SiteConfig>().WithMany().HasForeignKey(p => p.SiteId).OnDelete(DeleteBehavior.Restrict);
    }
}
