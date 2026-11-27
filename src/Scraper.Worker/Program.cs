using Scraper.Infrastructure;
using Scraper.Infrastructure.Crawling;
using Scraper.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Below compose's stop_grace_period (30 s): time to finish and ack the in-flight task on SIGTERM.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCrawler(builder.Configuration);
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.AddHostedService<CrawlTaskConsumerService>();
builder.Services.AddHostedService<FetchProbeService>();

await builder.Build().RunAsync();
