using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scraper.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling at design time. Generating a migration needs no live
/// database, so a password-less local fallback is enough when the variable isn't set.
/// </summary>
public sealed class ScraperDbContextFactory : IDesignTimeDbContextFactory<ScraperDbContext>
{
    public ScraperDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=scraper";
        var options = new DbContextOptionsBuilder<ScraperDbContext>().UseNpgsql(connectionString).Options;
        return new ScraperDbContext(options);
    }
}
