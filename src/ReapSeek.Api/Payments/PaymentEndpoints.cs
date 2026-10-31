using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Payments;

public static class PaymentEndpoints
{
    public static void MapPayments(this RouteGroupBuilder api) =>
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
