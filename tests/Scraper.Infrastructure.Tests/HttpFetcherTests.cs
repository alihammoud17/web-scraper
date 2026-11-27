using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Scraper.Application.Crawling;
using Scraper.Domain.Sites;
using Scraper.Domain.Urls;
using Scraper.Infrastructure.Crawling;

namespace Scraper.Infrastructure.Tests;

/// <summary>
/// The real "crawler" HttpClient pipeline (retry, rate-limit handler, attempt timeout) with a fake
/// network at the bottom and a recording rate limiter. Delays are milliseconds instead of seconds.
/// </summary>
public sealed class HttpFetcherTests : IDisposable
{
    private const string UserAgent = "DistributedScraperBot/1.0 (+mailto:test@example.com)";

    private static readonly CrawlerHttpOptions FastOptions = new()
    {
        BaseDelay = TimeSpan.FromMilliseconds(1),
        UseJitter = false,
        AttemptTimeout = TimeSpan.FromMilliseconds(300),
        TotalTimeout = TimeSpan.FromSeconds(30),
        MaxResponseBytes = 1_000,
    };

    private readonly FakeNetwork _network = new();
    private readonly RecordingRateLimiter _limiter = new();
    private readonly ServiceProvider _services;

    public HttpFetcherTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CrawlerOptions { UserAgent = UserAgent });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISiteCatalog>(new OneSite());
        services.AddSingleton<IRateLimiter>(_limiter);
        services.AddCrawlerHttp(FastOptions).ConfigurePrimaryHttpMessageHandler(() => _network);
        _services = services.BuildServiceProvider();
    }

    private IFetcher Fetcher => _services.GetRequiredService<IFetcher>();

    [Fact]
    public async Task Sends_the_user_agent_and_conditional_headers()
    {
        var lastModified = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        _network.Then(_ => Respond(HttpStatusCode.NotModified));

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page"), "\"abc\"", lastModified), CancellationToken.None);

        var call = Assert.Single(_network.Calls);
        Assert.Equal(UserAgent, call.UserAgent);
        Assert.Equal("\"abc\"", call.IfNoneMatch);
        Assert.Equal(lastModified, call.IfModifiedSince);
        Assert.True(result.IsNotModified);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task Returns_body_validators_and_lowercase_headers()
    {
        _network.Then(_ =>
        {
            var response = Respond(HttpStatusCode.OK, "<html>hi</html>");
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
            return response;
        });

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("<html>hi</html>", result.Body);
        Assert.Equal("\"v2\"", result.ETag);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), result.LastModified);
        Assert.Equal("\"v2\"", result.Headers["etag"]);
    }

    [Fact]
    public async Task Service_unavailable_is_retried_and_every_attempt_takes_a_token()
    {
        _network.Then(_ => Respond(HttpStatusCode.ServiceUnavailable))
            .Then(_ => Respond(HttpStatusCode.ServiceUnavailable))
            .Then(_ => Respond(HttpStatusCode.ServiceUnavailable))
            .Then(_ => Respond(HttpStatusCode.OK, "ok"));

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(4, _network.Calls.Count);
        Assert.Equal(4, _limiter.Tokens.Count);
        Assert.All(_limiter.Tokens, authority => Assert.Equal("example.com", authority));
    }

    [Fact]
    public async Task Gives_up_after_the_last_retry_and_returns_the_response()
    {
        _network.Then(_ => Respond(HttpStatusCode.ServiceUnavailable));

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal(503, result.StatusCode);
        Assert.Equal(1 + FastOptions.MaxRetryAttempts, _network.Calls.Count);
    }

    [Fact]
    public async Task Connection_errors_are_retried()
    {
        _network.Then(_ => throw new HttpRequestException("connection reset"))
            .Then(_ => Respond(HttpStatusCode.OK, "ok"));

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, _network.Calls.Count);
    }

    [Fact]
    public async Task A_timed_out_attempt_is_retried()
    {
        _network.ThenAsync(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken); // hangs until the attempt timeout fires
                return Respond(HttpStatusCode.OK);
            })
            .Then(_ => Respond(HttpStatusCode.OK, "ok"));

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, _network.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Other_errors_are_not_retried(HttpStatusCode status)
    {
        _network.Then(_ => Respond(status));

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal((int)status, result.StatusCode);
        Assert.Single(_network.Calls);
    }

    [Fact]
    public async Task Retry_after_is_honored()
    {
        _network.Then(_ =>
            {
                var response = Respond(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return response;
            })
            .Then(_ => Respond(HttpStatusCode.OK, "ok"));

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        var gap = _network.Calls[1].At - _network.Calls[0].At;
        Assert.True(gap >= TimeSpan.FromMilliseconds(950), $"Retried after {gap.TotalMilliseconds:F0} ms instead of the 1 s Retry-After.");
    }

    [Fact]
    public async Task A_retry_after_beyond_the_cap_is_returned_instead_of_waited_out()
    {
        _network.Then(_ =>
        {
            var response = Respond(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        });

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.Equal(429, result.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(120), result.RetryAfter);
        Assert.Single(_network.Calls);
    }

    [Fact]
    public async Task Redirects_are_returned_not_followed()
    {
        _network.Then(_ =>
        {
            var response = Respond(HttpStatusCode.Found);
            response.Headers.Location = new Uri("/elsewhere", UriKind.Relative);
            return response;
        });

        var result = await Fetcher.FetchAsync(new FetchRequest(Url("/page")), CancellationToken.None);

        Assert.True(result.IsRedirect);
        Assert.Equal("/elsewhere", result.RedirectLocation);
        Assert.Single(_network.Calls);
    }

    [Fact]
    public void The_real_network_handler_never_follows_redirects()
    {
        using var handler = HttpFetcher.CreatePrimaryHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
    }

    [Fact]
    public async Task Hosts_that_are_not_configured_sites_are_refused()
    {
        _network.Then(_ => Respond(HttpStatusCode.OK));

        await Assert.ThrowsAsync<UnknownSiteException>(() =>
            Fetcher.FetchAsync(new FetchRequest(NormalizedUrl.Create("https://unknown.example/")), CancellationToken.None));

        Assert.Empty(_network.Calls);
        Assert.Empty(_limiter.Tokens);
    }

    [Fact]
    public async Task Responses_over_the_size_limit_are_rejected()
    {
        _network.Then(_ => Respond(HttpStatusCode.OK, new string('x', 5_000)));

        await Assert.ThrowsAsync<ResponseTooLargeException>(() =>
            Fetcher.FetchAsync(new FetchRequest(Url("/big")), CancellationToken.None));
    }

    public void Dispose() => _services.Dispose();

    private static NormalizedUrl Url(string path) => NormalizedUrl.Create("https://example.com" + path);

    private static HttpResponseMessage Respond(HttpStatusCode status, string? body = null) =>
        new(status) { Content = new StringContent(body ?? string.Empty) };

    private sealed class OneSite : ISiteCatalog
    {
        private readonly SiteConfig _site = new(
            "example", "Example", NormalizedUrl.Create("https://example.com/"), [NormalizedUrl.Create("https://example.com/")],
            [], FetchMode.Http, new RateLimit(5m), 2, 100);

        public Task<SiteConfig?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken) =>
            Task.FromResult(authority == "example.com" ? _site : null);

        public Task<SiteConfig?> FindByIdAsync(string siteId, CancellationToken cancellationToken) =>
            Task.FromResult(siteId == "example" ? _site : null);
    }

    private sealed class RecordingRateLimiter : IRateLimiter
    {
        public List<string> Tokens { get; } = [];

        public Task AcquireAsync(string authority, RateLimit limit, CancellationToken cancellationToken)
        {
            lock (Tokens)
            {
                Tokens.Add(authority);
            }
            return Task.CompletedTask;
        }
    }

    private sealed record Call(TimeSpan At, string UserAgent, string? IfNoneMatch, DateTimeOffset? IfModifiedSince);

    /// <summary>Plays scripted responses in order; the last one repeats.</summary>
    private sealed class FakeNetwork : HttpMessageHandler
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = [];

        public List<Call> Calls { get; } = [];

        public FakeNetwork Then(Func<HttpRequestMessage, HttpResponseMessage> step) =>
            ThenAsync((request, _) => Task.FromResult(step(request)));

        public FakeNetwork ThenAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step)
        {
            _steps.Add(step);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int index;
            lock (Calls)
            {
                Calls.Add(new Call(
                    _clock.Elapsed,
                    request.Headers.UserAgent.ToString(),
                    request.Headers.TryGetValues("If-None-Match", out var etags) ? etags.Single() : null,
                    request.Headers.IfModifiedSince));
                index = Math.Min(Calls.Count, _steps.Count) - 1;
            }
            return _steps[index](request, cancellationToken);
        }
    }
}
