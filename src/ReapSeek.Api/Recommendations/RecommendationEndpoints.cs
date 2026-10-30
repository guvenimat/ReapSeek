using Microsoft.AspNetCore.Mvc;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Recommendations;

public static class RecommendationEndpoints
{
    public static void MapRecommendations(this RouteGroupBuilder api)
    {
        api.MapGet("/recommendations/{placement}", async (string placement, [FromQuery(Name = "product_id")] long? productId, int? limit,
                HttpContext ctx, RecommendationService recommendations, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return ApiResults.Ok(await recommendations.GetAsync(placement, productId, limit, ctx, ct), AppJsonContext.Default.RecommendationsResponse);
        });
    }
}
