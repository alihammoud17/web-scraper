using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Crawling;
using Scraper.Domain.Sites;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class CrawlJobConfiguration : IEntityTypeConfiguration<CrawlJob>
{
    public void Configure(EntityTypeBuilder<CrawlJob> builder)
    {
        builder.ToTable("crawl_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.SiteId).HasMaxLength(32);
        builder.Property(j => j.Mode).IsEnumText();
        builder.Property(j => j.Status).IsEnumText();

        builder.HasOne<SiteConfig>().WithMany().HasForeignKey(j => j.SiteId).OnDelete(DeleteBehavior.Restrict);
    }
}
