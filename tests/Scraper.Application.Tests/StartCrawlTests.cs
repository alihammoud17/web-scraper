using Scraper.Application.Crawling;
using Scraper.Domain.Crawling;

namespace Scraper.Application.Tests;

public class StartCrawlTests
{
    private readonly CrawlFakes _fakes = new();

    private StartCrawl UseCase => new(_fakes.Store, _fakes.Sites, _fakes.Queue, _fakes.Seen, _fakes.Budget, TimeProvider.System);

    [Fact]
    public async Task Starts_a_running_job_and_publishes_its_seeds()
    {
        var result = await UseCase.ExecuteAsync(new StartCrawlCommand("testsite", Seeds: ["http://testsite:8080/tree/0", "http://testsite:8080/tree/0#dup"]), CancellationToken.None);

        var started = Assert.IsType<StartCrawlResult.Started>(result);
        Assert.Equal(1, started.SeedCount); // duplicates collapse after normalization
        Assert.Equal(CrawlJobStatus.Running, _fakes.Store.Created!.Value.Job.Status);
        var message = Assert.Single(_fakes.Queue.Published);
        Assert.Equal((1, 0), (message.Attempt, message.Depth));
        Assert.Contains("http://testsite:8080/tree/0", _fakes.Seen.Seen);
        Assert.Equal(["create-job", "mark-seen", "publish:1"], _fakes.Events);
    }

    [Fact]
    public async Task Uses_the_site_seeds_when_none_are_given()
    {
        var result = await UseCase.ExecuteAsync(new StartCrawlCommand("testsite"), CancellationToken.None);

        Assert.IsType<StartCrawlResult.Started>(result);
        Assert.Equal("http://testsite:8080/", Assert.Single(_fakes.Queue.Published).Url);
    }

    [Fact]
    public async Task Unknown_sites_are_rejected()
    {
        Assert.IsType<StartCrawlResult.SiteNotFound>(await UseCase.ExecuteAsync(new StartCrawlCommand("nope"), CancellationToken.None));
        Assert.Null(_fakes.Store.Created);
    }

    [Fact]
    public async Task Browser_sites_are_not_supported_yet()
    {
        Assert.IsType<StartCrawlResult.Unsupported>(await UseCase.ExecuteAsync(new StartCrawlCommand("newswire"), CancellationToken.None));
    }

    [Fact]
    public async Task Seeds_outside_the_site_are_rejected()
    {
        var result = await UseCase.ExecuteAsync(new StartCrawlCommand("testsite", Seeds: ["https://gtacars.net/", "not a url"]), CancellationToken.None);

        Assert.Equal(["https://gtacars.net/", "not a url"], Assert.IsType<StartCrawlResult.InvalidSeeds>(result).Seeds);
        Assert.Null(_fakes.Store.Created);
    }

    [Fact]
    public async Task A_publish_failure_marks_the_job_failed()
    {
        _fakes.Queue.Fails = true;

        var result = await UseCase.ExecuteAsync(new StartCrawlCommand("testsite"), CancellationToken.None);

        var unavailable = Assert.IsType<StartCrawlResult.QueueUnavailable>(result);
        Assert.Equal([unavailable.JobId], _fakes.Store.FailedJobs);
    }
}
