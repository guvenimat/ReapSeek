using Dapper;
using Npgsql;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Search;

namespace ReapSeek.Api.Wishlist;

public sealed record WishlistItem(ProductCard Product, decimal? AddedPrice, decimal? PriceDrop, DateTime AddedAt);
public sealed record WishlistResponse(List<WishlistItem> Items);
public sealed record WishlistIdsResponse(List<long> ProductIds);
public sealed record WishlistRequest(long? ProductId);

public sealed class WishlistRepository(NpgsqlDataSource db)
{
    public const int MaxItems = 200;

    public sealed record ItemRecord(long CustomerId, long ProductId);
    public sealed record CustomerRecord(long CustomerId, int Limit);
    public sealed record RowRecord(long ProductId, decimal? AddedPrice, DateTime CreatedAt);

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
        if (await conn.ExecuteScalarAsync<long?>("SELECT id FROM products WHERE id = @ProductId AND status = 'active'", args) is null) return null;
        if (await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM wishlist_items WHERE customer_id = @CustomerId", args) >= MaxItems) return false;
        await conn.ExecuteAsync("""
            INSERT INTO wishlist_items (customer_id, product_id, added_price)
            SELECT @CustomerId, @ProductId, (
                SELECT min(o.price) FROM offers o JOIN product_variants v ON v.id = o.variant_id JOIN sellers s ON s.id = o.seller_id
                WHERE v.product_id = @ProductId AND o.status = 'active' AND o.stock > 0 AND s.status = 'active')
            ON CONFLICT (customer_id, product_id) DO NOTHING
            """, args);
        return true;
    }

    public async Task RemoveAsync(long customerId, long productId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("DELETE FROM wishlist_items WHERE customer_id = @CustomerId AND product_id = @ProductId", new ItemRecord(customerId, productId));
    }
}

public static class WishlistEndpoints
{
    public static void MapWishlist(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/wishlist").RequireCustomer();

        group.MapGet("/", async (UserInfo user, WishlistRepository wishlist, SearchService search, CancellationToken ct) =>
        {
            var rows = await wishlist.ListAsync(user.Id, ct);
            var cards = (await search.CardsAsync(rows.Select(r => r.ProductId).ToList(), false, ct)).ToDictionary(c => c.Id);
            var items = rows.Where(r => cards.ContainsKey(r.ProductId)).Select(r =>
            {
                var card = cards[r.ProductId];
                var drop = r.AddedPrice is { } added && card.InStock && card.Price is { } now && now < added ? added - now : (decimal?)null;
                return new WishlistItem(card, r.AddedPrice, drop, r.CreatedAt);
            }).ToList();
            return ApiResults.Ok(new WishlistResponse(items), AppJsonContext.Default.WishlistResponse);
        });

        group.MapGet("/ids", async (UserInfo user, WishlistRepository wishlist, CancellationToken ct) =>
            ApiResults.Ok(new WishlistIdsResponse(await wishlist.IdsAsync(user.Id, ct)), AppJsonContext.Default.WishlistIdsResponse));

        group.MapPost("/", async (WishlistRequest req, UserInfo user, WishlistRepository wishlist, CancellationToken ct) =>
        {
            if (req.ProductId is not > 0) return ApiResults.BadRequest("product_id is required");
            return await wishlist.AddAsync(user.Id, req.ProductId.Value, ct) switch
            {
                null => ApiResults.NotFound("Product not found"),
                false => ApiResults.Conflict($"Your wishlist can hold at most {WishlistRepository.MaxItems} products"),
                _ => ApiResults.Ok(new WishlistIdsResponse(await wishlist.IdsAsync(user.Id, ct)), AppJsonContext.Default.WishlistIdsResponse),
            };
        });

        group.MapDelete("/{productId:long}", async (long productId, UserInfo user, WishlistRepository wishlist, CancellationToken ct) =>
        {
            await wishlist.RemoveAsync(user.Id, productId, ct);
            return ApiResults.Ok(new WishlistIdsResponse(await wishlist.IdsAsync(user.Id, ct)), AppJsonContext.Default.WishlistIdsResponse);
        });
    }
}
