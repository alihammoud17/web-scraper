using Scraper.Domain.Urls;
using Scraper.Infrastructure.Crawling;

namespace Scraper.Infrastructure.Tests;

public class AngleSharpLinkExtractorTests
{
    private static readonly NormalizedUrl Page = NormalizedUrl.Create("http://testsite:8080/tree/0");
    private readonly AngleSharpLinkExtractor _extractor = new();

    [Fact]
    public void Resolves_relative_links_and_keeps_their_text()
    {
        var links = _extractor.Extract("""
            <a href="/tree/0-1">  First   child </a>
            <a href="0-2">Second</a>
            <a href="http://testsite:8080/tree/0-3?utm_source=x#top">Third</a>
            """, Page);

        Assert.Equal(["http://testsite:8080/tree/0-1", "http://testsite:8080/tree/0-2", "http://testsite:8080/tree/0-3"], links.Select(l => l.Url.Value));
        Assert.Equal("  First   child ", links[0].Text); // cleaned when stored (Link.Discover)
    }

    [Fact]
    public void Respects_the_base_element()
    {
        var links = _extractor.Extract("""<html><head><base href="http://testsite:8080/other/"></head><body><a href="page">x</a></body></html>""", Page);

        Assert.Equal("http://testsite:8080/other/page", Assert.Single(links).Url.Value);
    }

    [Fact]
    public void Skips_non_web_links_nofollow_and_links_to_the_same_page()
    {
        var links = _extractor.Extract("""
            <a href="mailto:owner@example.com">mail</a>
            <a href="javascript:void(0)">js</a>
            <a href="tel:+123">phone</a>
            <a href="#section">same page</a>
            <a href="/tree/0">same page again</a>
            <a href="/sponsored" rel="sponsored NoFollow">ad</a>
            <a>no href</a>
            <a href="/kept">kept</a>
            """, Page);

        Assert.Equal("http://testsite:8080/kept", Assert.Single(links).Url.Value);
    }

    [Fact]
    public void Off_site_links_are_returned_for_the_scope_check_to_decide()
    {
        var links = _extractor.Extract("""<a href="https://gtacars.net/gta5/adder">Adder</a>""", Page);

        Assert.Equal("https://gtacars.net/gta5/adder", Assert.Single(links).Url.Value);
    }
}
