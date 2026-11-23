using Scraper.Infrastructure;
using Scraper.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<HeartbeatService>();

await builder.Build().RunAsync();
