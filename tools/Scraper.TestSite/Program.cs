// Local site the crawler runs against in tests and demos (robots.txt and pages arrive in P4).
// Every request is logged as "HIT <UTC time> <path> <user-agent>", so tools/check-rate.sh can prove
// from `docker compose logs testsite` that all workers together respect the site's rate limit.
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.Use(async (context, next) =>
{
    var time = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    await Console.Out.WriteLineAsync($"HIT {time} {context.Request.Path}{context.Request.QueryString} {context.Request.Headers.UserAgent}");
    await next(context);
});

const string HomePage = """
    <!doctype html>
    <html lang="en">
    <head><meta charset="utf-8"><title>Scraper Test Site</title></head>
    <body>
      <h1>Scraper Test Site</h1>
      <p>A local site for crawler tests.</p>
      <ul>
        <li><a href="/pages/1">Page 1</a></li>
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

app.MapGet("/", () => Results.Content(HomePage, "text/html"));

app.MapGet("/robots.txt", () => Results.Text(RobotsTxt, "text/plain"));

app.MapGet("/pages/{n:int:min(1)}", (int n) => Results.Content($"""
    <!doctype html>
    <html lang="en">
    <head><meta charset="utf-8"><title>Page {n}</title></head>
    <body>
      <h1>Page {n}</h1>
      <p>Test page number {n}.</p>
      <a href="/pages/{n + 1}">Next page</a>
    </body>
    </html>
    """, "text/html"));

app.MapGet("/private/{**rest}", () => Results.Content("<p>robots.txt disallows this page.</p>", "text/html"));

// Any status on demand; 429 and 503 also send Retry-After, to exercise the fetcher's backoff.
app.MapGet("/status/{code:int:range(200,599)}", (int code, HttpResponse response) =>
{
    if (code is 429 or 503)
    {
        response.Headers.RetryAfter = "2";
    }
    return Results.StatusCode(code);
});

await app.RunAsync();
