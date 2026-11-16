using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Ordering.Application;

namespace ReapSeek.Ordering.Api;

internal static class ShippingEndpoints
{
    public static void MapShipping(this IEndpointRouteBuilder api) =>
        api.MapPost("/shipping/sandbox/webhook", async (HttpRequest request, ShippingService shipping, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            return await shipping.HandleSandboxWebhookAsync(body, request.Headers["Sandbox-Signature"], ct) switch
            {
                ShippingWebhookOutcome.Rejected => ApiResults.Unauthorized("Invalid signature"),
                ShippingWebhookOutcome.Malformed => ApiResults.BadRequest("Malformed event"),
                _ => Results.Ok(),
            };
        });
}
