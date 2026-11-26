using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Crawling;
using Scraper.Domain.Pages;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class PageFetchConfiguration : IEntityTypeConfiguration<PageFetch>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<PageFetch> builder)
    {
        builder.ToTable("page_fetches");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.FetchMode).IsEnumText();
        builder.Property(f => f.Headers)
            .HasConversion(
                JsonColumn.Converter<IReadOnlyDictionary<string, string>>(Json),
                JsonColumn.Comparer<IReadOnlyDictionary<string, string>>(Json))
            .HasColumnType(JsonColumn.ColumnType);

        // One observation per task, so a redelivery doesn't add a second: ON CONFLICT (crawl_task_id) DO NOTHING.
        builder.HasIndex(f => f.CrawlTaskId).IsUnique();

        builder.HasOne<Page>().WithMany().HasForeignKey(f => f.PageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrawlTask>().WithMany().HasForeignKey(f => f.CrawlTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
