using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scraper.Infrastructure.Persistence;

namespace Scraper.Infrastructure.Tests;

/// <summary>
/// A test that needs a real PostgreSQL (unique constraints and ON CONFLICT can't be faked). Runs when
/// <c>SCRAPER_TEST_POSTGRES</c> holds a connection string for a user allowed to create databases (CI sets it
/// for its service container); skipped otherwise.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgresDatabase.AdminConnectionString))
        {
            Skip = $"Set {PostgresDatabase.Variable} to run Postgres-backed tests.";
        }
    }
}

/// <summary>A throw-away database per test class: created, migrated (with the seeded sites), then dropped.</summary>
public sealed class PostgresDatabase : IAsyncLifetime
{
    public const string Variable = "SCRAPER_TEST_POSTGRES";

    private string? _name;

    public static string? AdminConnectionString => Environment.GetEnvironmentVariable(Variable);

    public string ConnectionString { get; private set; } = string.Empty;

    public ScraperDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ScraperDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(AdminConnectionString))
        {
            return;
        }
        _name = $"scraper_test_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE {_name}");
        ConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = _name }.ConnectionString;
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_name is null)
        {
            return;
        }
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
