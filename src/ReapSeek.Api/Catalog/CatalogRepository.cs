using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Catalog;

public sealed record ProductFilter(string? Search, string? Status, string? CategoryPath, long? BrandId, int Page, int PageSize);

public sealed record NewProduct(
    long Id, string Slug, string Title, long BrandId, long CategoryId, string Description, string AttributesJson,
    List<(string Sku, string OptionsJson)> Variants, List<ProductImage> Images);

public sealed class CatalogRepository(NpgsqlDataSource db)
{
    public sealed record CategoryRecord(long Id, long? ParentId, string Path, int Depth, string ImageKind);
    public sealed record ProductRecord(
        long Id, string Slug, string Title, long BrandId, string Brand, long CategoryId, string CategoryName, string CategoryPath,
        string Description, string Attributes, string Status, DateTime CreatedAt, DateTime UpdatedAt);
    public sealed record VariantRecord(long Id, string Sku, string Options, int Position);

    const string UniqueViolation = "23505";
    const string ForeignKeyViolation = "23503";

    const string ProductListSelect = """
        SELECT p.id, p.slug, p.title, b.name AS brand, c.name AS category_name, c.path AS category_path, p.status,
            s.variant_count, s.seller_count, bb.price, bb.list_price,
            (SELECT i.url FROM product_images i WHERE i.product_id = p.id ORDER BY i.position LIMIT 1) AS image_url,
            p.updated_at
        FROM page
        JOIN products p ON p.id = page.id
        JOIN brands b ON b.id = p.brand_id
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN LATERAL (
            SELECT count(DISTINCT v.id)::int AS variant_count, count(DISTINCT o.seller_id)::int AS seller_count
            FROM product_variants v LEFT JOIN offers o ON o.variant_id = v.id AND o.status = 'active'
            WHERE v.product_id = p.id
        ) s ON true
        LEFT JOIN LATERAL (
            SELECT o.price, o.list_price
            FROM product_variants v
            JOIN offers o ON o.variant_id = v.id AND o.status = 'active' AND o.stock > 0
            JOIN sellers sl ON sl.id = o.seller_id AND sl.status = 'active'
            WHERE v.product_id = p.id
            ORDER BY o.price, sl.rating DESC, o.handling_days, o.id
            LIMIT 1
        ) bb ON true
        """;

    const string ProductFilterWhere = """
        WHERE (@Search::text IS NULL OR p.title ILIKE @Search OR b.name ILIKE @Search OR p.id::text = @Exact)
          AND (@Status::text IS NULL OR p.status = @Status)
          AND (@CategoryPath::text IS NULL OR c.path = @CategoryPath OR c.path LIKE @CategoryPrefix)
          AND (@BrandId::bigint IS NULL OR p.brand_id = @BrandId)
        """;

