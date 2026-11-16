using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Application;

namespace ReapSeek.Catalog.Api;

internal static class RecommendationEndpoints
{
    public static void MapRecommendations(this IEndpointRouteBuilder api)
    {
        api.MapGet("/recommendations/{placement}", async (string placement, [FromQuery(Name = "product_id")] long? productId, int? limit,
                HttpContext ctx, RecommendationService recommendations, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return ApiResults.Ok(await recommendations.GetAsync(placement, productId, limit, ctx, ct), CatalogJsonContext.Default.RecommendationsResponse);
        });
    }
}
