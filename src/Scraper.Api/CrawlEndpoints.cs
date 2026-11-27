using Scraper.Application.Crawling;
using Scraper.Contracts;
using Scraper.Domain.Crawling;

namespace Scraper.Api;

/// <summary>Crawl control (no auth yet; JWT roles arrive in a later milestone).</summary>
public static class CrawlEndpoints
{
    public static IEndpointRouteBuilder MapCrawls(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crawls", StartCrawlAsync).WithName("StartCrawl");
        app.MapPost("/dead-letters/{taskId:guid}/retry", RetryDeadLetterAsync).WithName("RetryDeadLetter");
        return app;
    }

    private static async Task<IResult> StartCrawlAsync(StartCrawlRequest request, StartCrawl useCase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SiteId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["siteId"] = ["Required."] });
        }
        var mode = CrawlMode.Full;
        if (request.Mode is not null && !Enum.TryParse(request.Mode, ignoreCase: true, out mode))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = ["Use \"Full\" or \"Incremental\"."] });
        }

        var result = await useCase.ExecuteAsync(new StartCrawlCommand(request.SiteId, mode, request.Seeds), cancellationToken);
        return result switch
        {
            StartCrawlResult.Started started => Results.Accepted(uri: null, new StartCrawlResponse(started.JobId, started.SeedCount)),
            StartCrawlResult.SiteNotFound => Results.Problem($"No site '{request.SiteId}'.", statusCode: StatusCodes.Status404NotFound),
            StartCrawlResult.Unsupported unsupported => Results.Problem(unsupported.Reason, statusCode: StatusCodes.Status400BadRequest),
            StartCrawlResult.InvalidSeeds invalid => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["seeds"] = [.. invalid.Seeds.Select(s => $"'{s}' is not a URL inside site '{request.SiteId}'.")],
            }),
            StartCrawlResult.QueueUnavailable unavailable => Results.Problem(
                $"The job {unavailable.JobId} was stored but its seeds couldn't be queued; it is marked failed.", statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> RetryDeadLetterAsync(Guid taskId, RetryDeadLetter useCase, CancellationToken cancellationToken) =>
        await useCase.ExecuteAsync(taskId, cancellationToken) switch
        {
            RetryDeadLetterResult.Requeued => Results.Accepted(),
            RetryDeadLetterResult.NotFound => Results.Problem($"No task {taskId}.", statusCode: StatusCodes.Status404NotFound),
            RetryDeadLetterResult.NotDeadLettered notDead => Results.Problem(
                $"Task {taskId} is {notDead.Status}, not dead-lettered.", statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
}
