using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;
using Scraper.Application.Crawling;
using Scraper.Infrastructure.Messaging;

namespace Scraper.Infrastructure.Crawling;

public static class CrawlerServiceCollectionExtensions
{
    /// <summary>
    /// The work queue and the crawl use cases that only enqueue or record (API and worker): RabbitMQ topology,
    /// confirmed publisher and queue length, the retry policy (<c>Queue__MaxAttempts</c>, <c>Queue__RetryDelays</c>),
    /// site catalog, seen set, page budget, <see cref="StartCrawl"/>, <see cref="RetryDeadLetter"/> and
    /// <see cref="RecordDeadLetter"/>. Needs <c>AddInfrastructure</c> (database, Redis, RabbitMQ connection factory).
    /// </summary>
    public static IServiceCollection AddCrawlQueue(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISiteCatalog, SiteCatalog>();
        services.TryAddSingleton<ISeenUrlStore, RedisSeenUrlStore>();
        services.TryAddSingleton<ICrawlBudget, RedisCrawlBudget>();
        services.TryAddSingleton<IFailureClassifier, FailureClassifier>();
        services.TryAddSingleton(RetryOptions.From(configuration["Queue:MaxAttempts"], configuration["Queue:RetryDelays"]));
        services.TryAddSingleton<RetryPolicy>();

        services.TryAddSingleton(new RabbitMqTopology());
        services.TryAddSingleton<RabbitMqConnection>();
        services.TryAddSingleton<RabbitMqTaskQueue>();
        services.TryAddSingleton<ITaskQueue>(provider => provider.GetRequiredService<RabbitMqTaskQueue>());
        services.TryAddSingleton<RabbitMqQueueStats>();

        services.TryAddScoped<StartCrawl>();
        services.TryAddScoped<RetryDeadLetter>();
        services.TryAddScoped<RecordDeadLetter>();
        return services;
    }

    /// <summary>
    /// Everything a worker needs to process tasks: the queue (<see cref="AddCrawlQueue"/>), the polite fetch
    /// pipeline (rate limiter, robots policy, resilient HTTP client, <see cref="PoliteFetcher"/>), the link
    /// extractor and <see cref="ProcessCrawlTask"/>. The per-attempt timeout comes from
    /// <c>Crawler__AttemptTimeoutSeconds</c> (default 30).
    /// </summary>
    public static IServiceCollection AddCrawler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCrawlQueue(configuration);
        services.AddSingleton(CrawlerOptions.FromConfiguration(configuration));
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<IRobotsPolicy, RedisRobotsPolicy>();
        services.AddSingleton<ILinkExtractor, AngleSharpLinkExtractor>();
        services.AddSingleton<PoliteFetcher>();
        services.AddScoped<ProcessCrawlTask>();

        var attemptTimeout = int.TryParse(configuration["Crawler:AttemptTimeoutSeconds"], out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : new CrawlerHttpOptions().AttemptTimeout;
        services.AddCrawlerHttp(new CrawlerHttpOptions { AttemptTimeout = attemptTimeout });
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
