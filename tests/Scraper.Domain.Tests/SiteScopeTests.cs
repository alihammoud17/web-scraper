using Scraper.Domain.Sites;
using Scraper.Domain.Urls;

namespace Scraper.Domain.Tests;

public class SiteScopeTests
{
    private static readonly SiteConfig Newswire = Site("https://www.rockstargames.com/newswire", "/newswire");
    private static readonly SiteConfig WholeSite = Site("https://gtacars.net/");
    private static readonly SiteConfig TestSite = Site("http://testsite:8080/");

    [Theory]
    [InlineData("https://www.rockstargames.com/newswire")]
    [InlineData("https://www.rockstargames.com/newswire/article/39akk424a9oo53/gta-vi")]
    [InlineData("https://www.rockstargames.com/newswire?page=2")]
    public void Urls_under_an_allowed_prefix_are_in_scope(string url)
    {
        Assert.True(Newswire.IsInScope(NormalizedUrl.Create(url)));
    }

    [Theory]
    [InlineData("https://www.rockstargames.com/")]                 // outside the prefix
    [InlineData("https://www.rockstargames.com/newswirefoo")]      // prefix must match whole segments
    [InlineData("https://www.rockstargames.com/gta-v")]
    [InlineData("https://socialclub.rockstargames.com/newswire")]  // other host
    [InlineData("http://www.rockstargames.com/newswire")]          // other scheme
    public void Other_hosts_schemes_and_paths_are_out_of_scope(string url)
    {
        Assert.False(Newswire.IsInScope(NormalizedUrl.Create(url)));
    }

    [Theory]
    [InlineData("https://gtacars.net/", true)]
    [InlineData("https://gtacars.net/gta5/adder", true)]
    [InlineData("https://www.gtacars.net/gta5/adder", false)]
    public void Empty_prefixes_mean_the_whole_site(string url, bool inScope)
    {
        Assert.Equal(inScope, WholeSite.IsInScope(NormalizedUrl.Create(url)));
    }

    [Theory]
    [InlineData("http://testsite:8080/pages/1", true)]
    [InlineData("http://testsite/pages/1", false)]
    [InlineData("http://testsite:9090/pages/1", false)]
    public void The_port_is_part_of_the_site(string url, bool inScope)
    {
        Assert.Equal(inScope, TestSite.IsInScope(NormalizedUrl.Create(url)));
    }

    private static SiteConfig Site(string baseUrl, params string[] prefixes)
    {
        var url = NormalizedUrl.Create(baseUrl);
        return new SiteConfig("test", "Test", url, [url], prefixes, FetchMode.Http, new RateLimit(1m), 2, 100);
    }
}
