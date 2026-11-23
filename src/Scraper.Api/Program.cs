using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Scraper.Api;
using Scraper.Infrastructure;
using Scraper.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

// The Compose "migrator" service runs this image with --migrate-only: apply migrations, then exit 0.
if (args.Contains("--migrate-only"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ScraperDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Migrations applied; exiting");
    return 0;
}

// Exposed in every environment so the API can be explored in the Docker demo (/openapi/v1.json, /scalar).
app.MapOpenApi();
app.MapScalarApiReference();

app.MapHealth();

await app.RunAsync();
return 0;
