using Dapper;
using Npgsql;
using ReapSeek.Modules.Catalog;
using ReapSeek.Domain.Carts;
using ReapSeek.Domain.Common;

namespace ReapSeek.Api.Ordering.Carts;

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
        (await Offers.LoadAsync(conn, tx, offerIds, forUpdate)).Select(o => new OfferRecord(o.OfferId, o.Price, o.ListPrice, o.Stock, o.OfferStatus, o.HandlingDays,
            o.SellerId, o.SellerName, o.SellerStatus, o.SellerRating, o.ProductId, o.ProductSlug, o.ProductTitle, o.ProductStatus, o.Brand,
            o.VariantId, o.Sku, o.Options, o.ImageUrl)).ToList();
}
