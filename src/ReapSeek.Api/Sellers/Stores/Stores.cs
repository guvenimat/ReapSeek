using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Catalog.Search;
using ReapSeek.Shared;

namespace ReapSeek.Api.Sellers.Stores;

public sealed record StoreSeller(long Id, string Name, string Slug, string City, decimal Rating, DateTime JoinedAt, int ProductCount,
    int DeliveredPackages, decimal? AverageHandlingDays);
public sealed record StoreSellerResponse(StoreSeller Seller, List<ProductCard> Products, int Total, int Page, int PageSize);

public sealed class StoreRepository(NpgsqlDataSource db)
{
    public sealed record SlugRecord(string Slug);
    public sealed record SellerRecord(long Id, string Name, string Slug, string City, decimal Rating, DateTime CreatedAt, int DeliveredPackages);

    public async Task<SellerRecord?> SellerAsync(string slug, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SellerRecord>("""
            SELECT s.id, s.name, s.slug, s.city, s.rating, s.created_at,
                (SELECT count(*)::int FROM order_packages p WHERE p.seller_id = s.id AND p.status = 'delivered') AS delivered_packages
            FROM sellers.sellers s WHERE s.slug = @Slug AND s.status = 'active'
            """, new SlugRecord(slug));
    }
}

public static class StoreEndpoints
{
    const int PageSize = 24;

    public static void MapStores(this RouteGroupBuilder api)
    {
        api.MapGet("/catalog/sellers/{slug}", async (string slug, int? page, StoreRepository stores, SellerCatalog sellerCatalog, SearchService search, CancellationToken ct) =>
        {
            var p = Ensure.InRange(page ?? 1, 1, 1000, "page");
            if (slug.Length > 100 || await stores.SellerAsync(slug, ct) is not { } s) return ApiResults.NotFound("Seller not found");
            var (ids, total, handling) = await sellerCatalog.StoreProductsAsync(s.Id, p, PageSize, ct);
            var cards = await search.InStockCardsAsync(ids, ct);
            var seller = new StoreSeller(s.Id, s.Name, s.Slug, s.City, s.Rating, s.CreatedAt, total, s.DeliveredPackages, handling);
            return ApiResults.Ok(new StoreSellerResponse(seller, cards, total, p, PageSize), AppJsonContext.Default.StoreSellerResponse);
        });
    }
}
