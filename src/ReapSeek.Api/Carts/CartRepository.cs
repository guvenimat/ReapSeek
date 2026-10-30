using Dapper;
using Npgsql;
using ReapSeek.Domain.Carts;
using ReapSeek.Domain.Common;

namespace ReapSeek.Api.Carts;

public sealed record CartOwner(long? CustomerId, string? GuestToken);

public sealed class CartRepository(NpgsqlDataSource db)
{
    public sealed record CartRecord(Guid Id, long? CustomerId, string? GuestToken);
    public sealed record LineRecord(long OfferId, int Quantity, decimal AddedPrice, DateTime AddedAt);
    public sealed record OfferRecord(
        long OfferId, decimal Price, decimal? ListPrice, int Stock, string OfferStatus, int HandlingDays,
        long SellerId, string SellerName, string SellerStatus, decimal SellerRating,
        long ProductId, string ProductSlug, string ProductTitle, string ProductStatus, string Brand,
        long VariantId, string Sku, string Options, string? ImageUrl);

    const string OfferSelect = """
        SELECT o.id AS offer_id, o.price, o.list_price, o.stock, o.status AS offer_status, o.handling_days,
            s.id AS seller_id, s.name AS seller_name, s.status AS seller_status, s.rating AS seller_rating,
            p.id AS product_id, p.slug AS product_slug, p.title AS product_title, p.status AS product_status, b.name AS brand,
            v.id AS variant_id, v.sku, v.options::text AS options,
            COALESCE(
                (SELECT i.url FROM product_images i WHERE i.product_id = p.id AND i.color = v.options->>'Color' ORDER BY i.position LIMIT 1),
                (SELECT i.url FROM product_images i WHERE i.product_id = p.id ORDER BY i.position LIMIT 1)) AS image_url
        FROM offers o
        JOIN sellers s ON s.id = o.seller_id
        JOIN product_variants v ON v.id = o.variant_id
        JOIN products p ON p.id = v.product_id
        JOIN brands b ON b.id = p.brand_id
        """;

    public async Task<Cart?> FindAsync(CartOwner owner, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await FindAsync(conn, null, owner);
    }

    public static async Task<Cart?> FindAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, CartOwner owner)
    {
        CartRecord? cart = owner.CustomerId is { } id
            ? await conn.QuerySingleOrDefaultAsync<CartRecord>("SELECT id, customer_id, guest_token FROM carts WHERE customer_id = @Id", new { Id = id }, tx)
            : owner.GuestToken is { } token
                ? await conn.QuerySingleOrDefaultAsync<CartRecord>("SELECT id, customer_id, guest_token FROM carts WHERE guest_token = @Token", new { Token = token }, tx)
                : null;
        if (cart is null) return null;
        var lines = await conn.QueryAsync<LineRecord>(
            "SELECT offer_id, quantity, added_price, added_at FROM cart_lines WHERE cart_id = @Id ORDER BY added_at, offer_id", new { cart.Id }, tx);
        return Cart.Restore(cart.Id, cart.CustomerId, cart.GuestToken,
            lines.Select(l => new CartLine(l.OfferId, l.Quantity, Money.Of(l.AddedPrice), l.AddedAt)));
    }

    public async Task SaveAsync(Cart cart, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SaveAsync(conn, tx, cart);
        await tx.CommitAsync(ct);
    }

    public static async Task SaveAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Cart cart)
    {
        await conn.ExecuteAsync("""
            INSERT INTO carts (id, customer_id, guest_token) VALUES (@Id, @CustomerId, @GuestToken)
            ON CONFLICT (id) DO UPDATE SET updated_at = now()
            """, new { cart.Id, cart.CustomerId, cart.GuestToken }, tx);
        await conn.ExecuteAsync("DELETE FROM cart_lines WHERE cart_id = @Id", new { cart.Id }, tx);
        await conn.ExecuteAsync("""
            INSERT INTO cart_lines (cart_id, offer_id, quantity, added_price, added_at)
            SELECT @Id, l.offer_id, l.quantity, l.added_price, l.added_at
            FROM unnest(@OfferIds, @Quantities, @Prices, @AddedAt) AS l(offer_id, quantity, added_price, added_at)
            """, new
        {
            cart.Id,
            OfferIds = cart.Lines.Select(l => l.OfferId).ToArray(),
            Quantities = cart.Lines.Select(l => l.Quantity).ToArray(),
            Prices = cart.Lines.Select(l => l.AddedPrice.Amount).ToArray(),
            AddedAt = cart.Lines.Select(l => l.AddedAt).ToArray(),
        }, tx);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("DELETE FROM carts WHERE id = @Id", new { Id = id });
    }

    public async Task<List<OfferRecord>> OffersAsync(long[] offerIds, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await OffersAsync(conn, null, offerIds, false);
    }

    public static async Task<List<OfferRecord>> OffersAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long[] offerIds, bool forUpdate) =>
        (forUpdate
            ? await conn.QueryAsync<OfferRecord>(OfferSelect + " WHERE o.id = ANY(@Ids) ORDER BY o.id FOR UPDATE OF o", new { Ids = offerIds }, tx)
            : await conn.QueryAsync<OfferRecord>(OfferSelect + " WHERE o.id = ANY(@Ids) ORDER BY o.id", new { Ids = offerIds }, tx)).AsList();
}
