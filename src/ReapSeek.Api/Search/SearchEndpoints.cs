using Microsoft.AspNetCore.Mvc;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Api.Search;

public static class SearchEndpoints
{
    public static void MapStorefrontCatalog(this RouteGroupBuilder api)
    {
        api.MapGet("/catalog/categories", async (CategoryCache categories, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "public, max-age=60";
            return ApiResults.Ok(await categories.TreeAsync(ct), AppJsonContext.Default.CategoryTreeResponse);
        });

        api.MapGet("/search", async (SearchService search, CancellationToken ct, string? q, string? category,
                [FromQuery] string[]? brand, [FromQuery] string[]? color, [FromQuery] string[]? size,
                [FromQuery(Name = "min_price")] decimal? minPrice, [FromQuery(Name = "max_price")] decimal? maxPrice,
                [FromQuery(Name = "in_stock")] bool? inStock, string? sort, int? page, [FromQuery(Name = "page_size")] int? pageSize) =>
            ApiResults.Ok(await search.SearchAsync(SearchQuery.Parse(q, category, brand, color, size, minPrice, maxPrice, inStock, sort, page, pageSize), ct),
                AppJsonContext.Default.SearchResponse));

        api.MapGet("/search/suggest", async (SearchService search, string? q, CancellationToken ct) =>
        {
            var query = Ensure.NotBlank(q, "q", 100);
            return ApiResults.Ok(await search.SuggestAsync(query, ct), AppJsonContext.Default.SuggestResponse);
        });
    }
}
