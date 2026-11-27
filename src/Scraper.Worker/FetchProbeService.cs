using Scraper.Application.Crawling;
using Scraper.Domain.Urls;

namespace Scraper.Worker;

/// <summary>
/// Demo and proof mode, off unless <c>Crawler__ProbeUrl</c> is set: fetches that one URL in a loop
/// through the full polite pipeline (site → scope → robots.txt → shared rate limit → HTTP). Running
/// several workers against TestSite shows that together they stay within the site's rate limit.
/// </summary>
public sealed class FetchProbeService(PoliteFetcher fetcher, IConfiguration configuration, ILogger<FetchProbeService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PauseAfterBlockOrError = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = configuration["Crawler:ProbeUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return; // probe off: the normal case
        }

        var url = NormalizedUrl.Create(configured);
        logger.LogInformation("Fetch probe started for {Url}", url);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await fetcher.FetchAsync(new FetchRequest(url), stoppingToken);
                logger.LogInformation(
                    "Probe {Kind} status={Status} reason={Reason} elapsed={ElapsedMs:F0} ms",
                    outcome.Kind, outcome.Result?.StatusCode, outcome.BlockReason, outcome.Result?.Elapsed.TotalMilliseconds);
                if (outcome.Kind == PoliteFetchKind.Blocked)
                {
                    await Task.Delay(PauseAfterBlockOrError, stoppingToken); // a block costs no request; don't spin
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The probe keeps running; every failure is logged and retried after a pause.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Probe fetch failed; retrying in {Pause}", PauseAfterBlockOrError);
                await Task.Delay(PauseAfterBlockOrError, stoppingToken);
            }
        }
    }
}
