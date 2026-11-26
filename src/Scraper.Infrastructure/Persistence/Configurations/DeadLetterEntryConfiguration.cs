using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Crawling;
using Scraper.Domain.Sites;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class DeadLetterEntryConfiguration : IEntityTypeConfiguration<DeadLetterEntry>
{
    public void Configure(EntityTypeBuilder<DeadLetterEntry> builder)
    {
        builder.ToTable("dead_letter_entries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.SiteId).HasMaxLength(32);
        builder.Property(d => d.Url).IsNormalizedUrl();

        // One entry per task: ON CONFLICT (crawl_task_id) DO UPDATE SET last_error, attempt_count, dead_lettered_at.
        builder.HasIndex(d => d.CrawlTaskId).IsUnique();

        builder.HasOne<CrawlTask>().WithMany().HasForeignKey(d => d.CrawlTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiteConfig>().WithMany().HasForeignKey(d => d.SiteId).OnDelete(DeleteBehavior.Restrict);
    }
}
