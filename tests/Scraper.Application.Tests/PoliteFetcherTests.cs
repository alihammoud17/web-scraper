using Scraper.Application.Crawling;
using Scraper.Domain.Sites;
using Scraper.Domain.Urls;

namespace Scraper.Application.Tests;

public class PoliteFetcherTests
{
    private static readonly SiteConfig Newswire = new(
        "newswire", "Rockstar Newswire", NormalizedUrl.Create("https://www.rockstargames.com/newswire"),
        [NormalizedUrl.Create("https://www.rockstargames.com/newswire")], ["/newswire"],
        FetchMode.Browser, new RateLimit(0.2m), 2, 200);

    private readonly FakeSites _sites = new(Newswire);
    private readonly FakeRobots _robots = new();
    private readonly FakeFetcher _fetcher = new();

    [Fact]
    public async Task Unknown_site_is_blocked_without_a_request()
    {
        var outcome = await Fetch("https://unknown.example/page");

        Assert.Equal(PoliteFetchKind.Blocked, outcome.Kind);
        Assert.Equal(BlockReason.UnknownSite, outcome.BlockReason);
        Assert.Empty(_fetcher.Requests);
    }

    [Fact]
    public async Task Out_of_scope_url_is_blocked_without_a_request()
    {
        var outcome = await Fetch("https://www.rockstargames.com/gta-v");

        Assert.Equal(BlockReason.OutOfScope, outcome.BlockReason);
        Assert.Empty(_fetcher.Requests);
        Assert.Empty(_robots.Checked);
    }

    [Fact]
    public async Task Robots_disallowed_url_is_blocked_without_a_request()
    {
        _robots.Allowed = false;

        var outcome = await Fetch("https://www.rockstargames.com/newswire/article/1");

        Assert.Equal(BlockReason.RobotsDisallowed, outcome.BlockReason);
        Assert.Single(_robots.Checked);
        Assert.Empty(_fetcher.Requests);
    }

    [Fact]
    public async Task Allowed_url_is_fetched_with_its_validators()
    {
        var lastModified = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var request = new FetchRequest(NormalizedUrl.Create("https://www.rockstargames.com/newswire"), "\"v1\"", lastModified);

        var outcome = await new PoliteFetcher(_sites, _robots, _fetcher).FetchAsync(request, CancellationToken.None);

        Assert.Equal(PoliteFetchKind.Fetched, outcome.Kind);
        var sent = Assert.Single(_fetcher.Requests);
        Assert.Equal("\"v1\"", sent.ETag);
        Assert.Equal(lastModified, sent.LastModified);
    }

    [Fact]
    public async Task Not_modified_response_is_reported()
    {
        _fetcher.Next = Result(304);

        Assert.Equal(PoliteFetchKind.NotModified, (await Fetch("https://www.rockstargames.com/newswire")).Kind);
    }

    [Fact]
    public async Task Redirect_is_reported_with_a_normalized_target_but_not_followed()
    {
        _fetcher.Next = Result(301, location: "/newswire/article/2?utm_source=x");

        var outcome = await Fetch("https://www.rockstargames.com/newswire/article/1");

        Assert.Equal(PoliteFetchKind.Redirected, outcome.Kind);
        Assert.Equal("https://www.rockstargames.com/newswire/article/2", outcome.RedirectTarget?.Value);
        Assert.Single(_fetcher.Requests);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(429)]
    public async Task Error_statuses_are_failures(int status)
    {
        _fetcher.Next = Result(status);

        var outcome = await Fetch("https://www.rockstargames.com/newswire");

        Assert.Equal(PoliteFetchKind.Failed, outcome.Kind);
        Assert.Equal(status, outcome.Result?.StatusCode);
    }

    private Task<PoliteFetchOutcome> Fetch(string url) =>
        new PoliteFetcher(_sites, _robots, _fetcher).FetchAsync(new FetchRequest(NormalizedUrl.Create(url)), CancellationToken.None);

    private static FetchResult Result(int status, string? location = null) =>
        new(status, new Dictionary<string, string>(), status == 200 ? "<html></html>" : null, null, null, location, null, TimeSpan.Zero);

    private sealed class FakeSites(params SiteConfig[] sites) : ISiteCatalog
    {
        public Task<SiteConfig?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken) =>
            Task.FromResult(sites.FirstOrDefault(s => s.BaseUrl.Authority == authority));

        public Task<SiteConfig?> FindByIdAsync(string siteId, CancellationToken cancellationToken) =>
            Task.FromResult(sites.FirstOrDefault(s => s.Id == siteId));
    }

    private sealed class FakeRobots : IRobotsPolicy
    {
        public bool Allowed { get; set; } = true;

        public List<NormalizedUrl> Checked { get; } = [];

        public Task<bool> IsAllowedAsync(NormalizedUrl url, CancellationToken cancellationToken)
        {
            Checked.Add(url);
            return Task.FromResult(Allowed);
        }
    }

    private sealed class FakeFetcher : IFetcher
    {
        public FetchResult Next { get; set; } = Result(200);

        public List<FetchRequest> Requests { get; } = [];

        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Next);
        }
    }
}
