using System.Text.Json.Nodes;
using ReapSeek.Api.Search;
using ReapSeek.Shared;
using ReapSeek.Shared.Search;
using ReapSeek.Worker.Search;

namespace ReapSeek.UnitTests;

public class SearchTests
{
    static SearchRequest Request(string? q = null, string? category = null, string[]? brand = null, string[]? color = null, string[]? size = null,
        decimal? min = null, decimal? max = null, bool? inStock = null, string? sort = null, int? page = null) =>
        SearchQuery.Parse(q, category, brand, color, size, min, max, inStock, sort, page, null);

    [Fact]
    public void Parse_normalizes_and_defaults()
    {
        var r = Request(q: "  linen dress ", category: "/women/dresses/", brand: ["Velora,Coralie", "Velora"]);
        Assert.Equal("linen dress", r.Query);
        Assert.Equal("women/dresses", r.Category);
        Assert.Equal(["Velora", "Coralie"], r.Brands);
        Assert.Equal(SearchSort.Relevance, r.Sort);
        Assert.Equal((1, SearchQuery.DefaultPageSize), (r.Page, r.PageSize));
        Assert.Null(Request(q: "   ").Query);
    }

    [Fact]
    public void Parse_rejects_invalid_input()
    {
        Assert.Throws<ValidationException>(() => Request(sort: "cheapest"));
        Assert.Throws<ValidationException>(() => Request(page: 101));
        Assert.Throws<ValidationException>(() => Request(min: 100, max: 50));
        Assert.Throws<ValidationException>(() => Request(min: -1));
        Assert.Throws<ValidationException>(() => Request(q: new string('a', 101)));
        Assert.Throws<ValidationException>(() => Request(brand: Enumerable.Range(0, 11).Select(i => $"b{i}").ToArray()));
    }

    [Fact]
    public void Facet_filters_go_to_post_filter_and_each_facet_ignores_its_own_filter()
    {
        var body = SearchQuery.Build(Request(category: "women", brand: ["Velora"], color: ["Black", "Navy"], min: 100));
        var post = body["post_filter"]!["bool"]!["filter"]!.ToJsonString();
        Assert.Contains("\"category_paths\":\"women\"", post);
        Assert.Contains("\"brand\":[\"Velora\"]", post);
        Assert.Contains("\"colors\":[\"Black\",\"Navy\"]", post);
        Assert.Contains("\"gte\":100", post);

        var brandFacet = body["aggs"]!["f_brand"]!["filter"]!.ToJsonString();
        Assert.DoesNotContain("\"brand\":", brandFacet);
        Assert.Contains("\"colors\":", brandFacet);
        var colorFacet = body["aggs"]!["f_color"]!["filter"]!.ToJsonString();
        Assert.DoesNotContain("\"colors\":", colorFacet);
        Assert.Contains("\"brand\":", colorFacet);
        Assert.DoesNotContain("\"gte\"", body["aggs"]!["f_price"]!["filter"]!.ToJsonString());
        Assert.DoesNotContain("category_paths", body["aggs"]!["f_category"]!["filter"]!.ToJsonString());
    }

    [Fact]
    public void Text_queries_use_synonyms_and_typo_tolerance_only_for_longer_words()
    {
        var json = SearchQuery.Build(Request(q: "linen dres")).ToJsonString();
        Assert.Contains("\"analyzer\":\"product_synonyms\"", json);
        Assert.Contains("\"fuzziness\":\"AUTO:4,7\"", json);
        Assert.Contains("\"match_phrase\"", json);
        Assert.DoesNotContain("match_all", json);
        Assert.Contains("match_all", SearchQuery.Build(Request()).ToJsonString());
    }

    [Fact]
    public void In_stock_only_is_a_query_filter_not_a_facet()
    {
        var body = SearchQuery.Build(Request(inStock: true));
        Assert.Contains("\"in_stock\":true", body["query"]!["bool"]!["filter"]!.ToJsonString());
        Assert.DoesNotContain("in_stock", body["post_filter"]!.ToJsonString());
    }

