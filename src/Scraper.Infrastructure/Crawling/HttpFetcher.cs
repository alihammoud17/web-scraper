using System.Diagnostics;
using System.Net;
using Scraper.Application.Crawling;

namespace Scraper.Infrastructure.Crawling;

/// <summary>
/// <see cref="IFetcher"/> over the named "crawler" <see cref="HttpClient"/>, whose handler chain
/// (see <see cref="CrawlerServiceCollectionExtensions"/>) adds retries, timeouts and the shared rate limit.
/// Redirects are returned, not followed, so their targets go through scope, robots and rate limiting.
/// </summary>
public sealed class HttpFetcher(IHttpClientFactory clients, CrawlerHttpOptions options, TimeProvider time) : IFetcher
{
    public const string ClientName = "crawler";

    public async Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = clients.CreateClient(ClientName);
        var uri = new Uri(request.Url.Value);

        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        if (request.ETag is not null)
        {
            message.Headers.TryAddWithoutValidation("If-None-Match", request.ETag);
        }
        message.Headers.IfModifiedSince = request.LastModified;

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await ReadBodyAsync(response, uri, cancellationToken);
        stopwatch.Stop();

        return new FetchResult(
            (int)response.StatusCode,
            Headers(response),
            body,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified,
            response.Headers.Location?.ToString(),
            RetryAfterOf(response, time.GetUtcNow()),
            stopwatch.Elapsed);
    }

    /// <summary>The primary handler: no automatic redirects, compressed responses accepted.</summary>
    public static SocketsHttpHandler CreatePrimaryHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>How long the server asked us to wait (Retry-After as seconds or as a date), if at all.</summary>
    public static TimeSpan? RetryAfterOf(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }
        return retryAfter?.Date is { } date ? (date > now ? date - now : TimeSpan.Zero) : null;
    }

    private async Task<string?> ReadBodyAsync(HttpResponseMessage response, Uri uri, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return null;
        }
        if (response.Content.Headers.ContentLength > options.MaxResponseBytes)
        {
            throw new ResponseTooLargeException(uri, options.MaxResponseBytes);
        }
        try
        {
            await response.Content.LoadIntoBufferAsync(options.MaxResponseBytes, cancellationToken);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            // Thrown when the body (without a Content-Length) grows past the buffer limit.
            throw new ResponseTooLargeException(uri, options.MaxResponseBytes);
        }
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static Dictionary<string, string> Headers(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .GroupBy(h => h.Key.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(h => h.Value)));
}
