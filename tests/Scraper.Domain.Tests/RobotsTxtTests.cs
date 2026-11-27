using System.Text;
using Scraper.Domain.Robots;

namespace Scraper.Domain.Tests;

public class RobotsTxtTests
{
    private const string Us = "DistributedScraperBot";

    // ---- Captured fixtures (tests/Scraper.Domain.Tests/Fixtures/robots, captured 2026-11-27) ----

    [Theory]
    [InlineData("/")]
    [InlineData("/newswire")]
    [InlineData("/newswire/article/39akk424a9oo53/grand-theft-auto-vi-now-available")]
    public void Rockstar_allows_us_everywhere_because_it_has_no_star_group(string path)
    {
        Assert.True(Fixture("rockstar").IsAllowed(Us, path));
    }

    [Theory]
    [InlineData("ClaudeBot")]
    [InlineData("CCBot")]
    [InlineData("gptbot")]
    [InlineData("Applebot-Extended")] // the file has a trailing space after this name
    public void Rockstar_blocks_the_named_ai_bots(string bot)
    {
        Assert.False(Fixture("rockstar").IsAllowed(bot, "/newswire"));
    }

    [Theory]
    [InlineData("/api/vehicles", false)]
    [InlineData("/api/", false)]
    [InlineData("/gta5/adder", true)]
    [InlineData("/", true)]
    public void Gtacars_disallows_only_the_api_for_us(string path, bool allowed)
    {
        Assert.Equal(allowed, Fixture("gtacars").IsAllowed(Us, path));
    }

    [Theory]
    [InlineData("AhrefsBot")]
    [InlineData("SemrushBot")]
    [InlineData("Seekport Crawler")]
    public void Gtacars_blocks_every_agent_of_its_multi_agent_group(string bot)
    {
        Assert.False(Fixture("gtacars").IsAllowed(bot, "/gta5/adder"));
    }

    [Theory]
    [InlineData("/vehicles/adder/download/123", false)]
    [InlineData("/tools/some-tool/download/9", false)]
    [InlineData("/search/adder", false)]
    [InlineData("/search/", false)]
    [InlineData("/vehicles/adder", true)]
    [InlineData("/vehicles/2", true)]
    [InlineData("/search", true)]
    [InlineData("/vehicles/download", true)] // the pattern needs /vehicles/<something>/download/
    public void Gta5mods_blocks_download_and_search_patterns(string path, bool allowed)
    {
        Assert.Equal(allowed, Fixture("gta5mods").IsAllowed(Us, path));
    }

    // ---- RFC 9309 behaviour ----

    [Fact]
    public void Our_group_wins_over_the_star_group()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: *
            Disallow: /

