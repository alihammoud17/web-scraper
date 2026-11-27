using Scraper.Application.Crawling;
using Scraper.Domain.Crawling;
using Scraper.Domain.Sites;
using Scraper.Domain.Urls;

namespace Scraper.Application.Tests;

/// <summary>In-memory fakes for the crawl ports. They append to one shared event log, so tests can assert order.</summary>
internal sealed class CrawlFakes
{
    public static readonly SiteConfig TestSite = new(
        "testsite", "Test", NormalizedUrl.Create("http://testsite:8080/"), [NormalizedUrl.Create("http://testsite:8080/")],
        [], FetchMode.Http, new RateLimit(5m), maxDepth: 2, maxPages: 100);

    public static readonly SiteConfig BrowserSite = new(
        "newswire", "Newswire", NormalizedUrl.Create("https://www.rockstargames.com/newswire"), [NormalizedUrl.Create("https://www.rockstargames.com/newswire")],
        ["/newswire"], FetchMode.Browser, new RateLimit(0.2m), 2, 200);

    public List<string> Events { get; } = [];

    public FakeStore Store { get; }

    public FakeQueue Queue { get; }

    public FakeSeen Seen { get; }

    public FakeBudget Budget { get; }

    public FakeSites Sites { get; } = new(TestSite, BrowserSite);

    public FakeRobots Robots { get; } = new();

    public FakeFetcher Fetcher { get; }

    public FakeExtractor Extractor { get; } = new();

    public CrawlFakes()
    {
        Store = new FakeStore(Events);
        Queue = new FakeQueue(Events);
        Seen = new FakeSeen(Events);
        Budget = new FakeBudget();
        Fetcher = new FakeFetcher(Events);
    }

    public ProcessCrawlTask Processor(RetryOptions? options = null) => new(
        Store, Sites, new PoliteFetcher(Sites, Robots, Fetcher), Extractor, Seen, Budget, Queue,
        new RetryPolicy(options ?? RetryOptions.Default), new FakeClassifier(), TimeProvider.System);

