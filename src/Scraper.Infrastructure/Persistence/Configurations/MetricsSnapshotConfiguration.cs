using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Content;
using Scraper.Domain.Crawling;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class MetricsSnapshotConfiguration : IEntityTypeConfiguration<MetricsSnapshot>
{
    public void Configure(EntityTypeBuilder<MetricsSnapshot> builder)
    {
        builder.ToTable("metrics_snapshots");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        // One snapshot per task: ON CONFLICT (crawl_task_id) DO NOTHING.
        builder.HasIndex(m => m.CrawlTaskId).IsUnique();

        builder.HasOne<Document>().WithMany().HasForeignKey(m => m.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrawlTask>().WithMany().HasForeignKey(m => m.CrawlTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
