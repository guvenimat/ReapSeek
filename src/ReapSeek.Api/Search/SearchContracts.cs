namespace ReapSeek.Api.Search;

public sealed record SearchRequest(
    string? Query, string? Category, List<string> Brands, List<string> Colors, List<string> Sizes,
    decimal? MinPrice, decimal? MaxPrice, bool InStockOnly, string Sort, int Page, int PageSize);

public sealed record ProductCard(
    long Id, string Slug, string Title, string Brand, string CategoryName, decimal? Price, decimal? ListPrice,
    int Discount, bool InStock, int SellerCount, string? ImageUrl, double? Rating = null, int ReviewCount = 0);

public sealed record ProductCardsResponse(List<ProductCard> Products);

public sealed record ProductCategory(string Path, string Name);

public sealed record FacetValue(string Value, string Label, long Count, bool Selected);
public sealed record PriceStats(decimal? Min, decimal? Max);
public sealed record SearchFacets(
    List<FacetValue> Departments, List<FacetValue> Categories, List<FacetValue> Brands, List<FacetValue> Colors,
    List<FacetValue> Sizes, PriceStats Price);
public sealed record SearchResponse(
    string? Query, string? Category, string Sort, List<ProductCard> Products, long Total, int Page, int PageSize,
    SearchFacets Facets, int TookMs);

public sealed record SuggestResponse(string Query, List<ProductCard> Products, List<FacetValue> Brands, List<FacetValue> Categories);

public sealed record CategoryNode(long Id, string Slug, string Name, string Path, int Depth, string ImageKind, List<CategoryNode> Children);
public sealed record CategoryTreeResponse(List<CategoryNode> Departments);

public static class SearchSort
{
    public const string Relevance = "relevance";
    public const string PriceAsc = "price_asc";
    public const string PriceDesc = "price_desc";
    public const string Newest = "newest";
    public const string Discount = "discount";
    public const string Rating = "rating";
    public static readonly string[] All = [Relevance, PriceAsc, PriceDesc, Newest, Discount, Rating];
}
