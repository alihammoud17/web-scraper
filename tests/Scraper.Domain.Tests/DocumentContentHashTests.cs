using Scraper.Domain.Content;

namespace Scraper.Domain.Tests;

public class DocumentContentHashTests
{
    private static readonly GtaCarsFields AdderFields = new("Truffade", "Super", 1_000_000, 250.5m, TimeSpan.FromSeconds(62.4));

    [Fact]
    public void Same_content_gives_the_same_hash()
    {
        Assert.Equal(Adder().ComputeHash(), Adder().ComputeHash());
    }

    [Fact]
    public void Whitespace_only_differences_give_the_same_hash()
    {
        var tidy = Adder(title: "Truffade Adder", paragraph: "The fastest car in the game.");
        var messy = Adder(title: "  Truffade \n Adder ", paragraph: "The   fastest\tcar in\r\nthe game.  ");

        Assert.Equal(tidy.ComputeHash(), messy.ComputeHash());
    }

    [Fact]
    public void Unicode_composition_differences_give_the_same_hash()
    {
        var composed = Adder(paragraph: "Café");
        var decomposed = Adder(paragraph: "Café");

        Assert.Equal(composed.ComputeHash(), decomposed.ComputeHash());
    }

    [Fact]
    public void Changing_the_block_order_changes_the_hash()
    {
        var heading = ContentBlock.Heading("Overview", 2);
        var paragraph = ContentBlock.Paragraph("Text");

        var a = new DocumentContent("Adder", "Adder", null, AdderFields, [heading, paragraph]);
        var b = new DocumentContent("Adder", "Adder", null, AdderFields, [paragraph, heading]);

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void Changing_a_table_cell_changes_the_hash()
    {
        var a = Adder(table: Stats("250.5"));
        var b = Adder(table: Stats("251.0"));

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void Changing_a_site_field_changes_the_hash()
    {
        var cheaper = AdderFields with { PriceGtaDollars = 900_000 };

        Assert.NotEqual(Adder().ComputeHash(), Adder(fields: cheaper).ComputeHash());
    }

    [Fact]
    public void Site_field_key_order_does_not_matter()
    {
        var forward = new DocumentContent("T", null, null, new OrderedFields(reverse: false), []);
        var reversed = new DocumentContent("T", null, null, new OrderedFields(reverse: true), []);

        Assert.Equal(forward.ComputeHash(), reversed.ComputeHash());
    }

    [Fact]
    public void List_item_order_matters()
    {
        var a = new DocumentContent("T", null, null, new NewswireFields(null, ["GTA Online", "Bonuses"]), []);
        var b = new DocumentContent("T", null, null, new NewswireFields(null, ["Bonuses", "GTA Online"]), []);

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void Null_and_empty_give_different_hashes()
    {
        var nullCategory = new DocumentContent("T", null, null, new NewswireFields(null, []), []);
        var emptyCategory = new DocumentContent("T", null, null, new NewswireFields("", []), []);

        Assert.NotEqual(nullCategory.ComputeHash(), emptyCategory.ComputeHash());
    }

    [Fact]
    public void Part_boundaries_cannot_be_confused()
    {
        var a = new DocumentContent("T", null, null, new NewswireFields(null, []), [List("ab", "c")]);
        var b = new DocumentContent("T", null, null, new NewswireFields(null, []), [List("a", "bc")]);

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void Metrics_do_not_affect_the_hash()
    {
        // Two crawls of the same mod: the content is identical, only the counters moved (ADR 0009).
        var documentId = Guid.CreateVersion7();
        var fields = new Gta5ModsFields("someauthor", "1.2", ["Car", "Add-On"], null, null);
        var firstCrawl = new DocumentContent("Adder Mod", "Adder", null, fields, [ContentBlock.Paragraph("Description")]);
        var secondCrawl = new DocumentContent("Adder Mod", "Adder", null, fields, [ContentBlock.Paragraph("Description")]);
        var firstMetrics = MetricsSnapshot.Capture(documentId, Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, 1_000, 50, 9_000);
        var secondMetrics = MetricsSnapshot.Capture(documentId, Guid.CreateVersion7(), DateTimeOffset.UnixEpoch.AddDays(1), 1_250, 61, 11_400);

        Assert.NotEqual(firstMetrics.Downloads, secondMetrics.Downloads);
        Assert.Equal(firstCrawl.ComputeHash(), secondCrawl.ComputeHash());
    }

    private static DocumentContent Adder(
        string title = "Truffade Adder",
        string paragraph = "The fastest car in the game.",
        TableBlock? table = null,
        GtaCarsFields? fields = null) =>
        new(title, "Adder", null, fields ?? AdderFields,
            [ContentBlock.Heading("Overview", 2), ContentBlock.Paragraph(paragraph), table ?? Stats("250.5")]);

    private static TableBlock Stats(string topSpeed) =>
        new("Stats", ["Stat", "Value"], [["Top speed", topSpeed], ["Seats", "2"]]);

    private static ContentBlock List(params string[] items) => ContentBlock.List(items, ordered: false);

    private sealed record OrderedFields(bool reverse) : SiteFields
    {
        public override string Site => "test";

        public override IEnumerable<KeyValuePair<string, string?>> CanonicalFields()
        {
            KeyValuePair<string, string?>[] fields = [Field("a", "1"), Field("b", "2"), Field("c", null)];
            return reverse ? fields.Reverse() : fields;
        }
    }
}
