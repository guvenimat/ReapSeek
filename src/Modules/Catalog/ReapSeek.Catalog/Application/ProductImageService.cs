using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Api;
using ReapSeek.Catalog.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Catalog.Application;

internal sealed class ProductImageService(NpgsqlDataSource db, UploadStore store, ILogger<ProductImageService> log)
{
    internal sealed record OwnerRecord(long Id, string Title, string Status, long? SubmittedBySellerId);
    internal sealed record ProductIdRecord(long ProductId);
    internal sealed record ImageIdRecord(long ProductId, long ImageId);
    internal sealed record ImageInsertRecord(long ProductId, string Url, string Alt, string? Color);
    internal sealed record RenumberRecord(long ProductId, long FirstId);

    public async Task<List<ProductImageRow>> ListAsync(long productId, long? sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await OwnerAsync(conn, tx, productId, sellerId, write: false);
        return await ListAsync(conn, tx, productId);
    }

    public async Task<List<ProductImageRow>> UploadAsync(long productId, long? sellerId, HttpRequest request, CancellationToken ct)
    {
        Ensure.That(request.HasFormContentType, "Send the image as multipart/form-data");
        Ensure.That(request.ContentLength is null or <= ImageRules.MaxBytes + 64 * 1024, "Images can be at most 5 MB");
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        Ensure.That(file is not null, "file is required");
        Ensure.That(file.Length <= ImageRules.MaxBytes, "Images can be at most 5 MB");
        var data = new byte[file.Length];
        await using (var s = file.OpenReadStream()) await s.ReadExactlyAsync(data, ct);
        var info = ImageRules.Validate(data);
        var color = form["color"].ToString().Trim();
        Ensure.That(color.Length <= 40, "color is too long");
        var alt = form["alt"].ToString().Trim();
        Ensure.That(alt.Length <= 200, "alt is too long");

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var owner = await OwnerAsync(conn, tx, productId, sellerId, write: true);
        var uploads = await conn.ExecuteScalarAsync<int>($"SELECT count(*)::int FROM product_images WHERE product_id = @ProductId AND url LIKE '{ImageRules.UploadPrefix}%'",
            new ProductIdRecord(productId), tx);
        if (uploads >= ImageRules.MaxUploadsPerProduct) throw new ConflictException($"A product can have at most {ImageRules.MaxUploadsPerProduct} uploaded images", "too_many_images");

        var url = await store.SaveAsync(data, info, ct);
        try
        {
            await conn.ExecuteAsync("UPDATE product_images SET position = -position - 1 WHERE product_id = @ProductId", new ProductIdRecord(productId), tx);
            await conn.ExecuteAsync("INSERT INTO product_images (product_id, position, url, alt, color) VALUES (@ProductId, -1000000, @Url, @Alt, @Color)",
                new ImageInsertRecord(productId, url, alt.Length > 0 ? alt : owner.Title, color.Length > 0 ? color : null), tx);
            await RenumberAsync(conn, tx, productId, 0);
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged([productId]), OutboxJsonContext.Default.ProductsChanged);
            await tx.CommitAsync(ct);
        }
        catch
        {
            store.Delete(url);
            throw;
        }
        log.LogInformation("Uploaded image {Url} ({Width}x{Height} {Format}) for product {Product}", url, info.Width, info.Height, info.Format, productId);
        return await ListAsync(productId, sellerId, ct);
    }

    public async Task<List<ProductImageRow>> DeleteAsync(long productId, long imageId, long? sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await OwnerAsync(conn, tx, productId, sellerId, write: true);
        var images = await ListAsync(conn, tx, productId);
        var image = images.FirstOrDefault(i => i.Id == imageId) ?? throw new NotFoundException("Image not found");
        if (sellerId is not null) Ensure.That(image.Uploaded, "Only uploaded images can be removed");
        Ensure.That(images.Count > 1, "A product needs at least one image");
        await conn.ExecuteAsync("DELETE FROM product_images WHERE product_id = @ProductId AND id = @ImageId", new ImageIdRecord(productId, imageId), tx);
        await conn.ExecuteAsync("UPDATE product_images SET position = -position - 1 WHERE product_id = @ProductId", new ProductIdRecord(productId), tx);
        await RenumberAsync(conn, tx, productId, 0);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged([productId]), OutboxJsonContext.Default.ProductsChanged);
        await tx.CommitAsync(ct);
        try
        {
            store.Delete(image.Url);
        }
        catch (IOException ex)
        {
            log.LogWarning("Could not delete {Url}: {Message}", image.Url, ex.Message);
        }
        return await ListAsync(productId, sellerId, ct);
    }

    public async Task<List<ProductImageRow>> MakeFirstAsync(long productId, long imageId, long? sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await OwnerAsync(conn, tx, productId, sellerId, write: true);
        if ((await ListAsync(conn, tx, productId)).All(i => i.Id != imageId)) throw new NotFoundException("Image not found");
        await conn.ExecuteAsync("UPDATE product_images SET position = -position - 1 WHERE product_id = @ProductId", new ProductIdRecord(productId), tx);
        await RenumberAsync(conn, tx, productId, imageId);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged([productId]), OutboxJsonContext.Default.ProductsChanged);
        await tx.CommitAsync(ct);
        return await ListAsync(productId, sellerId, ct);
    }

    static async Task<OwnerRecord> OwnerAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long productId, long? sellerId, bool write)
    {
        var owner = await conn.QuerySingleOrDefaultAsync<OwnerRecord>(
            $"SELECT id, title, status, submitted_by_seller_id FROM products WHERE id = @ProductId{(write ? " FOR UPDATE" : "")}", new ProductIdRecord(productId), tx);
        if (owner is null || (sellerId is not null && owner.SubmittedBySellerId != sellerId)) throw new NotFoundException("Product not found");
        if (sellerId is not null && write && owner.Status is not ("pending_review" or "rejected"))
            throw new ConflictException("Images of a proposal can be changed until it is approved", "proposal_closed");
        return owner;
    }

    static async Task<List<ProductImageRow>> ListAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long productId) =>
        (await conn.QueryAsync<ProductImageRow>($"""
            SELECT id, position, url, alt, color, url LIKE '{ImageRules.UploadPrefix}%' AS uploaded
            FROM product_images WHERE product_id = @ProductId ORDER BY position
            """, new ProductIdRecord(productId), tx)).AsList();

    static Task RenumberAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long productId, long firstId) =>
        conn.ExecuteAsync($"""
            UPDATE product_images i SET position = r.n - 1
            FROM (
                SELECT id, row_number() OVER (ORDER BY id = @FirstId DESC, url LIKE '{ImageRules.UploadPrefix}%' DESC, position DESC) AS n
                FROM product_images WHERE product_id = @ProductId
            ) r
            WHERE i.id = r.id
            """, new RenumberRecord(productId, firstId), tx);
}
