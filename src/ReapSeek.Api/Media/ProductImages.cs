using System.Security.Cryptography;
using Dapper;
using Npgsql;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Media;

public sealed record ProductImageRow(long Id, int Position, string Url, string Alt, string? Color, bool Uploaded);
public sealed record ProductImagesResponse(List<ProductImageRow> Images);

public static class ImageRules
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxUploadsPerProduct = 8;
    public const int MinSide = 200;
    public const int MaxSide = 8000;
    public const string UploadPrefix = "/media/uploads/";

    public static ImageInfo Validate(ReadOnlySpan<byte> data)
    {
        Ensure.That(data.Length > 0, "The file is empty");
        Ensure.That(data.Length <= MaxBytes, "Images can be at most 5 MB");
        var info = ImageProbe.Inspect(data);
        Ensure.That(info is not null, "Only JPEG, PNG and WebP images are accepted");
        Ensure.That(info.Width >= MinSide && info.Height >= MinSide, $"Images must be at least {MinSide} x {MinSide} pixels");
        Ensure.That(info.Width <= MaxSide && info.Height <= MaxSide, $"Images can be at most {MaxSide} x {MaxSide} pixels");
        return info;
    }

    public static bool IsUploadName(string name) =>
        name.Length is > 33 and < 40 && name[..32].All(char.IsAsciiHexDigitLower) && name[32] == '.' && name[33..] is "jpg" or "png" or "webp";

    public static string ContentType(string name) => name[33..] switch
    {
        "jpg" => "image/jpeg",
        "png" => "image/png",
        _ => "image/webp",
    };
}

public sealed class UploadStore(IConfiguration cfg)
{
    public string Root { get; } = cfg["Media:UploadPath"] ?? Path.Combine(AppContext.BaseDirectory, "uploads");

    public async Task<string> SaveAsync(byte[] data, ImageInfo info, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var name = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}.{info.Extension}";
        await File.WriteAllBytesAsync(Path.Combine(Root, name), data, ct);
        return ImageRules.UploadPrefix + name;
    }

    public void Delete(string url)
    {
        if (!url.StartsWith(ImageRules.UploadPrefix, StringComparison.Ordinal)) return;
        var name = url[ImageRules.UploadPrefix.Length..];
        if (ImageRules.IsUploadName(name)) File.Delete(Path.Combine(Root, name));
    }

    public string? PathOf(string name) => ImageRules.IsUploadName(name) && File.Exists(Path.Combine(Root, name)) ? Path.Combine(Root, name) : null;
}

public sealed class ProductImageService(NpgsqlDataSource db, UploadStore store, ILogger<ProductImageService> log)
{
    public sealed record OwnerRecord(long Id, string Title, string Status, long? SubmittedBySellerId);
    public sealed record ProductIdRecord(long ProductId);
    public sealed record ImageIdRecord(long ProductId, long ImageId);
    public sealed record ImageInsertRecord(long ProductId, string Url, string Alt, string? Color);
    public sealed record RenumberRecord(long ProductId, long FirstId);

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

public static class ProductImageEndpoints
{
    public static void MapProductImages(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/backoffice/catalog/products/{productId:long}/images").RequireAdmin();
        admin.MapGet("/", async (long productId, ProductImageService images, CancellationToken ct) =>
            Ok(await images.ListAsync(productId, null, ct)));
        admin.MapPost("/", async (long productId, HttpRequest request, ProductImageService images, CancellationToken ct) =>
            Ok(await images.UploadAsync(productId, null, request, ct), StatusCodes.Status201Created));
        admin.MapDelete("/{imageId:long}", async (long productId, long imageId, ProductImageService images, CancellationToken ct) =>
            Ok(await images.DeleteAsync(productId, imageId, null, ct)));
        admin.MapPost("/{imageId:long}/first", async (long productId, long imageId, ProductImageService images, CancellationToken ct) =>
            Ok(await images.MakeFirstAsync(productId, imageId, null, ct)));

        var seller = api.MapGroup("/seller/proposals/{productId:long}/images").RequireSeller();
        seller.MapGet("/", async (long productId, UserInfo user, ProductImageService images, CancellationToken ct) =>
            Ok(await images.ListAsync(productId, SellerOf(user), ct)));
        seller.MapPost("/", async (long productId, UserInfo user, HttpRequest request, ProductImageService images, CancellationToken ct) =>
            Ok(await images.UploadAsync(productId, SellerOf(user), request, ct), StatusCodes.Status201Created));
        seller.MapDelete("/{imageId:long}", async (long productId, long imageId, UserInfo user, ProductImageService images, CancellationToken ct) =>
            Ok(await images.DeleteAsync(productId, imageId, SellerOf(user), ct)));
        seller.MapPost("/{imageId:long}/first", async (long productId, long imageId, UserInfo user, ProductImageService images, CancellationToken ct) =>
            Ok(await images.MakeFirstAsync(productId, imageId, SellerOf(user), ct)));
    }

    static IResult Ok(List<ProductImageRow> images, int status = StatusCodes.Status200OK) =>
        Results.Json(new ProductImagesResponse(images), AppJsonContext.Default.ProductImagesResponse, statusCode: status);

    static long SellerOf(UserInfo user) => user.SellerId ?? throw new NotFoundException("Seller not found");
}
