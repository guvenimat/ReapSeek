namespace ReapSeek.Modules.Catalog;

public sealed record ProductCard(
    long Id, string Slug, string Title, string Brand, string CategoryName, decimal? Price, decimal? ListPrice,
    int Discount, bool InStock, int SellerCount, string? ImageUrl, double? Rating = null, int ReviewCount = 0);

public interface IProductCards
{
    Task<List<ProductCard>> CardsAsync(IReadOnlyCollection<long> ids, bool inStockOnly, CancellationToken ct);
}
