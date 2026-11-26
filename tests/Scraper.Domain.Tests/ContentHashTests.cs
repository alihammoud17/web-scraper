using Scraper.Domain.Content;

namespace Scraper.Domain.Tests;

public class ContentHashTests
{
    [Fact]
    public void Compute_matches_the_known_sha256_vector()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ContentHash.Compute("abc").Value);
    }

    [Fact]
    public void Compute_is_deterministic()
    {
        Assert.Equal(ContentHash.Compute("same input"), ContentHash.Compute("same input"));
        Assert.NotEqual(ContentHash.Compute("input a"), ContentHash.Compute("input b"));
    }

    [Fact]
    public void Parse_round_trips_a_computed_hash()
    {
        var hash = ContentHash.Compute("abc");

        Assert.Equal(hash, ContentHash.Parse(hash.Value));
    }

    [Fact]
    public void Parse_stores_uppercase_hex_as_lowercase()
    {
        var parsed = ContentHash.Parse("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");

        Assert.Equal(ContentHash.Compute("abc"), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ba7816bf")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad00")]
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Parse_rejects_values_that_are_not_64_hex_characters(string value)
    {
        Assert.Throws<FormatException>(() => ContentHash.Parse(value));
    }
}
