using Microsoft.AspNetCore.Mvc;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Catalog;

public static class CatalogEndpoints
{
    public static void MapCatalogAdmin(this RouteGroupBuilder group)
    {
        group.RequireAdmin();
        group.MapGet("/stats", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(await catalog.StatsAsync(ct), AppJsonContext.Default.CatalogStats));

        group.MapGet("/categories", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(new CategoriesResponse(await catalog.ListCategoriesAsync(ct)), AppJsonContext.Default.CategoriesResponse));
        group.MapPost("/categories", async (CreateCategoryRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Created(await service.CreateCategoryAsync(req, ct), AppJsonContext.Default.CategoryRow));

        group.MapGet("/brands", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(new BrandsResponse(await catalog.ListBrandsAsync(ct)), AppJsonContext.Default.BrandsResponse));
        group.MapPost("/brands", async (CreateBrandRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Created(await service.CreateBrandAsync(req, ct), AppJsonContext.Default.BrandRow));

        group.MapGet("/products", async (CatalogService service, string? q, string? status, string? category,
                [FromQuery(Name = "brand_id")] long? brandId, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
            ApiResults.Ok(await service.ListProductsAsync(CatalogService.Filter(q, status, category, brandId, page, pageSize), ct),
                AppJsonContext.Default.ProductsResponse));
        group.MapGet("/products/{id:long}", async (long id, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetProductAsync(id, ct), AppJsonContext.Default.ProductDetail));
        group.MapPost("/products", async (CreateProductRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Created(await service.CreateProductAsync(req, ct), AppJsonContext.Default.ProductDetail));
        group.MapPatch("/products/{id:long}", async (long id, UpdateProductRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateProductAsync(id, req, ct), AppJsonContext.Default.ProductDetail));

        group.MapPut("/offers", async (OfferRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpsertOfferAsync(req, ct), AppJsonContext.Default.ProductDetail));
        group.MapDelete("/offers/{id:long}", async (long id, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.DeleteOfferAsync(id, ct), AppJsonContext.Default.ProductDetail));
    }
}
