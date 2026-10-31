using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Outbox;

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
    public sealed record AlertInsertRecord(
        long AlertId, string AlertName, string State, string Condition, double Threshold, string TimeWindow, double Value, double? PreviousValue,
        string Message, string InsightName, DateTime CheckedAt);
    public sealed record SubscriptionRecord(long AlertId, string Secret);

    public static void MapAlerts(this RouteGroupBuilder api)
    {
        api.MapPost("/reapdeep/alerts", Receive);

        var admin = api.MapGroup("/backoffice/alerts").RequireAdmin();
        admin.MapGet("/", async (NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var events = (await conn.QueryAsync<AlertEventRow>("""
                SELECT id, alert_id, alert_name, state, condition, threshold, time_window, value, previous_value, message, insight_name, checked_at, received_at
                FROM analytics_alerts ORDER BY received_at DESC, id DESC LIMIT 100
                """)).AsList();
            var subscriptions = await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reapdeep_alert_subscriptions");
            return ApiResults.Ok(new AlertsResponse(events, subscriptions), AppJsonContext.Default.AlertsResponse);
        });
        admin.MapPut("/subscriptions", async (AlertSubscriptionRequest req, NpgsqlDataSource db, CancellationToken ct) =>
        {
            Ensure.That(req.AlertId is > 0, "alert_id is required");
            var secret = Ensure.NotBlank(req.Secret, "secret", 200);
            await using var conn = await db.OpenConnectionAsync(ct);
            await conn.ExecuteAsync("""
                INSERT INTO reapdeep_alert_subscriptions (alert_id, secret) VALUES (@AlertId, @Secret)
                ON CONFLICT (alert_id) DO UPDATE SET secret = excluded.secret
                """, new SubscriptionRecord(req.AlertId!.Value, secret));
            return Results.NoContent();
        });
    }

    static async Task<IResult> Receive(HttpRequest request, NpgsqlDataSource db, ILoggerFactory logs, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        if (!long.TryParse(request.Headers["X-ReapDeep-Alert"], NumberStyles.None, CultureInfo.InvariantCulture, out var alertId))
            return ApiResults.Unauthorized("Unknown alert");

        await using var conn = await db.OpenConnectionAsync(ct);
        var secret = await conn.ExecuteScalarAsync<string?>("SELECT secret FROM reapdeep_alert_subscriptions WHERE alert_id = @Id", new { Id = alertId });
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

        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long?>("""
            INSERT INTO analytics_alerts (alert_id, alert_name, state, condition, threshold, time_window, value, previous_value, message, insight_name, checked_at)
            VALUES (@AlertId, @AlertName, @State, @Condition, @Threshold, @TimeWindow, @Value, @PreviousValue, @Message, @InsightName, @CheckedAt)
            ON CONFLICT (alert_id, checked_at, state) DO NOTHING RETURNING id
            """, new AlertInsertRecord(p.AlertId, p.AlertName, p.State, p.Condition, p.Threshold, p.Window, p.Value, p.PreviousValue, p.Message,
            p.Insight.Name, p.CheckedAt), tx);
        if (id is { } eventId)
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.AlertReceived, new AlertMessage(eventId), OutboxJsonContext.Default.AlertMessage);
        await tx.CommitAsync(ct);
        logs.CreateLogger("ReapSeek.Alerts").LogInformation("ReapDeep alert {Alert} is {State}: {Message}", p.AlertName, p.State, p.Message);
        return Results.Ok();
    }
}
