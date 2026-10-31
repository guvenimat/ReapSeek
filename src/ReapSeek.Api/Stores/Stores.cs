using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Search;
using ReapSeek.Shared;

namespace ReapSeek.Api.Stores;

public sealed record StoreSeller(long Id, string Name, string Slug, string City, decimal Rating, DateTime JoinedAt, int ProductCount,
    int DeliveredPackages, decimal? AverageHandlingDays);
public sealed record StoreSellerResponse(StoreSeller Seller, List<ProductCard> Products, int Total, int Page, int PageSize);

public sealed class StoreRepository(NpgsqlDataSource db)
{
    public sealed record SlugRecord(string Slug);
    public sealed record PageRecord(long SellerId, int Limit, int Offset);
    public sealed record SellerRecord(long Id, string Name, string Slug, string City, decimal Rating, DateTime CreatedAt, int DeliveredPackages);
    public sealed record ProductRowRecord(long Id, int Total, decimal HandlingDays);

    public async Task<SellerRecord?> SellerAsync(string slug, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SellerRecord>("""
            SELECT s.id, s.name, s.slug, s.city, s.rating, s.created_at,
                (SELECT count(*)::int FROM order_packages p WHERE p.seller_id = s.id AND p.status = 'delivered') AS delivered_packages
            FROM sellers s WHERE s.slug = @Slug AND s.status = 'active'
            """, new SlugRecord(slug));
    }

    public async Task<(List<long> Ids, int Total, decimal? HandlingDays)> ProductsAsync(long sellerId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = (await conn.QueryAsync<ProductRowRecord>("""
            WITH mine AS (
                SELECT v.product_id, max(p.created_at) AS created_at, avg(o.handling_days) AS handling_days
                FROM offers o JOIN product_variants v ON v.id = o.variant_id JOIN products p ON p.id = v.product_id
                WHERE o.seller_id = @SellerId AND o.status = 'active' AND o.stock > 0 AND p.status = 'active'
                GROUP BY v.product_id)
            SELECT product_id AS id, (count(*) OVER ())::int AS total, (avg(handling_days) OVER ())::numeric(6, 1) AS handling_days
            FROM mine ORDER BY created_at DESC, product_id DESC LIMIT @Limit OFFSET @Offset
            """, new PageRecord(sellerId, pageSize, (page - 1) * pageSize))).AsList();
        return (rows.Select(r => r.Id).ToList(), rows.Count > 0 ? rows[0].Total : 0, rows.Count > 0 ? rows[0].HandlingDays : null);
    }
}

public static class StoreEndpoints
{
    const int PageSize = 24;

    public static void MapStores(this RouteGroupBuilder api)
    {
        api.MapGet("/catalog/sellers/{slug}", async (string slug, int? page, StoreRepository stores, SearchService search, CancellationToken ct) =>
        {
            var p = Ensure.InRange(page ?? 1, 1, 1000, "page");
            if (slug.Length > 100 || await stores.SellerAsync(slug, ct) is not { } s) return ApiResults.NotFound("Seller not found");
            var (ids, total, handling) = await stores.ProductsAsync(s.Id, p, PageSize, ct);
            var cards = await search.InStockCardsAsync(ids, ct);
            var seller = new StoreSeller(s.Id, s.Name, s.Slug, s.City, s.Rating, s.CreatedAt, total, s.DeliveredPackages, handling);
            return ApiResults.Ok(new StoreSellerResponse(seller, cards, total, p, PageSize), AppJsonContext.Default.StoreSellerResponse);
        });
    }
}
