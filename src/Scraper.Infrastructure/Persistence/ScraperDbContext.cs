using Microsoft.EntityFrameworkCore;

namespace Scraper.Infrastructure.Persistence;

/// <summary>EF Core context for the scraper database. The domain model is mapped in P3.</summary>
public sealed class ScraperDbContext(DbContextOptions<ScraperDbContext> options) : DbContext(options);
