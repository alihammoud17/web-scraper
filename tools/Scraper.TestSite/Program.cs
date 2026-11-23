// Local site the crawler tests will run against (pages are added in Milestone 3).
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
