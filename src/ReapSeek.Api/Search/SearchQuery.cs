using System.Globalization;
using System.Text.Json.Nodes;
using ReapSeek.Shared;

namespace ReapSeek.Api.Search;

public static class SearchQuery
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;
    public const int MaxPage = 100;
    const int MaxFilterValues = 10;
    const int MaxQueryLength = 100;
    static readonly string[] SizeOrder = ["XS", "S", "M", "L", "XL", "XXL"];
    static readonly string[] CardFields = ["id", "slug", "title", "brand", "category_name", "price", "list_price", "discount", "in_stock", "seller_count", "image_url", "color_images"];

    public static SearchRequest Parse(string? q, string? category, string[]? brand, string[]? color, string[]? size,
        decimal? minPrice, decimal? maxPrice, bool? inStock, string? sort, int? page, int? pageSize)
    {
        var query = q?.Trim() is { Length: > 0 } t ? t : null;
        Ensure.That(query is null || query.Length <= MaxQueryLength, $"q can be at most {MaxQueryLength} characters");
        var path = category?.Trim().Trim('/') is { Length: > 0 } c ? c : null;
        Ensure.That(path is null || path.Length <= 200, "category is too long");
        Ensure.That(minPrice is null or >= 0 && maxPrice is null or >= 0, "prices must be positive");
        Ensure.That(minPrice is null || maxPrice is null || minPrice <= maxPrice, "min_price must not exceed max_price");
        var s = sort?.Trim() is { Length: > 0 } x ? Ensure.OneOf(x, "sort", SearchSort.All) : SearchSort.Relevance;
        return new SearchRequest(query, path, Values(brand, "brand"), Values(color, "color"), Values(size, "size"),
            minPrice, maxPrice, inStock ?? false, s,
            Ensure.InRange(page ?? 1, 1, MaxPage, "page"), Ensure.InRange(pageSize ?? DefaultPageSize, 1, MaxPageSize, "page_size"));
    }

    static List<string> Values(string[]? values, string name)
    {
        var list = (values ?? []).SelectMany(v => v.Split(',')).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        Ensure.That(list.Count <= MaxFilterValues && list.All(v => v.Length <= 100), $"{name} accepts at most {MaxFilterValues} values");
        return list;
    }

    public static JsonObject Build(SearchRequest r)
    {
        var facets = FacetFilters(r);
        var query = new JsonObject
        {
            ["bool"] = new JsonObject
            {
                ["must"] = r.Query is null ? Arr(new JsonObject { ["match_all"] = new JsonObject() }) : Arr(TextQuery(r.Query)),
                ["should"] = r.Query is null ? Arr(InStockBoost()) : Arr([.. Boosts(r.Query), InStockBoost()]),
                ["filter"] = r.InStockOnly ? Arr(Term("in_stock", true)) : new JsonArray(),
            },
        };
        return new JsonObject
        {
            ["from"] = (r.Page - 1) * r.PageSize,
            ["size"] = r.PageSize,
            ["track_total_hits"] = true,
            ["_source"] = Arr([.. CardFields.Select(f => (JsonNode)f)]),
            ["query"] = query,
            ["post_filter"] = AllOf(facets.Values),
            ["sort"] = Sort(r),
            ["aggs"] = new JsonObject
            {
                ["f_category"] = FacetAgg(facets, "category", new JsonObject
                {
                    ["departments"] = Terms("department", 20),
                    ["categories"] = Terms("category_path", 100),
                }),
                ["f_brand"] = FacetAgg(facets, "brand", new JsonObject { ["values"] = Terms("brand", 50) }),
                ["f_color"] = FacetAgg(facets, "color", new JsonObject { ["values"] = Terms("colors", 30) }),
                ["f_size"] = FacetAgg(facets, "size", new JsonObject { ["values"] = Terms("sizes", 40) }),
                ["f_price"] = FacetAgg(facets, "price", new JsonObject { ["stats"] = new JsonObject { ["stats"] = new JsonObject { ["field"] = "price" } } }),
            },
        };
    }

    public static JsonObject BuildSuggest(string q) => new()
    {
        ["size"] = 6,
        ["_source"] = Arr([.. CardFields.Select(f => (JsonNode)f)]),
        ["query"] = new JsonObject
        {
            ["bool"] = new JsonObject
            {
                ["must"] = Arr(new JsonObject
                {
                    ["multi_match"] = new JsonObject
                    {
                        ["query"] = q,
                        ["type"] = "bool_prefix",
                        ["fields"] = Arr("title.suggest", "title.suggest._2gram", "title.suggest._3gram", "brand.text"),
                        ["operator"] = "and",
                        ["fuzziness"] = "AUTO:4,7",
                        ["prefix_length"] = 1,
                    },
                }),
                ["should"] = Arr(InStockBoost()),
            },
        },
        ["aggs"] = new JsonObject
        {
            ["brands"] = Terms("brand", 10),
            ["categories"] = Terms("category_path", 4),
        },
    };

    public static SearchResponse ParseResults(JsonNode result, SearchRequest r, IReadOnlyDictionary<string, string> categoryNames)
    {
        var aggs = result["aggregations"]!;
        var cat = aggs["f_category"]!;
        var price = aggs["f_price"]!["stats"]!;
        var facets = new SearchFacets(
            Buckets(cat["departments"]!, r.Category is { } c ? [c.Split('/')[0]] : [], v => categoryNames.GetValueOrDefault(v, v)),
            Buckets(cat["categories"]!, r.Category is { } cc ? [cc] : [], v => categoryNames.GetValueOrDefault(v, v)),
            Buckets(aggs["f_brand"]!["values"]!, r.Brands, v => v),
            Buckets(aggs["f_color"]!["values"]!, r.Colors, v => v),
            OrderSizes(Buckets(aggs["f_size"]!["values"]!, r.Sizes, v => v)),
            new PriceStats(Decimal(price["min"]), Decimal(price["max"])));
        return new SearchResponse(r.Query, r.Category, r.Sort, Cards(result, r.Colors), result["hits"]!["total"]!["value"]!.GetValue<long>(),
            r.Page, r.PageSize, facets, result["took"]?.GetValue<int>() ?? 0);
    }

    public static SuggestResponse ParseSuggest(JsonNode result, string q, IReadOnlyDictionary<string, string> categoryNames)
    {
        var tokens = Fold(q).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var brands = Buckets(result["aggregations"]!["brands"]!, [], v => v)
            .Where(b => Fold(b.Label).Split(' ').Any(w => tokens.Any(t => w.StartsWith(t, StringComparison.Ordinal))))
            .Take(3).ToList();
        var categories = Buckets(result["aggregations"]!["categories"]!, [], v => categoryNames.GetValueOrDefault(v, v));
        return new SuggestResponse(q, Cards(result, []), brands, categories);
    }

    static List<ProductCard> Cards(JsonNode result, List<string> colors) =>
        result["hits"]!["hits"]!.AsArray().Select(h =>
        {
            var s = h!["_source"]!;
            var image = colors.Select(c => s["color_images"]?[c]?.GetValue<string>()).FirstOrDefault(u => u is not null) ?? s["image_url"]?.GetValue<string>();
            return new ProductCard(s["id"]!.GetValue<long>(), Str(s["slug"]), Str(s["title"]), Str(s["brand"]), Str(s["category_name"]),
                Decimal(s["price"]), Decimal(s["list_price"]), s["discount"]?.GetValue<int>() ?? 0, s["in_stock"]?.GetValue<bool>() ?? false,
                s["seller_count"]?.GetValue<int>() ?? 0, image);
        }).ToList();

    static JsonObject TextQuery(string q) => new()
    {
        ["bool"] = new JsonObject
        {
            ["should"] = Arr(
                new JsonObject { ["match"] = new JsonObject { ["all_text"] = new JsonObject { ["query"] = q, ["analyzer"] = "product_synonyms", ["operator"] = "and", ["boost"] = 2 } } },
                new JsonObject { ["match"] = new JsonObject { ["all_text"] = new JsonObject { ["query"] = q, ["fuzziness"] = "AUTO:4,7", ["prefix_length"] = 1, ["operator"] = "and" } } }),
            ["minimum_should_match"] = 1,
        },
    };

    static IEnumerable<JsonNode> Boosts(string q) =>
    [
        new JsonObject { ["match_phrase"] = new JsonObject { ["title"] = new JsonObject { ["query"] = q, ["boost"] = 6 } } },
        new JsonObject { ["match"] = new JsonObject { ["title"] = new JsonObject { ["query"] = q, ["operator"] = "and", ["boost"] = 3 } } },
        new JsonObject { ["match"] = new JsonObject { ["brand.text"] = new JsonObject { ["query"] = q, ["boost"] = 2 } } },
        new JsonObject { ["match"] = new JsonObject { ["category_name"] = new JsonObject { ["query"] = q, ["analyzer"] = "product_synonyms", ["boost"] = 2 } } },
    ];

    static JsonObject InStockBoost() => new() { ["term"] = new JsonObject { ["in_stock"] = new JsonObject { ["value"] = true, ["boost"] = 0.5 } } };

    static Dictionary<string, JsonNode> FacetFilters(SearchRequest r)
    {
        var filters = new Dictionary<string, JsonNode>();
        if (r.Category is { } c) filters["category"] = Term("category_paths", c);
        if (r.Brands.Count > 0) filters["brand"] = TermsFilter("brand", r.Brands);
        if (r.Colors.Count > 0) filters["color"] = TermsFilter("colors", r.Colors);
        if (r.Sizes.Count > 0) filters["size"] = TermsFilter("sizes", r.Sizes);
        if (r.MinPrice is not null || r.MaxPrice is not null)
        {
            var range = new JsonObject();
            if (r.MinPrice is { } min) range["gte"] = min;
            if (r.MaxPrice is { } max) range["lte"] = max;
            filters["price"] = new JsonObject { ["range"] = new JsonObject { ["price"] = range } };
        }
        return filters;
    }

    static JsonObject FacetAgg(Dictionary<string, JsonNode> facets, string own, JsonObject aggs) => new()
    {
        ["filter"] = AllOf(facets.Where(f => f.Key != own).Select(f => f.Value)),
        ["aggs"] = aggs,
    };

    static JsonObject AllOf(IEnumerable<JsonNode> filters) =>
        new() { ["bool"] = new JsonObject { ["filter"] = Arr([.. filters.Select(f => f.DeepClone())]) } };

    static JsonArray Sort(SearchRequest r)
    {
        JsonObject Desc(string field) => new() { [field] = new JsonObject { ["order"] = "desc" } };
        JsonObject Asc(string field) => new() { [field] = new JsonObject { ["order"] = "asc", ["missing"] = "_last" } };
        return r.Sort switch
        {
            SearchSort.PriceAsc => Arr(Desc("in_stock"), Asc("price"), Asc("id")),
            SearchSort.PriceDesc => Arr(Desc("in_stock"), new JsonObject { ["price"] = new JsonObject { ["order"] = "desc", ["missing"] = "_last" } }, Asc("id")),
            SearchSort.Newest => Arr(Desc("created_at"), Asc("id")),
            SearchSort.Discount => Arr(Desc("in_stock"), Desc("discount"), Asc("price"), Asc("id")),
            _ when r.Query is not null => Arr(Desc("_score"), Desc("in_stock"), Desc("seller_count"), Asc("id")),
            _ => Arr(Desc("in_stock"), Desc("seller_count"), Desc("discount"), Desc("created_at"), Asc("id")),
        };
    }

    static List<FacetValue> Buckets(JsonNode agg, IReadOnlyCollection<string> selected, Func<string, string> label)
    {
        var values = agg["buckets"]!.AsArray()
            .Select(b => new FacetValue(Str(b!["key"]), label(Str(b["key"])), b["doc_count"]!.GetValue<long>(), selected.Contains(Str(b["key"]))))
            .ToList();
        foreach (var s in selected.Where(s => values.All(v => v.Value != s))) values.Add(new FacetValue(s, label(s), 0, true));
        return values;
    }

    static List<FacetValue> OrderSizes(List<FacetValue> sizes) =>
        sizes.OrderBy(s => Array.IndexOf(SizeOrder, s.Value) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(s => double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.MaxValue)
            .ThenBy(s => s.Value, StringComparer.Ordinal)
            .ToList();

    static JsonObject Term(string field, JsonNode value) => new() { ["term"] = new JsonObject { [field] = value } };

    static JsonObject TermsFilter(string field, List<string> values) =>
        new() { ["terms"] = new JsonObject { [field] = Arr([.. values.Select(v => (JsonNode)v)]) } };

    static JsonObject Terms(string field, int size) => new() { ["terms"] = new JsonObject { ["field"] = field, ["size"] = size } };

    static JsonArray Arr(params JsonNode?[] items) => new(items);

    static string Str(JsonNode? node) => node is null ? "" : node.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();

    static decimal? Decimal(JsonNode? node) => node is null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null
        ? null
        : Math.Round(node.GetValue<decimal>(), 2);

    static string Fold(string s) => new string(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
        .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('ı', 'i');
}
