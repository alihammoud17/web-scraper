using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Scraper.Domain.Content;
using Scraper.Domain.Crawling;
using Scraper.Domain.Pages;
using Scraper.Infrastructure.Persistence;

namespace Scraper.Infrastructure.Tests;

/// <summary>Checks the EF model and migrations without a database (building the model never connects).</summary>
public sealed class ModelTests : IDisposable
{
    private readonly ScraperDbContext _context = new ScraperDbContextFactory().CreateDbContext([]);

    [Fact]
    public void Migrations_match_the_model()
    {
        Assert.False(
            _context.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run 'dotnet ef migrations add <Name>' (see CLAUDE.md).");
    }

    // The natural keys that make every write an idempotent upsert (see docs/plans, P3).
    [Theory]
    [InlineData(typeof(Page), new[] { nameof(Page.SiteId), nameof(Page.Url) })]
    [InlineData(typeof(CrawlTask), new[] { nameof(CrawlTask.CrawlJobId), nameof(CrawlTask.Url) })]
    [InlineData(typeof(PageFetch), new[] { nameof(PageFetch.CrawlTaskId) })]
    [InlineData(typeof(Link), new[] { nameof(Link.SourcePageId), nameof(Link.TargetUrl) })]
    [InlineData(typeof(Document), new[] { nameof(Document.PageId) })]
    [InlineData(typeof(DocumentVersion), new[] { nameof(DocumentVersion.DocumentId), nameof(DocumentVersion.ContentHash) })]
    [InlineData(typeof(Block), new[] { "DocumentVersionId", nameof(Block.Ordinal) })]
    [InlineData(typeof(MetricsSnapshot), new[] { nameof(MetricsSnapshot.CrawlTaskId) })]
    [InlineData(typeof(DeadLetterEntry), new[] { nameof(DeadLetterEntry.CrawlTaskId) })]
    public void Natural_keys_have_unique_indexes(Type entity, string[] properties)
    {
        var entityType = _context.Model.FindEntityType(entity);
        Assert.NotNull(entityType);

        var index = entityType.GetIndexes()
            .SingleOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual(properties));

        Assert.NotNull(index);
        Assert.True(index.IsUnique, $"Index on {entity.Name}({string.Join(", ", properties)}) must be unique.");
    }

    [Fact]
    public void No_foreign_key_cascades_deletes()
    {
        // Pages and content are never deleted (marked removed instead); RESTRICT makes an accidental delete fail.
        var offenders = _context.Model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredForeignKeys())
            .Where(fk => fk.DeleteBehavior != DeleteBehavior.Restrict)
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name} → {fk.PrincipalEntityType.ClrType.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Foreign keys without Restrict: " + string.Join(", ", offenders));
    }

    [Theory]
    [MemberData(nameof(SiteFieldsSamples))]
    public void Site_fields_round_trip_through_the_jsonb_converter(SiteFields fields)
    {
        var converter = SiteFieldsProperty().GetValueConverter();
        Assert.NotNull(converter);

        var json = (string)converter.ConvertToProvider(fields)!;
        var restored = (SiteFields)converter.ConvertFromProvider(json)!;

        Assert.Contains($"\"site\":\"{fields.Site}\"", json, StringComparison.Ordinal);
        Assert.Equal(fields.GetType(), restored.GetType());
        Assert.Equal(fields.CanonicalFields(), restored.CanonicalFields());
    }

    public static TheoryData<SiteFields> SiteFieldsSamples() =>
    [
        new GtaCarsFields("Truffade", "Super", 1_000_000, 250.5m, TimeSpan.FromSeconds(62.4)),
        new NewswireFields("GTA Online", ["Bonuses", "Podium"]),
        new Gta5ModsFields("someauthor", "1.2", ["Car", "Add-On"], DateTimeOffset.UnixEpoch, null),
    ];

    public void Dispose() => _context.Dispose();

    private IProperty SiteFieldsProperty() =>
        _context.Model.FindEntityType(typeof(DocumentVersion))!.FindProperty(nameof(DocumentVersion.SiteFields))!;
}
