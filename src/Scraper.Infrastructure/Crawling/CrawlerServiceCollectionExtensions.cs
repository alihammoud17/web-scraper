using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;
using Scraper.Application.Crawling;

namespace Scraper.Infrastructure.Crawling;

public static class CrawlerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the polite crawl pipeline (worker only): site catalog, Redis seen set, rate limiter and
    /// robots policy, the resilient HTTP fetcher and <see cref="PoliteFetcher"/>. Needs
    /// <c>AddInfrastructure</c> for the database and Redis connection.
    /// </summary>
    public static IServiceCollection AddCrawler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(CrawlerOptions.FromConfiguration(configuration));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISiteCatalog, SiteCatalog>();
        services.AddSingleton<ISeenUrlStore, RedisSeenUrlStore>();
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<IRobotsPolicy, RedisRobotsPolicy>();
        services.AddSingleton<PoliteFetcher>();
        services.AddCrawlerHttp(new CrawlerHttpOptions());
        return services;
    }

    /// <summary>
    /// The "crawler" HttpClient. Handler order, outermost first:
    /// <code>
    /// retry pipeline   total timeout → retry (429, 503, timeouts, connection errors; exponential + jitter; Retry-After)
    ///   RateLimitingHandler   every attempt takes a shared Redis token
    ///     attempt timeout     per attempt, after the token, so waiting for a token never counts as a timeout
    ///       SocketsHttpHandler  no automatic redirects, gzip/brotli
    /// </code>
    /// </summary>
    public static IHttpClientBuilder AddCrawlerHttp(this IServiceCollection services, CrawlerHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<IFetcher, HttpFetcher>();
        services.AddTransient<RateLimitingHandler>();

        var client = services
            .AddHttpClient(HttpFetcher.ClientName, (provider, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(provider.GetRequiredService<CrawlerOptions>().UserAgent);
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns all timeouts
            })
            .ConfigurePrimaryHttpMessageHandler(HttpFetcher.CreatePrimaryHandler);

        // Handlers wrap in the order they're added: the first one is the outermost.
        client.AddResilienceHandler("crawler-retry", builder =>
        {
            builder.AddTimeout(options.TotalTimeout);
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = options.UseJitter,
                Delay = options.BaseDelay,
                ShouldRetryAfterHeader = true,
                ShouldHandle = arguments => ValueTask.FromResult(ShouldRetry(arguments.Outcome, options)),
            });
        });
        client.AddHttpMessageHandler<RateLimitingHandler>();
        client.AddResilienceHandler("crawler-attempt", builder => builder.AddTimeout(options.AttemptTimeout));
        return client;
    }

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, CrawlerHttpOptions options) => outcome switch
    {
        { Exception: HttpRequestException or TimeoutRejectedException } => true,
        { Result: { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable } response } =>
            // A Retry-After beyond the cap is returned to the caller instead of blocking this worker.
            (HttpFetcher.RetryAfterOf(response, DateTimeOffset.UtcNow) ?? TimeSpan.Zero) <= options.MaxRetryAfter,
        _ => false,
    };
}
