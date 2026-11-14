using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;
namespace ReapSeek.Worker.Catalog.CatalogSync;

public sealed class CatalogSyncRepository(NpgsqlDataSource db)
{
    public sealed record ProductRecord(
        long Id, string Slug, string Title, string Status, string Category, string Department, string Brand, decimal? Price, string? ImageUrl);

    public async Task EnqueueSyncAsync(ReapDeepCatalogMessage message, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await OutboxWriter.EnqueueAsync(conn, null, OutboxTypes.ReapDeepCatalog, message, OutboxJsonContext.Default.ReapDeepCatalogMessage);
    }

    public async Task<List<long>> AllProductIdsAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<long>("SELECT id FROM products ORDER BY id")).AsList();
    }

    public async Task<List<ProductRecord>> LoadAsync(long[] ids, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<ProductRecord>("""
            SELECT p.id, p.slug, p.title, p.status, c.slug AS category, d.slug AS department, b.name AS brand, bb.price,
                (SELECT i.url FROM product_images i WHERE i.product_id = p.id ORDER BY i.position LIMIT 1) AS image_url
            FROM products p
            JOIN categories c ON c.id = p.category_id
            JOIN categories d ON d.path = split_part(c.path, '/', 1)
            JOIN brands b ON b.id = p.brand_id
            LEFT JOIN LATERAL (
                SELECT o.price
                FROM product_variants v
                JOIN offers o ON o.variant_id = v.id AND o.status = 'active' AND o.stock > 0
                JOIN seller_snapshots s ON s.id = o.seller_id AND s.status = 'active'
                WHERE v.product_id = p.id
                ORDER BY o.price, s.rating DESC, o.handling_days, o.id
                LIMIT 1
            ) bb ON true
            WHERE p.id = ANY(@Ids)
            """, new { Ids = ids })).AsList();
    }
}
