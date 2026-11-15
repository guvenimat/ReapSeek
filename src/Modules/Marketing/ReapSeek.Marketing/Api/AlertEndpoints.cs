using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Marketing.Application;
using ReapSeek.Marketing.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Marketing.Api;

internal static class AlertEndpoints
{
    public static void MapAlerts(this IEndpointRouteBuilder api)
    {
        api.MapPost("/reapdeep/alerts", Receive);

        var admin = api.MapGroup("/backoffice/alerts").RequireAdmin();
        admin.MapGet("/", async (AlertRepository alerts, CancellationToken ct) =>
            ApiResults.Ok(await alerts.ListAsync(ct), MarketingJsonContext.Default.AlertsResponse));
        admin.MapPut("/subscriptions", async (AlertSubscriptionRequest req, AlertRepository alerts, CancellationToken ct) =>
        {
            Ensure.That(req.AlertId is > 0, "alert_id is required");
            await alerts.SaveSubscriptionAsync(req.AlertId!.Value, Ensure.NotBlank(req.Secret, "secret", 200), ct);
            return Results.NoContent();
        });
    }

    static async Task<IResult> Receive(HttpRequest request, AlertRepository alerts, ILoggerFactory logs, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        if (!long.TryParse(request.Headers["X-ReapDeep-Alert"], NumberStyles.None, CultureInfo.InvariantCulture, out var alertId))
            return ApiResults.Unauthorized("Unknown alert");

        var secret = await alerts.SecretAsync(alertId, ct);
        if (secret is null || !AlertSignature.IsValid(request.Headers["X-ReapDeep-Signature"], body, secret))
            return ApiResults.Unauthorized("Invalid signature");

        ReapDeepAlertPayload? p;
        try
        {
            p = JsonSerializer.Deserialize(body, MarketingJsonContext.Default.ReapDeepAlertPayload);
        }
        catch (JsonException)
        {
            return ApiResults.BadRequest("Malformed alert");
        }
        if (p is null || p.AlertId != alertId) return ApiResults.BadRequest("Malformed alert");

        await alerts.RecordAsync(p, ct);
        logs.CreateLogger("ReapSeek.Alerts").LogInformation("ReapDeep alert {Alert} is {State}: {Message}", p.AlertName, p.State, p.Message);
        return Results.Ok();
    }
}
