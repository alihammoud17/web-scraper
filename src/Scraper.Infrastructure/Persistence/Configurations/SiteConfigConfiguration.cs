using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scraper.Domain.Sites;
using Scraper.Domain.Urls;
using Scraper.Infrastructure.Persistence.Converters;

namespace Scraper.Infrastructure.Persistence.Configurations;

internal sealed class SiteConfigConfiguration : IEntityTypeConfiguration<SiteConfig>
{
    private static readonly ValueComparer<IReadOnlyList<NormalizedUrl>> UrlListComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        list => list.Aggregate(0, (hash, url) => HashCode.Combine(hash, url)),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<SiteConfig> builder)
    {
        builder.ToTable("sites");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasMaxLength(32);
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.BaseUrl).IsNormalizedUrl();
        builder.Property(s => s.SeedUrls)
            .HasConversion(
                urls => urls.Select(u => u.Value).ToArray(),
                values => values.Select(NormalizedUrl.Create).ToList(),
                UrlListComparer)
            .HasColumnType("text[]");
        builder.Property(s => s.AllowedPathPrefixes).IsTextArray();
        builder.Property(s => s.FetchMode).IsEnumText();
        builder.Property(s => s.RateLimit)
            .HasConversion(r => r.RequestsPerSecond, value => new RateLimit(value))
            .HasColumnName("requests_per_second")
            .HasPrecision(4, 2);

        builder.HasData(SiteSeed.All);
    }
}
