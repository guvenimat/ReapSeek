using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Media;
using ReapSeek.Api.Seed;
using ReapSeek.Shared;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.CatalogSync;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.UnitTests;

public class CatalogTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    static readonly Lazy<SeedCatalog> Seed = new(() => CatalogGenerator.Generate(Now));

    [Theory]
    [InlineData("Wren & Sage Pleated Satin Dress", "wren-sage-pleated-satin-dress")]
    [InlineData("Çocuk Ayakkabısı Şık Gün", "cocuk-ayakkabisi-sik-gun")]
    [InlineData("  --Hello__World!! ", "hello-world")]
    [InlineData("!!!", "")]
    public void Slugs_are_lowercase_ascii_with_single_dashes(string input, string expected) =>
        Assert.Equal(expected, CatalogRules.Slugify(input));

    [Fact]
    public void Slugs_are_capped_and_product_slugs_end_with_the_id()
    {
        Assert.True(CatalogRules.Slugify(new string('a', 200)).Length <= CatalogRules.MaxSlugLength);
        Assert.Equal("linen-dress-p42", CatalogRules.ProductSlug("Linen Dress", 42));
        Assert.Equal("p42", CatalogRules.ProductSlug("!!!", 42));
    }

    [Fact]
    public void Without_axes_a_product_gets_one_default_variant() =>
        Assert.Equal([new Dictionary<string, string>()], CatalogRules.VariantMatrix(null));

    [Fact]
    public void Variant_axes_expand_into_every_combination()
    {
        var variants = CatalogRules.VariantMatrix(new() { ["Color"] = ["Black", "White"], ["Size"] = ["S", "M", "L"] });
        Assert.Equal(6, variants.Count);
        Assert.Equal(6, variants.Select(v => $"{v["Color"]}/{v["Size"]}").Distinct().Count());
        Assert.Equal(new Dictionary<string, string> { ["Color"] = "Black", ["Size"] = "S" }, variants[0]);
    }

    [Fact]
    public void Invalid_variant_axes_are_rejected()
    {
        Assert.Throws<ValidationException>(() => CatalogRules.VariantMatrix(new() { ["Size"] = ["S", "s"] }));
        Assert.Throws<ValidationException>(() => CatalogRules.VariantMatrix(new() { ["Size"] = [] }));
        Assert.Throws<ValidationException>(() => CatalogRules.VariantMatrix(new() { ["Size"] = ["S", " "] }));
        Assert.Throws<ValidationException>(() => CatalogRules.VariantMatrix(new()
            { ["A"] = Enumerable.Range(0, 11).Select(i => $"{i}").ToList(), ["B"] = Enumerable.Range(0, 10).Select(i => $"{i}").ToList() }));
        Assert.Throws<ValidationException>(() => CatalogRules.VariantMatrix(new() { ["A"] = ["1"], ["B"] = ["1"], ["C"] = ["1"], ["D"] = ["1"] }));
    }

    [Fact]
    public void Color_axis_is_detected_by_name() =>
        Assert.Equal("Colour", CatalogRules.ColorAxis(["Size", "Colour"]));

    [Fact]
    public void Valid_offers_are_rounded_and_defaulted()
    {
        var o = CatalogRules.Offer(new OfferRequest(1, 2, 10.005m, null, null, null, null));
        Assert.Equal(new ValidOffer(1, 2, 10.01m, null, 0, 1, OfferStatus.Active), o);
    }

    [Theory]
    [InlineData(0, null, 1, 1, "active")]
    [InlineData(100, null, -1, 1, "active")]
    [InlineData(100, null, 1, 11, "active")]
    [InlineData(100, null, 1, 1, "deleted")]
    public void Invalid_offers_are_rejected(double price, double? listPrice, int stock, int handling, string status) =>
        Assert.Throws<ValidationException>(() => CatalogRules.Offer(new OfferRequest(1, 2, (decimal)price, (decimal?)listPrice, stock, handling, status)));

    [Fact]
    public void List_price_below_the_price_is_rejected() =>
        Assert.Throws<ValidationException>(() => CatalogRules.Offer(new OfferRequest(1, 2, 100m, 99m, 1, 1, "active")));

    [Fact]
    public void Too_many_attributes_are_rejected() =>
        Assert.Throws<ValidationException>(() => CatalogRules.Attributes(Enumerable.Range(0, 31).ToDictionary(i => $"k{i}", i => "v")));

    [Fact]
    public void Every_art_kind_renders_valid_svg_in_every_color()
    {
        foreach (var kind in ProductArt.Kinds)
        foreach (var color in ProductArt.Colors)
        {
            var svg = XDocument.Parse(ProductArt.Render(kind, color, 12345));
            Assert.Equal("svg", svg.Root!.Name.LocalName);
            Assert.Equal("400", svg.Root.Attribute("width")!.Value);
        }
    }

    [Fact]
    public void Art_colors_are_found_by_name_and_urls_are_stable()
    {
        Assert.Equal("navy", ProductArt.FindColorByName("NAVY").Slug);
        Assert.Equal(ProductArt.FindColorByName("Teal"), ProductArt.FindColorByName("Teal"));
        Assert.Equal("/media/catalog/dress/navy/7.svg", ProductArt.Url("dress", "navy", 7));
    }

    [Theory]
    [InlineData(49.3, 49.90)]
    [InlineData(249.5, 249.90)]
    [InlineData(2449, 2499)]
    public void Seed_prices_use_retail_endings(double value, double expected) =>
        Assert.Equal((decimal)expected, CatalogGenerator.RoundPrice((decimal)value));

    [Fact]
    public void Seed_catalog_is_deterministic()
    {
        var again = CatalogGenerator.Generate(Now);
        Assert.Equal(Seed.Value.Products.Select(p => p.Title), again.Products.Select(p => p.Title));
        Assert.Equal(Seed.Value.Offers.Select(o => o.Price), again.Offers.Select(o => o.Price));
    }

    [Fact]
    public void Seed_catalog_is_consistent()
    {
        var c = Seed.Value;
        Assert.InRange(c.Products.Count, 2000, 3000);
        Assert.Equal(c.Products.Count, c.Products.Select(p => p.Slug).Distinct().Count());
        Assert.Equal(c.Variants.Count, c.Variants.Select(v => v.Sku).Distinct().Count());
        Assert.All(c.Products, p => Assert.Contains(c.Variants, v => v.ProductId == p.Id));
        Assert.Equal(c.Products.Select(p => p.Id).Order(), c.Images.Select(i => i.ProductId).Distinct().Order());
        Assert.All(c.Variants.GroupBy(v => v.ProductId), g => Assert.Equal(g.Count(), g.Select(v => v.OptionsJson).Distinct().Count()));

        var variantIds = c.Variants.Select(v => v.Id).ToHashSet();
        Assert.All(c.Offers, o =>
        {
            Assert.Contains(o.VariantId, variantIds);
            Assert.True(o.Price > 0);
            Assert.True(o.ListPrice is null || o.ListPrice > o.Price);
        });
        Assert.All(c.Offers.GroupBy(o => o.VariantId), g => Assert.InRange(g.Count(), 1, 4));
        Assert.Equal(c.Offers.Count, c.Offers.Select(o => (o.SellerId, o.VariantId)).Distinct().Count());

        var leaves = c.Categories.Where(x => x.Depth == 1).Select(x => x.Id).ToHashSet();
        Assert.All(c.Products, p => Assert.Contains(p.CategoryId, leaves));
        Assert.All(leaves, id => Assert.Contains(c.Products, p => p.CategoryId == id));
        Assert.All(c.Images, i => Assert.Matches("^/media/catalog/[a-z]+/[a-z]+/[0-9]+\\.svg$", i.Url));
    }

    [Fact]
    public void Seed_titles_do_not_repeat_words_or_mix_brands_into_descriptions()
    {
        var repeated = new Regex(@"\b(\w+) \1\b", RegexOptions.IgnoreCase);
        Assert.All(Seed.Value.Products, p =>
        {
            Assert.False(repeated.IsMatch(p.Title), p.Title);
            Assert.False(p.Description.Contains(p.Title, StringComparison.Ordinal) && !p.Description.Contains("published by", StringComparison.Ordinal), p.Description);
            Assert.NotNull(JsonSerializer.Deserialize<Dictionary<string, string>>(p.AttributesJson));
        });
    }

    [Fact]
    public void Synced_items_use_absolute_storefront_urls()
    {
        var item = CatalogSyncHandler.ToItem(new CatalogSyncRepository.ProductRecord(7, "linen-dress-p7", "Linen Dress", "active", "dresses", "women", "Velora", 499.90m,
            "/media/catalog/dress/navy/7.svg"), "http://shop.test");
        Assert.Equal("7", item.ItemId);
        Assert.True(item.InStock);
        Assert.Equal("http://shop.test/p/linen-dress-p7", item.Url);
        Assert.Equal("http://shop.test/media/catalog/dress/navy/7.svg", item.ImageUrl);
        Assert.Equal("women", item.Attributes["department"]);

        var soldOut = CatalogSyncHandler.ToItem(new CatalogSyncRepository.ProductRecord(8, "x-p8", "X", "active", "dresses", "women", "Velora", null, null), "http://shop.test");
        Assert.False(soldOut.InStock);
        Assert.Equal("", soldOut.ImageUrl);
    }

    [Fact]
    public void Catalog_messages_are_batched_and_others_run_alone()
    {
        var a = new OutboxRepository.MessageRecord(1, OutboxTypes.ProductsChanged, "{}", 1);
        var b = new OutboxRepository.MessageRecord(2, OutboxTypes.CatalogReindex, "{}", 1);
        var c = new OutboxRepository.MessageRecord(3, OutboxTypes.UserRegistered, "{}", 1);
        var d = new OutboxRepository.MessageRecord(4, OutboxTypes.UserRegistered, "{}", 1);
        Assert.Equal(OutboxDispatcher.GroupOf(a), OutboxDispatcher.GroupOf(b));
        Assert.NotEqual(OutboxDispatcher.GroupOf(c), OutboxDispatcher.GroupOf(d));
    }
}
