using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Pages;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class LinkConfiguration : IEntityTypeConfiguration<Link>
{
    public void Configure(EntityTypeBuilder<Link> builder)
    {
        builder.ToTable("links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.TargetUrl).IsNormalizedUrl();
        builder.Property(l => l.Text).HasMaxLength(Link.MaxTextLength);

        // One edge per (source, target): ON CONFLICT DO UPDATE SET text, last_seen_at.
        builder.HasIndex(l => new { l.SourcePageId, l.TargetUrl }).IsUnique();

        builder.HasOne<Page>().WithMany().HasForeignKey(l => l.SourcePageId).OnDelete(DeleteBehavior.Restrict);
    }
}
