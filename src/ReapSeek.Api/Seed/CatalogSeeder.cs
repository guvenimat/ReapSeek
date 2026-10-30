using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Seed;

public static class CatalogSeeder
{
    public static async Task RequestReindexAsync(NpgsqlDataSource db, string reason, ILogger log, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CatalogReindex, new CatalogReindex(reason), OutboxJsonContext.Default.CatalogReindex);
        await tx.CommitAsync(ct);
        log.LogInformation("Requested a full catalog sync");
    }

    public static async Task<bool> SeedAsync(NpgsqlDataSource db, bool reset, ILogger log, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (reset)
        {
            await conn.ExecuteAsync("TRUNCATE offers, product_variants, product_images, products, brands, categories, sellers RESTART IDENTITY CASCADE", transaction: tx);
            log.LogInformation("Catalog tables truncated");
        }
        else if (await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM sellers) OR EXISTS (SELECT 1 FROM categories)", transaction: tx))
        {
            log.LogInformation("Catalog already has data; skipping (use --reset to replace it)");
            return false;
        }

        var c = CatalogGenerator.Generate(DateTime.UtcNow);

        await conn.ExecuteAsync("""
            INSERT INTO sellers (id, name, slug, city, rating, commission_rate)
            SELECT * FROM unnest(@Ids, @Names, @Slugs, @Cities, @Ratings, @Commissions)
            """, new
        {
            Ids = c.Sellers.Select(s => s.Id).ToArray(),
            Names = c.Sellers.Select(s => s.Name).ToArray(),
            Slugs = c.Sellers.Select(s => s.Slug).ToArray(),
            Cities = c.Sellers.Select(s => s.City).ToArray(),
            Ratings = c.Sellers.Select(s => s.Rating).ToArray(),
            Commissions = c.Sellers.Select(s => s.Commission).ToArray(),
        }, tx);

        await conn.ExecuteAsync("""
            INSERT INTO categories (id, parent_id, slug, name, path, depth, position, image_kind)
            SELECT * FROM unnest(@Ids, @ParentIds, @Slugs, @Names, @Paths, @Depths, @Positions, @Kinds)
            """, new
        {
            Ids = c.Categories.Select(x => x.Id).ToArray(),
            ParentIds = c.Categories.Select(x => x.ParentId).ToArray(),
            Slugs = c.Categories.Select(x => x.Slug).ToArray(),
            Names = c.Categories.Select(x => x.Name).ToArray(),
            Paths = c.Categories.Select(x => x.Path).ToArray(),
            Depths = c.Categories.Select(x => x.Depth).ToArray(),
            Positions = c.Categories.Select(x => x.Position).ToArray(),
            Kinds = c.Categories.Select(x => x.ImageKind).ToArray(),
        }, tx);

        await conn.ExecuteAsync("""
            INSERT INTO brands (id, name, slug) SELECT * FROM unnest(@Ids, @Names, @Slugs)
            """, new { Ids = c.Brands.Select(b => b.Id).ToArray(), Names = c.Brands.Select(b => b.Name).ToArray(), Slugs = c.Brands.Select(b => b.Slug).ToArray() }, tx);

        await conn.ExecuteAsync("""
            INSERT INTO products (id, slug, title, brand_id, category_id, description, attributes, status, created_at, updated_at)
            SELECT p.id, p.slug, p.title, p.brand_id, p.category_id, p.description, p.attributes::jsonb, p.status, p.created_at, p.created_at
            FROM unnest(@Ids, @Slugs, @Titles, @BrandIds, @CategoryIds, @Descriptions, @Attributes, @Statuses, @CreatedAt)
                AS p(id, slug, title, brand_id, category_id, description, attributes, status, created_at)
            """, new
        {
            Ids = c.Products.Select(p => p.Id).ToArray(),
            Slugs = c.Products.Select(p => p.Slug).ToArray(),
            Titles = c.Products.Select(p => p.Title).ToArray(),
            BrandIds = c.Products.Select(p => p.BrandId).ToArray(),
            CategoryIds = c.Products.Select(p => p.CategoryId).ToArray(),
            Descriptions = c.Products.Select(p => p.Description).ToArray(),
            Attributes = c.Products.Select(p => p.AttributesJson).ToArray(),
            Statuses = c.Products.Select(p => p.Status).ToArray(),
            CreatedAt = c.Products.Select(p => p.CreatedAt).ToArray(),
        }, tx);

        await conn.ExecuteAsync("""
            INSERT INTO product_images (product_id, position, url, alt) SELECT * FROM unnest(@ProductIds, @Positions, @Urls, @Alts)
            """, new
        {
            ProductIds = c.Images.Select(i => i.ProductId).ToArray(),
            Positions = c.Images.Select(i => i.Position).ToArray(),
            Urls = c.Images.Select(i => i.Url).ToArray(),
            Alts = c.Images.Select(i => i.Alt).ToArray(),
        }, tx);

        await conn.ExecuteAsync("""
            INSERT INTO product_variants (id, product_id, sku, options, position)
            SELECT v.id, v.product_id, v.sku, v.options::jsonb, v.position
            FROM unnest(@Ids, @ProductIds, @Skus, @Options, @Positions) AS v(id, product_id, sku, options, position)
            """, new
        {
            Ids = c.Variants.Select(v => v.Id).ToArray(),
            ProductIds = c.Variants.Select(v => v.ProductId).ToArray(),
            Skus = c.Variants.Select(v => v.Sku).ToArray(),
            Options = c.Variants.Select(v => v.OptionsJson).ToArray(),
            Positions = c.Variants.Select(v => v.Position).ToArray(),
        }, tx);

        await conn.ExecuteAsync("""
            INSERT INTO offers (seller_id, variant_id, price, list_price, stock, handling_days, status)
            SELECT * FROM unnest(@SellerIds, @VariantIds, @Prices, @ListPrices, @Stocks, @Handling, @Statuses)
            """, new
        {
            SellerIds = c.Offers.Select(o => o.SellerId).ToArray(),
            VariantIds = c.Offers.Select(o => o.VariantId).ToArray(),
            Prices = c.Offers.Select(o => o.Price).ToArray(),
            ListPrices = c.Offers.Select(o => o.ListPrice).ToArray(),
            Stocks = c.Offers.Select(o => o.Stock).ToArray(),
            Handling = c.Offers.Select(o => o.HandlingDays).ToArray(),
            Statuses = c.Offers.Select(o => o.Status).ToArray(),
        }, tx);

        await conn.ExecuteAsync("""
            SELECT setval(pg_get_serial_sequence('sellers', 'id'), (SELECT max(id) FROM sellers));
            SELECT setval(pg_get_serial_sequence('categories', 'id'), (SELECT max(id) FROM categories));
            SELECT setval(pg_get_serial_sequence('brands', 'id'), (SELECT max(id) FROM brands));
            SELECT setval(pg_get_serial_sequence('products', 'id'), (SELECT max(id) FROM products));
            SELECT setval(pg_get_serial_sequence('product_variants', 'id'), (SELECT max(id) FROM product_variants));
            """, transaction: tx);

        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CatalogReindex, new CatalogReindex("seed"), OutboxJsonContext.Default.CatalogReindex);
        await tx.CommitAsync(ct);
        await conn.ExecuteAsync("ANALYZE sellers, categories, brands, products, product_images, product_variants, offers");
        log.LogInformation("Seeded {Sellers} sellers, {Categories} categories, {Brands} brands, {Products} products, {Variants} variants, {Offers} offers",
            c.Sellers.Count, c.Categories.Count, c.Brands.Count, c.Products.Count, c.Variants.Count, c.Offers.Count);
        return true;
    }
}
