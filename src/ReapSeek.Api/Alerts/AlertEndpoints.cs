using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Api.Alerts;

public sealed record AlertInsight(long Id, string Name);
public sealed record ReapDeepAlertPayload(
    long AlertId, string AlertName, long ProjectId, string State, string Condition, double Threshold, string Window, double Value, double? PreviousValue,
    string Message, AlertInsight Insight, DateTime CheckedAt);
public sealed record AlertSubscriptionRequest(long? AlertId, string? Secret);
public sealed record AlertEventRow(
    long Id, long AlertId, string AlertName, string State, string Condition, double Threshold, string TimeWindow, double Value, double? PreviousValue,
    string Message, string InsightName, DateTime CheckedAt, DateTime ReceivedAt);
public sealed record AlertsResponse(List<AlertEventRow> Events, int Subscriptions);

public static class AlertSignature
{
    public const string Prefix = "sha256=";

    public static bool IsValid(string? header, byte[] body, string secret)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.Ordinal) || string.IsNullOrEmpty(secret)) return false;
        byte[] given;
        try
        {
            given = Convert.FromHexString(header[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body), given);
    }
}

public static class AlertEndpoints
{
    public static void MapAlerts(this RouteGroupBuilder api)
    {
        api.MapPost("/reapdeep/alerts", Receive);

        var admin = api.MapGroup("/backoffice/alerts").RequireAdmin();
        admin.MapGet("/", async (AlertRepository alerts, CancellationToken ct) =>
            ApiResults.Ok(await alerts.ListAsync(ct), AppJsonContext.Default.AlertsResponse));
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
            p = JsonSerializer.Deserialize(body, AppJsonContext.Default.ReapDeepAlertPayload);
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
