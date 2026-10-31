using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Alerts;

public sealed class AlertRepository(NpgsqlDataSource db)
{
    public sealed record AlertInsertRecord(
        long AlertId, string AlertName, string State, string Condition, double Threshold, string TimeWindow, double Value, double? PreviousValue,
        string Message, string InsightName, DateTime CheckedAt);
    public sealed record SubscriptionRecord(long AlertId, string Secret);
    public sealed record AlertIdRecord(long Id);

    public async Task<AlertsResponse> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var events = (await conn.QueryAsync<AlertEventRow>("""
            SELECT id, alert_id, alert_name, state, condition, threshold, time_window, value, previous_value, message, insight_name, checked_at, received_at
            FROM analytics_alerts ORDER BY received_at DESC, id DESC LIMIT 100
            """)).AsList();
        var subscriptions = await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reapdeep_alert_subscriptions");
        return new AlertsResponse(events, subscriptions);
    }

    public async Task SaveSubscriptionAsync(long alertId, string secret, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO reapdeep_alert_subscriptions (alert_id, secret) VALUES (@AlertId, @Secret)
            ON CONFLICT (alert_id) DO UPDATE SET secret = excluded.secret
            """, new SubscriptionRecord(alertId, secret));
    }

    public async Task<string?> SecretAsync(long alertId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<string?>("SELECT secret FROM reapdeep_alert_subscriptions WHERE alert_id = @Id", new AlertIdRecord(alertId));
    }

    public async Task RecordAsync(ReapDeepAlertPayload p, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
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
    }
}
