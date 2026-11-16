using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Ordering.Contracts;
using ReapSeek.Sellers.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Sellers.Api;

internal sealed record StoreSeller(long Id, string Name, string Slug, string City, decimal Rating, DateTime JoinedAt, int ProductCount,
    int DeliveredPackages, decimal? AverageHandlingDays);
internal sealed record StoreSellerResponse(StoreSeller Seller, List<ProductCard> Products, int Total, int Page, int PageSize);

internal static class StoreEndpoints
{
    const int PageSize = 24;

    public static void MapStores(this IEndpointRouteBuilder api)
    {
        api.MapGet("/catalog/sellers/{slug}", async (string slug, int? page, StoreRepository stores, ISellerCatalog sellerCatalog, ISellerPackages packages, CancellationToken ct) =>
        {
            var p = Ensure.InRange(page ?? 1, 1, 1000, "page");
            if (slug.Length > 100 || await stores.SellerAsync(slug, ct) is not { } s) return ApiResults.NotFound("Seller not found");
            var store = await sellerCatalog.StoreProductsAsync(s.Id, p, PageSize, ct);
            var seller = new StoreSeller(s.Id, s.Name, s.Slug, s.City, s.Rating, s.CreatedAt, store.Total, await packages.DeliveredCountAsync(s.Id, ct),
                store.AverageHandlingDays);
            return ApiResults.Ok(new StoreSellerResponse(seller, store.Products, store.Total, p, PageSize), SellersJsonContext.Default.StoreSellerResponse);
        });
    }
}
