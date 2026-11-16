using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Application;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Catalog.Infrastructure;

namespace ReapSeek.Catalog.Api;

internal static class CatalogEndpoints
{
    public static void MapCatalogAdmin(this RouteGroupBuilder group)
    {
        group.RequireAdmin();
        group.MapGet("/stats", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(await catalog.StatsAsync(ct), CatalogJsonContext.Default.CatalogStats));

        group.MapGet("/categories", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(new CategoriesResponse(await catalog.ListCategoriesAsync(ct)), CatalogJsonContext.Default.CategoriesResponse));
        group.MapPost("/categories", async (CreateCategoryRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Created(await service.CreateCategoryAsync(req, ct), CatalogJsonContext.Default.CategoryRow));

        group.MapGet("/brands", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(new BrandsResponse(await catalog.ListBrandsAsync(ct)), CatalogJsonContext.Default.BrandsResponse));
        group.MapPost("/brands", async (CreateBrandRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Created(await service.CreateBrandAsync(req, ct), CatalogJsonContext.Default.BrandRow));

        group.MapGet("/products", async (CatalogService service, string? q, string? status, string? category,
                [FromQuery(Name = "brand_id")] long? brandId, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
            ApiResults.Ok(await service.ListProductsAsync(CatalogService.Filter(q, status, category, brandId, page, pageSize), ct),
                CatalogJsonContext.Default.ProductsResponse));
        group.MapGet("/products/{id:long}", async (long id, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetProductAsync(id, ct), CatalogJsonContext.Default.ProductDetail));
        group.MapPost("/products", async (CreateProductRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Created(await service.CreateProductAsync(req, ct), CatalogJsonContext.Default.ProductDetail));
        group.MapPatch("/products/{id:long}", async (long id, UpdateProductRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateProductAsync(id, req, ct), CatalogJsonContext.Default.ProductDetail));

        group.MapPut("/offers", async (OfferRequest req, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpsertOfferAsync(req, ct), CatalogJsonContext.Default.ProductDetail));
        group.MapDelete("/offers/{id:long}", async (long id, CatalogService service, CancellationToken ct) =>
            ApiResults.Ok(await service.DeleteOfferAsync(id, ct), CatalogJsonContext.Default.ProductDetail));
    }
}
