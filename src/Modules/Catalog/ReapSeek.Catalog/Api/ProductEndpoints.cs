using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Application;
using ReapSeek.Catalog.Infrastructure;

namespace ReapSeek.Catalog.Api;

internal static class ProductEndpoints
{
    const int SitemapLimit = 50_000;

    public static void MapStorefrontProducts(this IEndpointRouteBuilder api)
    {
        api.MapGet("/catalog/products/{slug}", async (string slug, ProductService products, CancellationToken ct) =>
            slug.Length <= 200 && await products.FindAsync(slug, ct) is { } product
                ? ApiResults.Ok(product, CatalogJsonContext.Default.StoreProduct)
                : ApiResults.NotFound("Product not found"));

        api.MapGet("/catalog/sitemap", async (ProductRepository products, CancellationToken ct) =>
            ApiResults.Ok(await products.SitemapAsync(SitemapLimit, ct), CatalogJsonContext.Default.SitemapResponse));
    }
}