    [Theory]
    [InlineData(null, null, "in_stock,seller_count,discount,created_at,id")]
    [InlineData("dress", null, "_score,in_stock,seller_count,id")]
    [InlineData(null, "price_asc", "in_stock,price,id")]
    [InlineData(null, "price_desc", "in_stock,price,id")]
    [InlineData(null, "newest", "created_at,id")]
    [InlineData(null, "discount", "in_stock,discount,price,id")]
    public void Sorting_keeps_in_stock_products_first(string? q, string? sort, string expected)
    {
        var fields = SearchQuery.Build(Request(q: q, sort: sort))["sort"]!.AsArray().Select(s => s!.AsObject().First().Key);
        Assert.Equal(expected, string.Join(',', fields));
    }

    [Fact]
    public void Pagination_sets_from_and_size()
    {
        var body = SearchQuery.Build(Request(page: 3));
        Assert.Equal(48, body["from"]!.GetValue<int>());
        Assert.Equal(24, body["size"]!.GetValue<int>());
    }

    static JsonNode Response(string hits, string brands = "[]", string colors = "[]", string sizes = "[]") => JsonNode.Parse($$"""
        {
          "took": 7,
          "hits": { "total": { "value": 42 }, "hits": [{{hits}}] },
          "aggregations": {
            "f_category": {
              "departments": { "buckets": [{ "key": "women", "doc_count": 40 }] },
              "categories": { "buckets": [{ "key": "women/dresses", "doc_count": 30 }] }
            },
            "f_brand": { "values": { "buckets": {{brands}} } },
            "f_color": { "values": { "buckets": {{colors}} } },
            "f_size": { "values": { "buckets": {{sizes}} } },
            "f_price": { "stats": { "min": 99.9, "max": 1499.0 } }
          }
        }
        """)!;

    const string Hit = """
        { "_source": { "id": 5, "slug": "linen-dress-p5", "title": "Velora Linen Dress", "brand": "Velora", "category_name": "Dresses",
          "price": 499.9, "list_price": 699.9, "discount": 29, "in_stock": true, "seller_count": 3,
          "image_url": "/media/catalog/dress/brown/5.svg",
          "color_images": { "Brown": "/media/catalog/dress/brown/5.svg", "Black": "/media/catalog/dress/black/5.svg" } } }
        """;

    [Fact]
    public void Results_are_parsed_with_labels_and_selected_values()
    {
        var names = new Dictionary<string, string> { ["women"] = "Women", ["women/dresses"] = "Dresses" };
        var r = SearchQuery.ParseResults(Response(Hit, brands: """[{ "key": "Velora", "doc_count": 12 }]"""),
            Request(category: "women", brand: ["Velora", "Coralie"]), names);
        Assert.Equal(42, r.Total);
        Assert.Equal(7, r.TookMs);
        var card = Assert.Single(r.Products);
        Assert.Equal(new ProductCard(5, "linen-dress-p5", "Velora Linen Dress", "Velora", "Dresses", 499.9m, 699.9m, 29, true, 3, "/media/catalog/dress/brown/5.svg"), card);
        Assert.Equal(new FacetValue("women", "Women", 40, true), r.Facets.Departments[0]);
        Assert.Equal("Dresses", r.Facets.Categories[0].Label);
        Assert.Equal([new FacetValue("Velora", "Velora", 12, true), new FacetValue("Coralie", "Coralie", 0, true)], r.Facets.Brands);
        Assert.Equal(new PriceStats(99.9m, 1499.0m), r.Facets.Price);
    }

    [Fact]
    public void Cards_show_the_image_of_the_filtered_color()
    {
        var r = SearchQuery.ParseResults(Response(Hit), Request(color: ["Black"]), new Dictionary<string, string>());
        Assert.Equal("/media/catalog/dress/black/5.svg", r.Products[0].ImageUrl);
        var other = SearchQuery.ParseResults(Response(Hit), Request(color: ["Teal"]), new Dictionary<string, string>());
        Assert.Equal("/media/catalog/dress/brown/5.svg", other.Products[0].ImageUrl);
    }

