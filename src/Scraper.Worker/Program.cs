using Scraper.Infrastructure;
using Scraper.Infrastructure.Crawling;
using Scraper.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCrawler(builder.Configuration);
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.AddHostedService<FetchProbeService>();

await builder.Build().RunAsync();
