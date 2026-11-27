using Microsoft.Extensions.Configuration;
using Scraper.Domain.Robots;

namespace Scraper.Infrastructure.Crawling;

/// <summary>Crawler settings from the environment (<c>Crawler__*</c>).</summary>
public sealed class CrawlerOptions
{
    /// <summary>Sent with every request; names the bot and gives site owners a contact (<c>Crawler__UserAgent</c>).</summary>
    public required string UserAgent { get; init; }

    /// <summary>The token robots.txt groups are matched against (the User-Agent before '/').</summary>
    public string ProductToken => RobotsTxt.ProductTokenOf(UserAgent);

    public static CrawlerOptions FromConfiguration(IConfiguration configuration)
    {
        var userAgent = configuration["Crawler:UserAgent"];
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            throw new InvalidOperationException("Crawler__UserAgent is not set.");
        }
        return new CrawlerOptions { UserAgent = userAgent };
    }
}
