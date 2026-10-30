using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Products;

public static class ProductEndpoints
{
    const int SitemapLimit = 50_000;

    public static void MapStorefrontProducts(this RouteGroupBuilder api)
    {
        api.MapGet("/catalog/products/{slug}", async (string slug, ProductService products, CancellationToken ct) =>
            slug.Length <= 200 && await products.FindAsync(slug, ct) is { } product
                ? ApiResults.Ok(product, AppJsonContext.Default.StoreProduct)
                : ApiResults.NotFound("Product not found"));

        api.MapGet("/catalog/sitemap", async (ProductRepository products, CancellationToken ct) =>
            ApiResults.Ok(await products.SitemapAsync(SitemapLimit, ct), AppJsonContext.Default.SitemapResponse));
    }
}
