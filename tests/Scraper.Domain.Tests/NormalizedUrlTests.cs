using Scraper.Domain.Urls;

namespace Scraper.Domain.Tests;

public class NormalizedUrlTests
{
    [Theory]
    [InlineData("HTTP://GTACars.NET/Vehicle/Adder", "http://gtacars.net/Vehicle/Adder")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("http://example.com:80/a", "http://example.com/a")]
    [InlineData("https://example.com:8080/a", "https://example.com:8080/a")]
    [InlineData("https://example.com/a#comments", "https://example.com/a")]
    [InlineData("https://example.com/a/", "https://example.com/a")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("https://example.com/a/../b/./c", "https://example.com/b/c")]
    [InlineData("https://example.com/a?", "https://example.com/a")]
    [InlineData("https://example.com/a?b=2&a=1", "https://example.com/a?a=1&b=2")]
    [InlineData("https://example.com/a?utm_source=x&b=2&UTM_medium=y&fbclid=z&gclid=w", "https://example.com/a?b=2")]
    [InlineData("https://example.com/a%2fb?q=%c3%a9", "https://example.com/a%2Fb?q=%C3%A9")]
    [InlineData("https://bücher.example/a", "https://xn--bcher-kva.example/a")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    public void Equivalent_forms_normalize_to_one_value(string input, string expected)
    {
        Assert.Equal(expected, NormalizedUrl.Create(input).Value);
    }

    [Theory]
    [InlineData("HTTPS://Example.com:443/Path/?utm_campaign=1&z=1&a=2#top")]
    [InlineData("https://www.gta5-mods.com/vehicles/2")]
    [InlineData("https://example.com/a%2fb?q=%c3%a9")]
    public void Normalizing_twice_gives_the_same_value(string input)
    {
        var once = NormalizedUrl.Create(input);
        var twice = NormalizedUrl.Create(once.Value);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Path_case_is_kept()
    {
        Assert.Equal("https://gtacars.net/gta5/Adder", NormalizedUrl.Create("https://gtacars.net/gta5/Adder").Value);
    }

    [Theory]
    [InlineData("/vehicles/3", "https://www.gta5-mods.com/vehicles/3")]
    [InlineData("3", "https://www.gta5-mods.com/vehicles/3")]
    [InlineData("../tools", "https://www.gta5-mods.com/tools")]
    [InlineData("https://other.example/x", "https://other.example/x")]
    public void Relative_href_resolves_against_the_base(string href, string expected)
    {
        var baseUri = new Uri("https://www.gta5-mods.com/vehicles/2");

        Assert.True(NormalizedUrl.TryCreate(href, baseUri, out var url));
        Assert.Equal(expected, url.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/vehicles/3")]
    [InlineData("vehicles/3")]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:secret@example.com/a")]
    public void Rejects_urls_that_are_not_crawlable(string input)
    {
        Assert.False(NormalizedUrl.TryCreate(input, null, out _));
        Assert.Throws<ArgumentException>(() => NormalizedUrl.Create(input));
    }

    [Fact]
    public void Rejects_javascript_href_even_with_a_base()
    {
        Assert.False(NormalizedUrl.TryCreate("javascript:void(0)", new Uri("https://example.com/"), out _));
    }

    [Fact]
    public void Rejects_urls_longer_than_the_maximum()
    {
        var url = "https://example.com/" + new string('a', NormalizedUrl.MaxLength);

        Assert.False(NormalizedUrl.TryCreate(url, null, out _));
    }

    [Fact]
    public void Equal_urls_are_equal_values()
    {
        var a = NormalizedUrl.Create("https://Example.com/a?b=1&a=2");
        var b = NormalizedUrl.Create("https://example.com/a/?a=2&b=1#x");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.Value, a.ToString());
    }
}
