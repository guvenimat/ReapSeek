using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Application;

namespace ReapSeek.Catalog.Api;

internal sealed record ProductImageRow(long Id, int Position, string Url, string Alt, string? Color, bool Uploaded);
internal sealed record ProductImagesResponse(List<ProductImageRow> Images);

internal static class ProductImageEndpoints
{
    public static void MapProductImages(this IEndpointRouteBuilder api)
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
        Results.Json(new ProductImagesResponse(images), CatalogJsonContext.Default.ProductImagesResponse, statusCode: status);

    static long SellerOf(UserInfo user) => user.SellerId ?? throw new NotFoundException("Seller not found");
}
