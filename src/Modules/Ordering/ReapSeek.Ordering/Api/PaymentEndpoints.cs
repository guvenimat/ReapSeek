using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Ordering.Application;

namespace ReapSeek.Ordering.Api;

internal static class PaymentEndpoints
{
    public static void MapPayments(this IEndpointRouteBuilder api) =>
        api.MapPost("/payments/sandbox/webhook", async (HttpRequest request, PaymentService payments, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            return await payments.HandleSandboxWebhookAsync(body, request.Headers["Sandbox-Signature"], ct) switch
            {
                WebhookOutcome.Rejected => ApiResults.Unauthorized("Invalid signature"),
                WebhookOutcome.Malformed => ApiResults.BadRequest("Malformed event"),
                _ => Results.Ok(),
            };
        });
}