    [Fact]
    public void Sizes_are_ordered_like_a_size_chart()
    {
        var buckets = """[{"key":"XL","doc_count":1},{"key":"40","doc_count":9},{"key":"S","doc_count":5},{"key":"38","doc_count":2},{"key":"M","doc_count":3}]""";
        var r = SearchQuery.ParseResults(Response("", sizes: buckets), Request(), new Dictionary<string, string>());
        Assert.Equal(["S", "M", "XL", "38", "40"], r.Facets.Sizes.Select(s => s.Value));
    }

    [Fact]
    public void Suggested_brands_must_start_with_a_typed_word()
    {
        var node = JsonNode.Parse("""
            { "hits": { "hits": [] }, "aggregations": {
              "brands": { "buckets": [{ "key": "Velora", "doc_count": 5 }, { "key": "Wren & Sage", "doc_count": 4 }, { "key": "Velvet Moss", "doc_count": 2 }] },
              "categories": { "buckets": [] } } }
            """)!;
        var s = SearchQuery.ParseSuggest(node, "VEL", new Dictionary<string, string>());
        Assert.Equal(["Velora", "Velvet Moss"], s.Brands.Select(b => b.Value));
        Assert.Equal(["Wren & Sage"], SearchQuery.ParseSuggest(node, "sage dress", new Dictionary<string, string>()).Brands.Select(b => b.Value));
    }

    static SearchRepository.DocumentRecord Record(decimal? price, decimal? listPrice) => new(
        7, "x-p7", "Velora Linen Dress", "Soft linen.", "active", "Velora", "velora", "women", "Women", "women/dresses", "Dresses",
        """{"Material":"Linen"}""", "Navy|Black", "S|M", "Storage: 128 GB", price, listPrice, 2, "/media/catalog/dress/navy/7.svg",
        "Black=/media/catalog/dress/black/7.svg|Navy=/media/catalog/dress/navy/7.svg", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 4.5, 12);

    [Fact]
    public void Documents_carry_category_ancestors_discount_and_color_images()
    {
        var d = SearchRepository.ToDocument(Record(70m, 100m));
        Assert.Equal(["women", "women/dresses"], d.CategoryPaths);
        Assert.Equal(["Black", "Navy"], d.Colors);
        Assert.Equal(["Storage: 128 GB"], d.Options);
        Assert.Equal(30, d.Discount);
        Assert.Equal(100m, d.ListPrice);
        Assert.True(d.InStock);
        Assert.Equal("/media/catalog/dress/black/7.svg", d.ColorImages["Black"]);
        Assert.Equal("Linen", d.Attributes["Material"]);
    }

    [Fact]
    public void Documents_without_a_real_discount_or_offer()
    {
        var noDiscount = SearchRepository.ToDocument(Record(100m, 100m));
        Assert.Equal((0, (decimal?)null), (noDiscount.Discount, noDiscount.ListPrice));
        var soldOut = SearchRepository.ToDocument(Record(null, null));
        Assert.False(soldOut.InStock);
    }

    [Fact]
    public void Index_definition_is_valid_json_with_version_and_synonyms()
    {
        var def = JsonNode.Parse(ProductIndex.Definition)!;
        Assert.Equal(ProductIndex.Version, def["mappings"]!["_meta"]!["version"]!.GetValue<int>());
        Assert.Equal(ProductIndex.Synonyms.Length, def["settings"]!["analysis"]!["filter"]!["product_synonyms"]!["synonyms"]!.AsArray().Count);
        Assert.Equal("strict", def["mappings"]!["dynamic"]!.GetValue<string>());
        Assert.Matches("^products_[0-9]{17}$", ProductIndex.NewIndexName(new DateTime(2026, 10, 30, 12, 0, 0, 5)));
    }
}