    public async Task<List<CategoryRow>> ListCategoriesAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<CategoryRow>("""
            SELECT c.id, c.parent_id, c.slug, c.name, c.path, c.depth, c.position, c.image_kind,
                (SELECT count(*) FROM products p JOIN categories pc ON pc.id = p.category_id
                 WHERE pc.path = c.path OR pc.path LIKE c.path || '/%') AS product_count
            FROM categories c ORDER BY c.depth, c.position, c.name
            """)).AsList();
    }

    public async Task<CategoryRecord?> GetCategoryAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CategoryRecord>(
            "SELECT id, parent_id, path, depth, image_kind FROM categories WHERE id = @Id", new { Id = id });
    }

    public async Task<long?> CreateCategoryAsync(long? parentId, string slug, string name, string path, int depth, string imageKind, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        try
        {
            return await conn.ExecuteScalarAsync<long>("""
                INSERT INTO categories (parent_id, slug, name, path, depth, image_kind, position)
                VALUES (@ParentId, @Slug, @Name, @Path, @Depth, @ImageKind,
                    (SELECT COALESCE(max(position) + 1, 0) FROM categories WHERE parent_id IS NOT DISTINCT FROM @ParentId))
                RETURNING id
                """, new { ParentId = parentId, Slug = slug, Name = name, Path = path, Depth = depth, ImageKind = imageKind });
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return null;
        }
    }

    public async Task<List<BrandRow>> ListBrandsAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<BrandRow>("""
            SELECT b.id, b.name, b.slug, (SELECT count(*) FROM products p WHERE p.brand_id = b.id) AS product_count
            FROM brands b ORDER BY lower(b.name)
            """)).AsList();
    }

    public async Task<BrandRow?> CreateBrandAsync(string name, string slug, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        try
        {
            return await conn.QuerySingleAsync<BrandRow>(
                "INSERT INTO brands (name, slug) VALUES (@Name, @Slug) RETURNING id, name, slug, 0::bigint AS product_count",
                new { Name = name, Slug = slug });
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return null;
        }
    }

    public async Task<VariantProductRecord?> ProductOfVariantAsync(long variantId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<VariantProductRecord>("""
            SELECT p.id, p.status, p.submitted_by_seller_id FROM product_variants v JOIN products p ON p.id = v.product_id WHERE v.id = @Id
            """, new { Id = variantId });
    }

    public async Task<bool> BrandExistsAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM brands WHERE id = @Id)", new { Id = id });
    }

    public async Task<(List<ProductListItem> Items, long Total)> ListProductsAsync(ProductFilter f, CancellationToken ct)
    {
        var search = f.Search is null ? null : "%" + f.Search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        var args = new
        {
            Search = search,
            Exact = f.Search,
            f.Status,
            f.CategoryPath,
            CategoryPrefix = f.CategoryPath is null ? null : f.CategoryPath + "/%",
            f.BrandId,
            Limit = f.PageSize,
            Offset = (f.Page - 1) * f.PageSize,
        };
        await using var conn = await db.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("""
            SELECT count(*) FROM products p JOIN brands b ON b.id = p.brand_id JOIN categories c ON c.id = p.category_id
            """ + "\n" + ProductFilterWhere, args);
        var items = await conn.QueryAsync<ProductListItem>("""
            WITH page AS (
                SELECT p.id, p.updated_at FROM products p JOIN brands b ON b.id = p.brand_id JOIN categories c ON c.id = p.category_id
            """ + "\n" + ProductFilterWhere + """

                ORDER BY p.updated_at DESC, p.id DESC
                LIMIT @Limit OFFSET @Offset
            )
            """ + "\n" + ProductListSelect + """

            ORDER BY page.updated_at DESC, page.id DESC
            """, args);
        return (items.AsList(), total);
    }

    public async Task<ProductDetail?> GetProductAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var p = await conn.QuerySingleOrDefaultAsync<ProductRecord>("""
            SELECT p.id, p.slug, p.title, p.brand_id, b.name AS brand, p.category_id, c.name AS category_name, c.path AS category_path,
                p.description, p.attributes::text AS attributes, p.status, p.created_at, p.updated_at
            FROM products p JOIN brands b ON b.id = p.brand_id JOIN categories c ON c.id = p.category_id
            WHERE p.id = @Id
            """, new { Id = id });
        if (p is null) return null;

        var images = (await conn.QueryAsync<ProductImage>(
            "SELECT position, url, alt, color FROM product_images WHERE product_id = @Id ORDER BY position", new { Id = id })).AsList();
        var variants = (await conn.QueryAsync<VariantRecord>(
            "SELECT id, sku, options::text AS options, position FROM product_variants WHERE product_id = @Id ORDER BY position, id",
            new { Id = id })).AsList();
        var variantIds = variants.Select(v => v.Id).ToArray();
        var offers = (await conn.QueryAsync<OfferRow>("""
            SELECT o.id, o.seller_id, s.name AS seller_name, s.status AS seller_status, s.rating AS seller_rating, o.variant_id,
                o.price, o.list_price, o.stock, o.handling_days, o.status, bb.offer_id IS NOT NULL AS is_buy_box, o.updated_at
            FROM offers o
            JOIN sellers s ON s.id = o.seller_id
            LEFT JOIN (SELECT offer_id FROM buy_box WHERE variant_id = ANY(@VariantIds)) bb ON bb.offer_id = o.id
            WHERE o.variant_id = ANY(@VariantIds)
            ORDER BY o.price, o.id
            """, new { VariantIds = variantIds })).AsList();

        return new ProductDetail(p.Id, p.Slug, p.Title, p.BrandId, p.Brand, p.CategoryId, p.CategoryName, p.CategoryPath,
            p.Description, ParseMap(p.Attributes), p.Status, p.CreatedAt, p.UpdatedAt, images,
            variants.Select(v => new VariantRow(v.Id, v.Sku, ParseMap(v.Options), v.Position, offers.Where(o => o.VariantId == v.Id).ToList())).ToList());
    }

    public async Task<long> NextProductIdAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>("SELECT nextval(pg_get_serial_sequence('products', 'id'))");
    }

    public async Task InsertProductAsync(NewProduct product, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO products (id, slug, title, brand_id, category_id, description, attributes, status)
            VALUES (@Id, @Slug, @Title, @BrandId, @CategoryId, @Description, @Attributes::jsonb, 'draft')
            """, new { product.Id, product.Slug, product.Title, product.BrandId, product.CategoryId, product.Description, Attributes = product.AttributesJson }, tx);
        await conn.ExecuteAsync("""
            INSERT INTO product_variants (product_id, sku, options, position)
            SELECT @ProductId, v.sku, v.options::jsonb, v.position - 1
            FROM unnest(@Skus, @Options) WITH ORDINALITY AS v(sku, options, position)
            """, new { ProductId = product.Id, Skus = product.Variants.Select(v => v.Sku).ToArray(), Options = product.Variants.Select(v => v.OptionsJson).ToArray() }, tx);
        await conn.ExecuteAsync("""
            INSERT INTO product_images (product_id, position, url, alt, color)
            SELECT @ProductId, i.position, i.url, i.alt, i.color FROM unnest(@Positions, @Urls, @Alts, @Colors) AS i(position, url, alt, color)
            """, new
        {
            ProductId = product.Id,
            Positions = product.Images.Select(i => i.Position).ToArray(),
            Urls = product.Images.Select(i => i.Url).ToArray(),
            Alts = product.Images.Select(i => i.Alt).ToArray(),
            Colors = product.Images.Select(i => i.Color).ToArray(),
        }, tx);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> UpdateProductAsync(long id, string title, long brandId, long categoryId, string description, string attributesJson, string status, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var updated = await conn.ExecuteAsync("""
            UPDATE products SET title = @Title, brand_id = @BrandId, category_id = @CategoryId, description = @Description,
                attributes = @Attributes::jsonb, status = @Status, updated_at = now()
            WHERE id = @Id
            """, new { Id = id, Title = title, BrandId = brandId, CategoryId = categoryId, Description = description, Attributes = attributesJson, Status = status }, tx);
        if (updated == 0) return false;
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged([id]), OutboxJsonContext.Default.ProductsChanged);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<(long OfferId, long ProductId)?> UpsertOfferAsync(ValidOffer offer, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var productId = await conn.ExecuteScalarAsync<long?>(
            "SELECT product_id FROM product_variants WHERE id = @VariantId", new { offer.VariantId }, tx);
        if (productId is null) return null;
        long offerId;
        try
        {
            offerId = await conn.ExecuteScalarAsync<long>("""
                INSERT INTO offers (seller_id, variant_id, price, list_price, stock, handling_days, status)
                VALUES (@SellerId, @VariantId, @Price, @ListPrice, @Stock, @HandlingDays, @Status)
                ON CONFLICT (seller_id, variant_id) DO UPDATE SET price = excluded.price, list_price = excluded.list_price,
                    stock = excluded.stock, handling_days = excluded.handling_days, status = excluded.status, updated_at = now()
                RETURNING id
                """, new { offer.SellerId, offer.VariantId, offer.Price, offer.ListPrice, offer.Stock, offer.HandlingDays, offer.Status }, tx);
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            return null;
        }
        await TouchAndEnqueueAsync(conn, tx, productId.Value);
        await tx.CommitAsync(ct);
        return (offerId, productId.Value);
    }

    public async Task<long?> DeleteOfferAsync(long offerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var productId = await conn.ExecuteScalarAsync<long?>("""
            DELETE FROM offers o USING product_variants v WHERE o.id = @Id AND v.id = o.variant_id RETURNING v.product_id
            """, new { Id = offerId }, tx);
        if (productId is null) return null;
        await TouchAndEnqueueAsync(conn, tx, productId.Value);
        await tx.CommitAsync(ct);
        return productId;
    }

    public async Task<CatalogStats> StatsAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<CatalogStats>("""
            SELECT
                (SELECT count(*) FROM products WHERE status = 'active') AS active_products,
                (SELECT count(*) FROM products WHERE status = 'draft') AS draft_products,
                (SELECT count(*) FROM products WHERE status = 'archived') AS archived_products,
                (SELECT count(*) FROM sellers WHERE status = 'active') AS active_sellers,
                (SELECT count(*) FROM sellers WHERE status = 'suspended') AS suspended_sellers,
                (SELECT count(*) FROM offers) AS offers,
                (SELECT count(*) FROM products p WHERE p.status = 'active' AND NOT EXISTS (
                    SELECT 1 FROM product_variants v JOIN offers o ON o.variant_id = v.id AND o.status = 'active' AND o.stock > 0
                    JOIN sellers s ON s.id = o.seller_id AND s.status = 'active'
                    WHERE v.product_id = p.id)) AS out_of_stock_products,
                (SELECT count(*) FROM brands) AS brands,
                (SELECT count(*) FROM categories) AS categories
            """);
    }

    static async Task TouchAndEnqueueAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long productId)
    {
        await conn.ExecuteAsync("UPDATE products SET updated_at = now() WHERE id = @Id", new { Id = productId }, tx);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged([productId]), OutboxJsonContext.Default.ProductsChanged);
    }

    static Dictionary<string, string> ParseMap(string json) =>
        JsonSerializer.Deserialize(json, AppJsonContext.Default.DictionaryStringString) ?? [];
}
