using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Search;

namespace ReapSeek.Worker.Search;

public sealed class SearchRepository(NpgsqlDataSource db)
{
    public sealed record DocumentRecord(
        long Id, string Slug, string Title, string Description, string Status, string Brand, string BrandSlug,
        string Department, string DepartmentName, string CategoryPath, string CategoryName, string Attributes,
        string? Colors, string? Sizes, string? Options, decimal? Price, decimal? ListPrice, int SellerCount, string? ImageUrl, string? ColorImages, DateTime CreatedAt, double? Rating, int ReviewCount);

    public async Task<List<long>> ActiveProductIdsAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<long>("SELECT id FROM products WHERE status = 'active' ORDER BY id")).AsList();
    }

    public async Task<List<DocumentRecord>> LoadAsync(long[] ids, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<DocumentRecord>("""
            SELECT p.id, p.slug, p.title, p.description, p.status, b.name AS brand, b.slug AS brand_slug,
                d.slug AS department, d.name AS department_name, c.path AS category_path, c.name AS category_name,
                p.attributes::text AS attributes,
                (SELECT string_agg(DISTINCT v.options->>'Color', '|') FROM product_variants v WHERE v.product_id = p.id) AS colors,
                (SELECT string_agg(DISTINCT v.options->>'Size', '|') FROM product_variants v WHERE v.product_id = p.id) AS sizes,
                (SELECT string_agg(DISTINCT kv.key || ': ' || kv.value, '|')
                 FROM product_variants v, jsonb_each_text(v.options) kv
                 WHERE v.product_id = p.id AND kv.key NOT IN ('Color', 'Size')) AS options,
                bb.price, bb.list_price,
                (SELECT count(DISTINCT o.seller_id)::int FROM product_variants v
                 JOIN offers o ON o.variant_id = v.id AND o.status = 'active' AND o.stock > 0
                 JOIN sellers s ON s.id = o.seller_id AND s.status = 'active'
                 WHERE v.product_id = p.id) AS seller_count,
                (SELECT i.url FROM product_images i WHERE i.product_id = p.id ORDER BY i.position LIMIT 1) AS image_url,
                (SELECT string_agg(i.color || '=' || i.url, '|' ORDER BY i.position) FROM product_images i
                 WHERE i.product_id = p.id AND i.color IS NOT NULL) AS color_images,
                p.created_at,
                (SELECT round(avg(r.rating), 1)::float8 FROM reviews r WHERE r.product_id = p.id AND r.status = 'visible') AS rating,
                (SELECT count(*)::int FROM reviews r WHERE r.product_id = p.id AND r.status = 'visible') AS review_count
            FROM products p
            JOIN brands b ON b.id = p.brand_id
            JOIN categories c ON c.id = p.category_id
            JOIN categories d ON d.path = split_part(c.path, '/', 1)
            LEFT JOIN LATERAL (
                SELECT o.price, o.list_price
                FROM product_variants v
                JOIN offers o ON o.variant_id = v.id AND o.status = 'active' AND o.stock > 0
                JOIN sellers s ON s.id = o.seller_id AND s.status = 'active'
                WHERE v.product_id = p.id
                ORDER BY o.price, s.rating DESC, o.handling_days, o.id
                LIMIT 1
            ) bb ON true
            WHERE p.id = ANY(@Ids)
            """, new { Ids = ids })).AsList();
    }

    public static ProductDocument ToDocument(DocumentRecord r)
    {
        var segments = r.CategoryPath.Split('/');
        var paths = Enumerable.Range(1, segments.Length).Select(n => string.Join('/', segments.Take(n))).ToList();
        var discount = r.Price is { } p && r.ListPrice is { } lp && lp > p ? (int)Math.Round((1 - p / lp) * 100, MidpointRounding.AwayFromZero) : 0;
        return new ProductDocument(r.Id, r.Slug, r.Title, r.Description, r.Brand, r.BrandSlug, r.Department, r.DepartmentName,
            r.CategoryPath, paths, r.CategoryName, Split(r.Colors), Split(r.Sizes), Split(r.Options),
            r.Price, discount > 0 ? r.ListPrice : null, discount, r.Price is not null, r.SellerCount, r.ImageUrl, r.CreatedAt,
            ColorImages(r.ColorImages),
            JsonSerializer.Deserialize(r.Attributes, WorkerJsonContext.Default.DictionaryStringString) ?? [], r.Rating, r.ReviewCount);
    }

    static Dictionary<string, string> ColorImages(string? joined)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Split(joined))
        {
            var i = pair.IndexOf('=');
            if (i > 0) map.TryAdd(pair[..i], pair[(i + 1)..]);
        }
        return map;
    }

    static List<string> Split(string? joined) =>
        string.IsNullOrEmpty(joined) ? [] : joined.Split('|', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList();
}
