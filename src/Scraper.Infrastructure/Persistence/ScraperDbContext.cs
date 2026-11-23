using Microsoft.EntityFrameworkCore;

namespace Scraper.Infrastructure.Persistence;

/// <summary>EF Core context for the scraper database. Entities arrive in Milestone 3.</summary>
public sealed class ScraperDbContext(DbContextOptions<ScraperDbContext> options) : DbContext(options);
