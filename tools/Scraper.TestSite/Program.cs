// Local site the crawler runs against in tests and demos (robots.txt and pages arrive in P4).
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string HomePage = """
    <!doctype html>
    <html lang="en">
    <head><meta charset="utf-8"><title>Scraper Test Site</title></head>
    <body>
      <h1>Scraper Test Site</h1>
      <p>A local site for crawler tests.</p>
    </body>
    </html>
    """;

app.MapGet("/", () => Results.Content(HomePage, "text/html"));

await app.RunAsync();
