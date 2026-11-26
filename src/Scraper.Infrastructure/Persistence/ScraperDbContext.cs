using Microsoft.EntityFrameworkCore;
using Scraper.Domain.Content;
using Scraper.Domain.Crawling;
using Scraper.Domain.Pages;
using Scraper.Domain.Sites;

namespace Scraper.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the scraper database. Mappings live in <c>Configurations/</c>; tables and
/// columns are snake_case so the raw <c>INSERT … ON CONFLICT</c> upserts read naturally.
/// </summary>
public sealed class ScraperDbContext(DbContextOptions<ScraperDbContext> options) : DbContext(options)
{
    public DbSet<SiteConfig> Sites => Set<SiteConfig>();

    public DbSet<CrawlJob> CrawlJobs => Set<CrawlJob>();

    public DbSet<CrawlTask> CrawlTasks => Set<CrawlTask>();

    public DbSet<DeadLetterEntry> DeadLetterEntries => Set<DeadLetterEntry>();

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<PageFetch> PageFetches => Set<PageFetch>();

    public DbSet<Link> Links => Set<Link>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<Block> ContentBlocks => Set<Block>();

    public DbSet<MetricsSnapshot> MetricsSnapshots => Set<MetricsSnapshot>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScraperDbContext).Assembly);
}
