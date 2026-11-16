using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Catalog;

public sealed record OfferView(
    long OfferId, decimal Price, decimal? ListPrice, int Stock, string OfferStatus, int HandlingDays,
    long SellerId, string SellerName, string SellerStatus, decimal SellerRating,
    long ProductId, string ProductSlug, string ProductTitle, string ProductStatus, string Brand,
    long VariantId, string Sku, string Options, string? ImageUrl);
public sealed record OfferIdsRecord(long[] Ids);
public sealed record StockChangeRecord(long OfferId, int Quantity);

public static class Offers
{
    const string Select = """
        SELECT o.id AS offer_id, o.price, o.list_price, o.stock, o.status AS offer_status, o.handling_days,
            s.id AS seller_id, s.name AS seller_name, s.status AS seller_status, s.rating AS seller_rating,
            p.id AS product_id, p.slug AS product_slug, p.title AS product_title, p.status AS product_status, b.name AS brand,
            v.id AS variant_id, v.sku, v.options::text AS options,
            COALESCE(
                (SELECT i.url FROM catalog.product_images i WHERE i.product_id = p.id AND i.color = v.options->>'Color' ORDER BY i.position LIMIT 1),
                (SELECT i.url FROM catalog.product_images i WHERE i.product_id = p.id ORDER BY i.position LIMIT 1)) AS image_url
        FROM catalog.offers o
        JOIN catalog.seller_snapshots s ON s.id = o.seller_id
        JOIN catalog.product_variants v ON v.id = o.variant_id
        JOIN catalog.products p ON p.id = v.product_id
        JOIN catalog.brands b ON b.id = p.brand_id
        """;

    public static async Task<List<OfferView>> LoadAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long[] offerIds, bool forUpdate) =>
        (await conn.QueryAsync<OfferView>(Select + " WHERE o.id = ANY(@Ids) ORDER BY o.id" + (forUpdate ? " FOR UPDATE OF o" : ""), new OfferIdsRecord(offerIds), tx)).AsList();

    public static async Task<bool> TryReserveAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long offerId, int quantity) =>
        await conn.ExecuteAsync("""
            UPDATE catalog.offers SET stock = stock - @Quantity, updated_at = now()
            WHERE id = @OfferId AND status = 'active' AND stock >= @Quantity
            """, new StockChangeRecord(offerId, quantity), tx) == 1;

    public static Task ReleaseAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long offerId, int quantity) =>
        conn.ExecuteAsync("UPDATE catalog.offers SET stock = stock + @Quantity, updated_at = now() WHERE id = @OfferId", new StockChangeRecord(offerId, quantity), tx);
}
