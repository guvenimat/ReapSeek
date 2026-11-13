using Dapper;
using Npgsql;

namespace ReapSeek.Api.Catalog;

public sealed record SellerOfferRow(
    long Id, long ProductId, string ProductTitle, string ProductSlug, string ProductStatus, long VariantId, string Sku, string VariantLabel,
    decimal Price, decimal? ListPrice, int Stock, int HandlingDays, string Status, decimal? BuyBoxPrice, bool IsBuyBox, DateTime UpdatedAt);
public sealed record ProposalRow(long Id, string Slug, string Title, string Brand, string CategoryName, string Status, string? ReviewNote, DateTime CreatedAt);
public sealed record BuyBoxRecord(long VariantId, long? OfferId, decimal? Price);

public sealed class SellerCatalogRepository(NpgsqlDataSource db)
{
    public sealed record StorePageRecord(long SellerId, int Limit, int Offset);
    public sealed record StoreProductRecord(long Id, int Total, decimal HandlingDays);

    public async Task<(List<long> Ids, int Total, decimal? HandlingDays)> ProductsAsync(long sellerId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = (await conn.QueryAsync<StoreProductRecord>("""
            WITH mine AS (
                SELECT v.product_id, max(p.created_at) AS created_at, avg(o.handling_days) AS handling_days
                FROM offers o JOIN product_variants v ON v.id = o.variant_id JOIN products p ON p.id = v.product_id
                WHERE o.seller_id = @SellerId AND o.status = 'active' AND o.stock > 0 AND p.status = 'active'
                GROUP BY v.product_id)
            SELECT product_id AS id, (count(*) OVER ())::int AS total, (avg(handling_days) OVER ())::numeric(6, 1) AS handling_days
            FROM mine ORDER BY created_at DESC, product_id DESC LIMIT @Limit OFFSET @Offset
            """, new StorePageRecord(sellerId, pageSize, (page - 1) * pageSize))).AsList();
        return (rows.Select(r => r.Id).ToList(), rows.Count > 0 ? rows[0].Total : 0, rows.Count > 0 ? rows[0].HandlingDays : null);
    }

    const string BuyBoxLateral = """
        LEFT JOIN LATERAL (
            SELECT bo.id, bo.price FROM offers bo JOIN seller_snapshots bs ON bs.id = bo.seller_id
            WHERE bo.variant_id = v.id AND bo.status = 'active' AND bo.stock > 0 AND bs.status = 'active'
            ORDER BY bo.price, bs.rating DESC, bo.handling_days, bo.id LIMIT 1
        ) bb ON true
        """;

    public async Task<(List<SellerOfferRow> Rows, long Total)> OffersAsync(long sellerId, string? q, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new { SellerId = sellerId, Q = q is null ? null : $"%{q}%", Limit = pageSize, Offset = (page - 1) * pageSize };
        const string Where = """
            FROM offers o JOIN product_variants v ON v.id = o.variant_id JOIN products p ON p.id = v.product_id
            WHERE o.seller_id = @SellerId AND (@Q::text IS NULL OR p.title ILIKE @Q OR v.sku ILIKE @Q)
            """;
        var total = await conn.ExecuteScalarAsync<long>("SELECT count(*) " + Where, args);
        var rows = (await conn.QueryAsync<SellerOfferRow>($"""
            WITH page AS (SELECT o.id {Where} ORDER BY o.updated_at DESC, o.id DESC LIMIT @Limit OFFSET @Offset)
            SELECT o.id, p.id AS product_id, p.title AS product_title, p.slug AS product_slug, p.status AS product_status, v.id AS variant_id, v.sku,
                coalesce((SELECT string_agg(e.value, ' / ' ORDER BY e.key) FROM jsonb_each_text(v.options) e), '') AS variant_label,
                o.price, o.list_price, o.stock, o.handling_days, o.status, CASE WHEN p.status = 'active' THEN bb.price END AS buy_box_price, coalesce(p.status = 'active' AND bb.id = o.id, false) AS is_buy_box, o.updated_at
            FROM page JOIN offers o ON o.id = page.id JOIN product_variants v ON v.id = o.variant_id JOIN products p ON p.id = v.product_id
            {BuyBoxLateral}
            ORDER BY o.updated_at DESC, o.id DESC
            """, args)).AsList();
        return (rows, total);
    }

    public async Task<List<BuyBoxRecord>> BuyBoxAsync(long productId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<BuyBoxRecord>($"""
            SELECT v.id AS variant_id, bb.id AS offer_id, bb.price FROM product_variants v {BuyBoxLateral}
            WHERE v.product_id = @ProductId ORDER BY v.position
            """, new { ProductId = productId })).AsList();
    }

    public async Task<long?> SubmitterAsync(long productId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>("SELECT submitted_by_seller_id FROM products WHERE id = @Id", new { Id = productId });
    }

    public async Task MarkSubmittedAsync(long productId, long sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("""
            UPDATE products SET status = 'pending_review', submitted_by_seller_id = @SellerId, review_note = NULL, updated_at = now() WHERE id = @Id
            """, new { Id = productId, SellerId = sellerId });
    }

    public async Task SetReviewNoteAsync(long productId, string? note, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("UPDATE products SET review_note = @Note WHERE id = @Id", new { Id = productId, Note = note });
    }

    public async Task<List<ProposalRow>> ProposalsAsync(long sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<ProposalRow>("""
            SELECT p.id, p.slug, p.title, b.name AS brand, c.name AS category_name, p.status, p.review_note, p.created_at
            FROM products p JOIN brands b ON b.id = p.brand_id JOIN categories c ON c.id = p.category_id
            WHERE p.submitted_by_seller_id = @SellerId ORDER BY p.created_at DESC, p.id DESC
            """, new { SellerId = sellerId })).AsList();
    }

}

public sealed class SellerCatalog(SellerCatalogRepository repository)
{
    public Task<(List<SellerOfferRow> Rows, long Total)> OffersAsync(long sellerId, string? q, int page, int pageSize, CancellationToken ct) =>
        repository.OffersAsync(sellerId, q, page, pageSize, ct);

    public Task<List<BuyBoxRecord>> BuyBoxAsync(long productId, CancellationToken ct) => repository.BuyBoxAsync(productId, ct);

    public Task<long?> SubmitterAsync(long productId, CancellationToken ct) => repository.SubmitterAsync(productId, ct);

    public Task MarkSubmittedAsync(long productId, long sellerId, CancellationToken ct) => repository.MarkSubmittedAsync(productId, sellerId, ct);

    public Task SetReviewNoteAsync(long productId, string? note, CancellationToken ct) => repository.SetReviewNoteAsync(productId, note, ct);

    public Task<(List<long> Ids, int Total, decimal? HandlingDays)> StoreProductsAsync(long sellerId, int page, int pageSize, CancellationToken ct) =>
        repository.ProductsAsync(sellerId, page, pageSize, ct);

    public Task<List<ProposalRow>> ProposalsAsync(long sellerId, CancellationToken ct) => repository.ProposalsAsync(sellerId, ct);
}
