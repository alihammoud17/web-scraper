using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Crawling;
using Scraper.Domain.Pages;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class CrawlTaskConfiguration : IEntityTypeConfiguration<CrawlTask>
{
    public void Configure(EntityTypeBuilder<CrawlTask> builder)
    {
        builder.ToTable("crawl_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Url).IsNormalizedUrl();
        builder.Property(t => t.Status).IsEnumText();

        // Idempotent enqueue: INSERT … ON CONFLICT (crawl_job_id, url) DO NOTHING.
        builder.HasIndex(t => new { t.CrawlJobId, t.Url }).IsUnique();
        // Job progress queries (count per status).
        builder.HasIndex(t => new { t.CrawlJobId, t.Status });

        builder.HasOne<CrawlJob>().WithMany().HasForeignKey(t => t.CrawlJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Page>().WithMany().HasForeignKey(t => t.PageId).OnDelete(DeleteBehavior.Restrict);
    }
}
