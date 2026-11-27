using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Scraper.Application.Crawling;
using Scraper.Infrastructure.Persistence;
using Scraper.Infrastructure.Persistence.Stores;
using StackExchange.Redis;

namespace Scraper.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers PostgreSQL (EF Core), the Redis multiplexer and the RabbitMQ connection factory.
    /// Every value comes from configuration, which the hosts fill from environment variables.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings__Postgres is not set.");
        services.AddDbContext<ScraperDbContext>(options => options.UseNpgsql(postgres));
        services.AddScoped<ICrawlStore, PostgresCrawlStore>();

        var redisOptions = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings__Redis is not set."));
        // Keep retrying in the background instead of crashing at startup if Redis isn't up yet.
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
        {
            HostName = Required(configuration, "RabbitMQ:Host"),
            UserName = Required(configuration, "RabbitMQ:Username"),
            Password = Required(configuration, "RabbitMQ:Password"),
        });

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException($"{key.Replace(":", "__", StringComparison.Ordinal)} is not set.");
}
