// Local site the crawler runs against in tests and demos: robots.txt, simple pages, failure endpoints for the
// fault-tolerance demos (scripts/), editable pages for change detection, and a 585-page benchmark tree.
// Every request is logged as "HIT <UTC time> <path> <user-agent>", so tools/check-rate.sh and the demo scripts can
// prove from `docker compose logs testsite` what the crawler actually requested and when.
// All state is in memory and resets when the container restarts.
using System.Collections.Concurrent;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var requestCounts = new ConcurrentDictionary<string, int>(); // per /flaky and /hang key
var healed = new ConcurrentDictionary<string, bool>();       // /fail keys that were healed
var editable = new ConcurrentDictionary<string, EditablePage>();

app.Use(async (context, next) =>
{
    var time = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    await Console.Out.WriteLineAsync($"HIT {time} {context.Request.Path}{context.Request.QueryString} {context.Request.Headers.UserAgent}");
    await next(context);
});

// /delay/{ms}/<any path>: waits, then serves <any path>. Relative links keep the prefix, so a whole tree stays slow.
app.Use(async (context, next) =>
{
    var segments = context.Request.Path.Value?.Split('/', 4, StringSplitOptions.None) ?? [];
    if (segments.Length >= 3 && segments[1] == "delay" && int.TryParse(segments[2], CultureInfo.InvariantCulture, out var milliseconds))
    {
        await Task.Delay(Math.Clamp(milliseconds, 0, 60_000), context.RequestAborted);
        context.Request.Path = "/" + (segments.Length == 4 ? segments[3] : string.Empty);
    }
    await next(context);
});

// Routing must run after the /delay rewrite (minimal APIs would otherwise route on the original path).
app.UseRouting();

const string HomePage = """
    <!doctype html>
    <html lang="en">
    <head><meta charset="utf-8"><title>Scraper Test Site</title></head>
    <body>
      <h1>Scraper Test Site</h1>
      <p>A local site for crawler tests.</p>
      <ul>
        <li><a href="/pages/1">Page 1</a></li>
        <li><a href="/tree/0">Benchmark tree (585 pages)</a></li>
        <li><a href="/private/secret">Private (disallowed by robots.txt)</a></li>
      </ul>
    </body>
    </html>
    """;

// Disallows /private/ for everyone, so robots.txt handling can be seen end to end.
const string RobotsTxt = """
    User-agent: *
    Disallow: /private/
    """;

app.MapGet("/", () => Html(HomePage));

app.MapGet("/robots.txt", () => Results.Text(RobotsTxt, "text/plain"));

app.MapGet("/pages/{n:int:min(1)}", (int n) => Page($"Page {n}", $"""<p>Test page number {n}.</p><a href="/pages/{n + 1}">Next page</a>"""));

app.MapGet("/private/{**rest}", () => Page("Private", "<p>robots.txt disallows this page.</p>"));

// Any status on demand; 429 and 503 also send Retry-After, to exercise the fetcher's backoff.
app.MapGet("/status/{code:int:range(200,599)}", (int code, HttpResponse response) =>
{
    if (code is 429 or 503)
    {
        response.Headers.RetryAfter = "2";
    }
    return Results.StatusCode(code);
});

// /tree/{path}?branch=8&depth=3: node "0" has children "0-0" … "0-7", and so on down to the given depth.
// Defaults give 1 + 8 + 64 + 512 = 585 pages. Links are relative and keep the query, so the shape carries through.
app.MapGet("/tree/{path}", (string path, int? branch, int? depth, HttpRequest request) =>
{
    var width = Math.Clamp(branch ?? 8, 1, 20);
    var maxDepth = Math.Clamp(depth ?? 3, 0, 6);
    var level = path.Count(c => c == '-');
    var children = level < maxDepth
        ? string.Join("\n", Enumerable.Range(0, width).Select(i => $"""<li><a href="{path}-{i}{request.QueryString}">Node {path}-{i}</a></li>"""))
        : "<li>(leaf)</li>";
    return Page($"Node {path}", $"<p>Tree node {path} at level {level}.</p><ul>{children}</ul>");
});

// /flaky/{key}?status=429|503&failures=N&retryAfter=S: the first N requests for a key fail, then the page loads.
app.MapGet("/flaky/{key}", (string key, int? status, int? failures, int? retryAfter, HttpResponse response) =>
{
    var count = requestCounts.AddOrUpdate("flaky:" + key, 1, (_, n) => n + 1);
    if (count <= (failures ?? 2))
    {
        var code = status ?? 429;
        if (code is 429 or 503)
        {
            response.Headers.RetryAfter = (retryAfter ?? 2).ToString(CultureInfo.InvariantCulture);
        }
        return Results.StatusCode(code);
    }
    return Page($"Flaky {key}", $"<p>Loaded after {count - 1} failed request(s).</p>");
});

// /hang/{key}?hangs=N: the first N requests for a key never answer (the crawler's timeout must cut them off).
app.MapGet("/hang/{key}", async (string key, int? hangs, CancellationToken aborted) =>
{
    var count = requestCounts.AddOrUpdate("hang:" + key, 1, (_, n) => n + 1);
    if (count <= (hangs ?? 2))
    {
        await Task.Delay(Timeout.Infinite, aborted);
    }
    return Page($"Hang {key}", $"<p>Answered on request {count}.</p>");
});

// /fail/{key}: always 500 until POST /fail/{key}/heal (to show a dead-lettered task being retried successfully).
app.MapGet("/fail/{key}", (string key) =>
    healed.ContainsKey(key) ? Page($"Healed {key}", "<p>This page works again.</p>") : Results.StatusCode(500));
app.MapPost("/fail/{key}/heal", (string key) =>
{
    healed[key] = true;
    return Results.NoContent();
});

// /editable/{id}: a page whose content can be changed with POST (body = new text). Sends ETag / Last-Modified and
// answers 304 to a matching If-None-Match, for incremental crawls and content versioning.
app.MapGet("/editable/{id}", (string id, HttpRequest request, HttpResponse response) =>
{
    var page = editable.GetOrAdd(id, _ => new EditablePage("Original content.", 1, DateTimeOffset.UtcNow));
    var etag = $"\"v{page.Version}\"";
    response.Headers.ETag = etag;
    response.Headers.LastModified = page.ModifiedAt.ToString("R", CultureInfo.InvariantCulture);
    return request.Headers.IfNoneMatch.ToString() == etag
        ? Results.StatusCode(304)
        : Page($"Editable {id}", $"<p>{System.Net.WebUtility.HtmlEncode(page.Text)}</p><p>Version {page.Version}.</p>");
});
app.MapPost("/editable/{id}", async (string id, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var text = await reader.ReadToEndAsync();
    var page = editable.AddOrUpdate(id,
        _ => new EditablePage(text, 1, DateTimeOffset.UtcNow),
        (_, old) => new EditablePage(text, old.Version + 1, DateTimeOffset.UtcNow));
    return Results.Ok(new { page.Version });
});

await app.RunAsync();

static IResult Html(string html) => Results.Content(html, "text/html");

static IResult Page(string title, string body) => Results.Content($"""
    <!doctype html>
    <html lang="en">
    <head><meta charset="utf-8"><title>{title}</title></head>
    <body>
      <h1>{title}</h1>
      {body}
    </body>
    </html>
    """, "text/html");

internal sealed record EditablePage(string Text, int Version, DateTimeOffset ModifiedAt);
