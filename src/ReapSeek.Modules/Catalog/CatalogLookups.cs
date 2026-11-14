using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Catalog;

public sealed record ProductInfo(long Id, string Title, string Slug);
public sealed record ProductIdRecord(long ProductId);
public sealed record ProductIdsRecord(long[] ProductIds);
public sealed record LowestPriceRecord(long ProductId, decimal Price);

public static class CatalogLookups
{
    public static Task<ProductInfo?> ProductAsync(NpgsqlConnection conn, long productId) =>
        conn.QuerySingleOrDefaultAsync<ProductInfo>("SELECT id, title, slug FROM catalog.products WHERE id = @ProductId", new ProductIdRecord(productId));

    public static async Task<Dictionary<long, ProductInfo>> ProductsAsync(NpgsqlConnection conn, IEnumerable<long> productIds)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<ProductInfo>("SELECT id, title, slug FROM catalog.products WHERE id = ANY(@ProductIds)", new ProductIdsRecord(ids)))
            .ToDictionary(p => p.Id);
    }

    public static async Task<List<long>> ActiveSellersOfProductAsync(NpgsqlConnection conn, long productId) =>
        (await conn.QueryAsync<long>("""
            SELECT DISTINCT o.seller_id FROM catalog.offers o JOIN catalog.product_variants v ON v.id = o.variant_id
            WHERE v.product_id = @ProductId AND o.status = 'active'
            """, new ProductIdRecord(productId))).AsList();

    public static async Task<Dictionary<long, decimal>> LowestPricesAsync(NpgsqlConnection conn, IEnumerable<long> productIds, NpgsqlTransaction? tx = null)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<LowestPriceRecord>("""
            SELECT v.product_id, min(o.price) AS price
            FROM catalog.offers o
            JOIN catalog.product_variants v ON v.id = o.variant_id
            JOIN catalog.products p ON p.id = v.product_id AND p.status = 'active'
            JOIN catalog.seller_snapshots s ON s.id = o.seller_id AND s.status = 'active'
            WHERE v.product_id = ANY(@ProductIds) AND o.status = 'active' AND o.stock > 0
            GROUP BY v.product_id
            """, new ProductIdsRecord(ids), tx)).ToDictionary(r => r.ProductId, r => r.Price);
    }

    public static Task<bool> IsActiveAsync(NpgsqlConnection conn, long productId) =>
        conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM catalog.products WHERE id = @ProductId AND status = 'active')", new ProductIdRecord(productId));
}
