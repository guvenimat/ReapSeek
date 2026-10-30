using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Products;

public sealed class ProductRepository(NpgsqlDataSource db)
{
    public sealed record ProductRecord(
        long Id, string Slug, string Title, string Brand, string BrandSlug, string Description, string Attributes, string CategoryPath, DateTime UpdatedAt);
    public sealed record VariantRecord(long Id, string Sku, string Options);
    public sealed record OfferRecord(
        long Id, long VariantId, long SellerId, string SellerName, string SellerSlug, decimal SellerRating, string SellerCity,
        decimal Price, decimal? ListPrice, int Stock, int HandlingDays, bool IsBuyBox);
    public sealed record CrumbRecord(string Name, string Slug, string Path);

    public async Task<StoreProduct?> FindAsync(string slug, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var p = await conn.QuerySingleOrDefaultAsync<ProductRecord>("""
            SELECT p.id, p.slug, p.title, b.name AS brand, b.slug AS brand_slug, p.description, p.attributes::text AS attributes,
                c.path AS category_path, p.updated_at
            FROM products p JOIN brands b ON b.id = p.brand_id JOIN categories c ON c.id = p.category_id
            WHERE p.slug = @Slug AND p.status = 'active'
            """, new { Slug = slug });
        if (p is null) return null;

        var segments = p.CategoryPath.Split('/');
        var paths = Enumerable.Range(1, segments.Length).Select(n => string.Join('/', segments.Take(n))).ToArray();
        var crumbs = (await conn.QueryAsync<CrumbRecord>(
            "SELECT name, slug, path FROM categories WHERE path = ANY(@Paths) ORDER BY depth", new { Paths = paths })).AsList();
        var images = (await conn.QueryAsync<StoreImage>(
            "SELECT url, alt, color FROM product_images WHERE product_id = @Id ORDER BY position", new { p.Id })).AsList();
        var variants = (await conn.QueryAsync<VariantRecord>(
            "SELECT id, sku, options::text AS options FROM product_variants WHERE product_id = @Id ORDER BY position, id", new { p.Id })).AsList();
        var variantIds = variants.Select(v => v.Id).ToArray();
        var offers = (await conn.QueryAsync<OfferRecord>("""
            SELECT o.id, o.variant_id, s.id AS seller_id, s.name AS seller_name, s.slug AS seller_slug, s.rating AS seller_rating,
                s.city AS seller_city, o.price, o.list_price, o.stock, o.handling_days, bb.offer_id IS NOT NULL AS is_buy_box
            FROM offers o
            JOIN sellers s ON s.id = o.seller_id AND s.status = 'active'
            LEFT JOIN (SELECT offer_id FROM buy_box WHERE variant_id = ANY(@VariantIds)) bb ON bb.offer_id = o.id
            WHERE o.variant_id = ANY(@VariantIds) AND o.status = 'active' AND o.stock > 0
            ORDER BY o.price, s.rating DESC, o.handling_days, o.id
            """, new { VariantIds = variantIds })).AsList();

        var storeVariants = variants.Select(v => new StoreVariant(v.Id, v.Sku, Map(v.Options),
            offers.Where(o => o.VariantId == v.Id).Select(o => new StoreOffer(o.Id, o.SellerId, o.SellerName, o.SellerSlug, o.SellerRating, o.SellerCity,
                o.Price, ProductRules.Discount(o.Price, o.ListPrice) > 0 ? o.ListPrice : null, ProductRules.Discount(o.Price, o.ListPrice),
                ProductRules.LowStock(o.Stock), o.HandlingDays, o.IsBuyBox)).ToList())).ToList();
        var buyBoxPrices = storeVariants.SelectMany(v => v.Offers.Where(o => o.IsBuyBox).Select(o => o.Price)).ToList();

        return new StoreProduct(p.Id, p.Slug, p.Title, p.Brand, p.BrandSlug, p.Description, Map(p.Attributes),
            crumbs.Select(c => new CategoryCrumb(c.Name, c.Slug, c.Path)).ToList(), images,
            ProductRules.Axes(storeVariants.Select(v => v.Options)), storeVariants,
            buyBoxPrices.Count > 0 ? buyBoxPrices.Min() : null, buyBoxPrices.Count > 0 ? buyBoxPrices.Max() : null,
            offers.Select(o => o.SellerId).Distinct().Count(), p.UpdatedAt);
    }

    public async Task<SitemapResponse> SitemapAsync(int limit, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var products = (await conn.QueryAsync<SitemapProduct>(
            "SELECT slug, updated_at FROM products WHERE status = 'active' ORDER BY id LIMIT @Limit", new { Limit = limit })).AsList();
        var categories = (await conn.QueryAsync<string>("SELECT slug FROM categories ORDER BY depth, position")).AsList();
        return new SitemapResponse(products, categories);
    }

    static Dictionary<string, string> Map(string json) => JsonSerializer.Deserialize(json, AppJsonContext.Default.DictionaryStringString) ?? [];
}
