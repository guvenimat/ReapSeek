using ReapSeek.Catalog.Contracts;

namespace ReapSeek.Catalog.Api;

internal sealed record SearchRequest(
    string? Query, string? Category, List<string> Brands, List<string> Colors, List<string> Sizes,
    decimal? MinPrice, decimal? MaxPrice, bool InStockOnly, string Sort, int Page, int PageSize);

internal sealed record ProductCardsResponse(List<ProductCard> Products);

internal sealed record ProductCategory(string Path, string Name);

internal sealed record FacetValue(string Value, string Label, long Count, bool Selected);
internal sealed record PriceStats(decimal? Min, decimal? Max);
internal sealed record SearchFacets(
    List<FacetValue> Departments, List<FacetValue> Categories, List<FacetValue> Brands, List<FacetValue> Colors,
    List<FacetValue> Sizes, PriceStats Price);
internal sealed record SearchResponse(
    string? Query, string? Category, string Sort, List<ProductCard> Products, long Total, int Page, int PageSize,
    SearchFacets Facets, int TookMs);

internal sealed record SuggestResponse(string Query, List<ProductCard> Products, List<FacetValue> Brands, List<FacetValue> Categories);

internal sealed record CategoryNode(long Id, string Slug, string Name, string Path, int Depth, string ImageKind, List<CategoryNode> Children);
internal sealed record CategoryTreeResponse(List<CategoryNode> Departments);

internal static class SearchSort
{
    public const string Relevance = "relevance";
    public const string PriceAsc = "price_asc";
    public const string PriceDesc = "price_desc";
    public const string Newest = "newest";
    public const string Discount = "discount";
    public const string Rating = "rating";
    public static readonly string[] All = [Relevance, PriceAsc, PriceDesc, Newest, Discount, Rating];
}