            User-agent: DistributedScraperBot
            Disallow: /private
            """);

        Assert.True(robots.IsAllowed(Us, "/public"));
        Assert.False(robots.IsAllowed(Us, "/private/x"));
        Assert.False(robots.IsAllowed("OtherBot", "/public"));
    }

    [Fact]
    public void Agent_matching_is_case_insensitive_and_trimmed()
    {
        var robots = RobotsTxt.Parse("USER-AGENT:   distributedscraperbot   \nDISALLOW: /x");

        Assert.False(robots.IsAllowed("DistributedScraperBot", "/x"));
    }

    [Fact]
    public void No_matching_group_and_no_star_group_allows_everything()
    {
        var robots = RobotsTxt.Parse("User-agent: SomeOtherBot\nDisallow: /");

        Assert.True(robots.IsAllowed(Us, "/anything"));
    }

    [Fact]
    public void Groups_for_the_same_agent_are_merged()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: DistributedScraperBot
            Disallow: /a

            User-agent: Unrelated
            Disallow: /

            User-agent: distributedscraperbot
            Disallow: /b
            """);

        Assert.False(robots.IsAllowed(Us, "/a"));
        Assert.False(robots.IsAllowed(Us, "/b"));
        Assert.True(robots.IsAllowed(Us, "/c"));
    }

    [Fact]
    public void The_longest_match_wins()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /shop\nAllow: /shop/public");

        Assert.False(robots.IsAllowed(Us, "/shop/cart"));
        Assert.True(robots.IsAllowed(Us, "/shop/public/item"));
    }

    [Fact]
    public void Allow_wins_a_tie()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /page\nAllow: /page");

        Assert.True(robots.IsAllowed(Us, "/page"));
    }

    [Fact]
    public void Dollar_anchors_the_end()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*.pdf$");

        Assert.False(robots.IsAllowed(Us, "/files/guide.pdf"));
        Assert.True(robots.IsAllowed(Us, "/files/guide.pdf?download=1"));
        Assert.True(robots.IsAllowed(Us, "/files/guide.pdfx"));
    }

    [Fact]
    public void Query_strings_are_part_of_the_match()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*?sort=");

        Assert.False(robots.IsAllowed(Us, "/vehicles?sort=new"));
        Assert.True(robots.IsAllowed(Us, "/vehicles"));
    }

    [Fact]
    public void Paths_are_case_sensitive()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /Private");

        Assert.False(robots.IsAllowed(Us, "/Private"));
        Assert.True(robots.IsAllowed(Us, "/private"));
    }

    [Fact]
    public void Comments_crlf_bom_and_unknown_keys_are_handled()
    {
        var robots = RobotsTxt.Parse("﻿# header comment\r\nUser-agent: * # everyone\r\nCrawl-delay: 10\r\nSitemap: https://example.com/sitemap.xml\r\nDisallow: /secret # keep out\r\n");

        Assert.False(robots.IsAllowed(Us, "/secret"));
        Assert.True(robots.IsAllowed(Us, "/open"));
    }

    [Fact]
    public void An_empty_disallow_restricts_nothing()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow:");

        Assert.True(robots.IsAllowed(Us, "/anything"));
    }

    [Fact]
    public void Robots_txt_itself_is_always_allowed()
    {
        Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow: /").IsAllowed(Us, "/robots.txt"));
        Assert.True(RobotsTxt.DisallowAll.IsAllowed(Us, "/robots.txt"));
    }

    [Fact]
    public void Rules_before_any_user_agent_are_ignored()
    {
        var robots = RobotsTxt.Parse("Disallow: /\nUser-agent: *\nDisallow: /x");

        Assert.True(robots.IsAllowed(Us, "/y"));
    }

    [Theory]
    [InlineData("/caf%C3%A9", "/café")]     // non-ASCII in the path is compared as UTF-8 escapes
    [InlineData("/%7euser", "/~user")]       // escaped unreserved characters are decoded
    [InlineData("/a%2fb", "/a%2Fb")]         // other escapes compare with uppercase hex
    public void Percent_encoding_is_treated_as_equivalent(string pattern, string path)
    {
        var robots = RobotsTxt.Parse($"User-agent: *\nDisallow: {pattern}");

        Assert.False(robots.IsAllowed(Us, path));
    }

    [Fact]
    public void Content_after_500_KiB_is_ignored()
    {
        var padding = new StringBuilder("User-agent: *\n");
        while (padding.Length < RobotsTxt.MaxBytes)
        {
            padding.Append("# padding line to push the next rule past the limit\n");
        }
        var robots = RobotsTxt.Parse(padding + "Disallow: /late\n");

        Assert.True(robots.IsAllowed(Us, "/late"));
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(404, true)]
    [InlineData(403, true)]
    [InlineData(301, true)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    public void Response_status_decides_the_rules(int status, bool secretAllowed)
    {
        var robots = RobotsTxt.FromResponse(status, "User-agent: *\nDisallow: /secret");

        Assert.Equal(secretAllowed, robots.IsAllowed(Us, "/secret"));
        Assert.Equal(status < 500, robots.IsAllowed(Us, "/open"));
    }

    [Theory]
    [InlineData("DistributedScraperBot/1.0 (+mailto:you@example.com)", "DistributedScraperBot")]
    [InlineData("  MyBot  ", "MyBot")]
    [InlineData("MyBot (+https://example.com)", "MyBot")]
    public void Product_token_is_the_user_agent_before_slash_or_space(string userAgent, string token)
    {
        Assert.Equal(token, RobotsTxt.ProductTokenOf(userAgent));
    }

    private static RobotsTxt Fixture(string name) =>
        RobotsTxt.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "robots", name + ".txt")));
}
