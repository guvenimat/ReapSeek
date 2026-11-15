using Dapper;
using Npgsql;
using ReapSeek.Modules.Catalog;

namespace ReapSeek.Engagement.Infrastructure;

internal sealed class WishlistRepository(NpgsqlDataSource db)
{
    public const int MaxItems = 200;

    internal sealed record ItemRecord(long CustomerId, long ProductId);
    internal sealed record InsertRecord(long CustomerId, long ProductId, decimal? AddedPrice);
    internal sealed record CustomerRecord(long CustomerId, int Limit);
    internal sealed record RowRecord(long ProductId, decimal? AddedPrice, DateTime CreatedAt);

    public async Task<List<RowRecord>> ListAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<RowRecord>("""
            SELECT product_id, added_price, created_at FROM wishlist_items WHERE customer_id = @CustomerId ORDER BY created_at DESC LIMIT @Limit
            """, new CustomerRecord(customerId, MaxItems))).AsList();
    }

    public async Task<List<long>> IdsAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<long>("SELECT product_id FROM wishlist_items WHERE customer_id = @CustomerId ORDER BY created_at DESC LIMIT @Limit",
            new CustomerRecord(customerId, MaxItems))).AsList();
    }

    public async Task<bool?> AddAsync(long customerId, long productId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new ItemRecord(customerId, productId);
        if (!await CatalogLookups.IsActiveAsync(conn, productId)) return null;
        if (await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM wishlist_items WHERE customer_id = @CustomerId", args) >= MaxItems) return false;
        await conn.ExecuteAsync("""
            INSERT INTO wishlist_items (customer_id, product_id, added_price)
            VALUES (@CustomerId, @ProductId, @AddedPrice)
            ON CONFLICT (customer_id, product_id) DO NOTHING
            """, new InsertRecord(customerId, productId, (await CatalogLookups.LowestPricesAsync(conn, [productId])).TryGetValue(productId, out var price) ? price : null));
        return true;
    }

    public async Task RemoveAsync(long customerId, long productId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("DELETE FROM wishlist_items WHERE customer_id = @CustomerId AND product_id = @ProductId", new ItemRecord(customerId, productId));
    }
}