    public static CrawlTaskMessage Message(string url = "http://testsite:8080/pages/1", int depth = 0, int attempt = 1) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "testsite", url, depth, attempt);

    public static FetchResult Html(string body = "<html></html>", int status = 200, string? location = null, TimeSpan? retryAfter = null) =>
        new(status, new Dictionary<string, string> { ["content-type"] = "text/html" }, body, "\"etag\"", null, location, retryAfter, TimeSpan.Zero);

    internal sealed class FakeStore(List<string> events) : ICrawlStore
    {
        public bool StartSucceeds { get; set; } = true;

        public Exception? CompleteThrows { get; set; }

        public TaskContext Context { get; set; } = new(CrawlJobStatus.Running, CrawlMode.Full, null, null);

        public List<CrawlTaskMessage> AlreadyQueuedChildren { get; } = [];

        public TaskCompletion? Completed { get; private set; }

        public List<string> Retries { get; } = [];

        public (CrawlJob Job, IReadOnlyList<CrawlTask> Seeds)? Created { get; private set; }

        public List<Guid> FailedJobs { get; } = [];

        public RetryDeadLetterResult ResetResult { get; set; } = new RetryDeadLetterResult.NotFound();

        public List<DeadLetterNotice> DeadLetters { get; } = [];

        public Task CreateJobAsync(CrawlJob job, IReadOnlyList<CrawlTask> seeds, CancellationToken cancellationToken)
        {
            events.Add("create-job");
            Created = (job, seeds);
            return Task.CompletedTask;
        }

        public Task MarkJobFailedAsync(Guid jobId, CancellationToken cancellationToken)
        {
            FailedJobs.Add(jobId);
            return Task.CompletedTask;
        }

        public Task<bool> TryStartTaskAsync(Guid taskId, CancellationToken cancellationToken)
        {
            events.Add("start");
            return Task.FromResult(StartSucceeds);
        }

        public Task<TaskContext> GetTaskContextAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult(Context);

        public Task<IReadOnlyList<CrawlTaskMessage>> CompleteTaskAsync(TaskCompletion completion, CancellationToken cancellationToken)
        {
            if (CompleteThrows is not null)
            {
                throw CompleteThrows;
            }
            events.Add("commit");
            Completed = completion;
            IReadOnlyList<CrawlTaskMessage> queued =
                [.. completion.Children.Select(c => new CrawlTaskMessage(Guid.CreateVersion7(), completion.Message.JobId, completion.Message.SiteId, c.Url.Value, c.Depth, 1))];
            return Task.FromResult(queued);
        }

        public Task<IReadOnlyList<CrawlTaskMessage>> GetQueuedChildrenAsync(Guid parentTaskId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CrawlTaskMessage>>(AlreadyQueuedChildren);

        public Task RecordRetryAsync(Guid taskId, string error, CancellationToken cancellationToken)
        {
            events.Add("record-retry");
            Retries.Add(error);
            return Task.CompletedTask;
        }

        public Task RecordDeadLetterAsync(DeadLetterNotice notice, DateTimeOffset at, CancellationToken cancellationToken)
        {
            events.Add("record-dead-letter");
            DeadLetters.Add(notice);
            return Task.CompletedTask;
        }

        public Task<RetryDeadLetterResult> ResetDeadLetterAsync(Guid taskId, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(ResetResult);

        public Task<bool> TryCompleteJobAsync(Guid jobId, CancellationToken cancellationToken)
        {
            events.Add("complete-job");
            return Task.FromResult(true);
        }
    }

    internal sealed class FakeQueue(List<string> events) : ITaskQueue
    {
        public bool Fails { get; set; }

        public List<CrawlTaskMessage> Published { get; } = [];

        public Task PublishAsync(IReadOnlyList<CrawlTaskMessage> messages, CancellationToken cancellationToken)
        {
            if (Fails)
            {
                throw new InvalidOperationException("broker unreachable");
            }
            events.Add($"publish:{messages.Count}");
            Published.AddRange(messages);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeSeen(List<string> events) : ISeenUrlStore
    {
        public HashSet<string> Seen { get; } = [];

        public Task<bool> TryMarkSeenAsync(Guid crawlJobId, NormalizedUrl url, CancellationToken cancellationToken) => Task.FromResult(Seen.Add(url.Value));

        public Task<IReadOnlyList<NormalizedUrl>> FilterUnseenAsync(Guid crawlJobId, IReadOnlyList<NormalizedUrl> urls, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NormalizedUrl>>([.. urls.Where(u => !Seen.Contains(u.Value))]);

        public Task MarkSeenAsync(Guid crawlJobId, IReadOnlyList<NormalizedUrl> urls, CancellationToken cancellationToken)
        {
            events.Add("mark-seen");
            Seen.UnionWith(urls.Select(u => u.Value));
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeBudget : ICrawlBudget
    {
        public int Remaining { get; set; } = int.MaxValue;

        public Task<int> TryReserveAsync(Guid crawlJobId, int maxPages, int count, CancellationToken cancellationToken)
        {
            var allowed = Math.Min(count, Remaining);
            Remaining -= allowed;
            return Task.FromResult(allowed);
        }
    }

    internal sealed class FakeSites(params SiteConfig[] sites) : ISiteCatalog
    {
        public Task<SiteConfig?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken) =>
            Task.FromResult(sites.FirstOrDefault(s => s.BaseUrl.Authority == authority));

        public Task<SiteConfig?> FindByIdAsync(string siteId, CancellationToken cancellationToken) =>
            Task.FromResult(sites.FirstOrDefault(s => s.Id == siteId));
    }

    internal sealed class FakeRobots : IRobotsPolicy
    {
        public bool Allowed { get; set; } = true;

        public Task<bool> IsAllowedAsync(NormalizedUrl url, CancellationToken cancellationToken) => Task.FromResult(Allowed);
    }

    internal sealed class FakeFetcher(List<string> events) : IFetcher
    {
        public FetchResult Next { get; set; } = Html();

        public Exception? Throws { get; set; }

        public List<FetchRequest> Requests { get; } = [];

        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
        {
            events.Add("fetch");
            Requests.Add(request);
            return Throws is null ? Task.FromResult(Next) : Task.FromException<FetchResult>(Throws);
        }
    }

    internal sealed class FakeExtractor : ILinkExtractor
    {
        public List<string> Links { get; } = [];

        public IReadOnlyList<DiscoveredLink> Extract(string html, NormalizedUrl pageUrl) =>
            [.. Links.Select(l => new DiscoveredLink(NormalizedUrl.Create(l), "text"))];
    }

    /// <summary>Timeouts and HTTP errors are transient, InvalidDataException is permanent, everything else unknown.</summary>
    internal sealed class FakeClassifier : IFailureClassifier
    {
        public FailureKind Classify(Exception exception) => exception switch
        {
            TimeoutException or HttpRequestException => FailureKind.Transient,
            InvalidDataException => FailureKind.Permanent,
            _ => FailureKind.Unknown,
        };
    }
}
