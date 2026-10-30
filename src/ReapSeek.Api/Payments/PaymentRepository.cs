using Dapper;
using Npgsql;

namespace ReapSeek.Api.Payments;

public static class PaymentRepository
{
    public static Task InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, Guid orderId, string provider, string providerPaymentId,
        decimal amount, string currency, string redirectUrl) =>
        conn.ExecuteAsync("""
            INSERT INTO payments (id, order_id, provider, provider_payment_id, amount, currency, status, redirect_url)
            VALUES (@Id, @OrderId, @Provider, @ProviderPaymentId, @Amount, @Currency, 'pending', @RedirectUrl)
            """, new { Id = id, OrderId = orderId, Provider = provider, ProviderPaymentId = providerPaymentId, Amount = amount, Currency = currency, RedirectUrl = redirectUrl }, tx);

    public static async Task<bool> RecordEventAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string provider, string eventId, string providerPaymentId,
        string type, string payload) =>
        await conn.ExecuteAsync("""
            INSERT INTO payment_events (provider, event_id, provider_payment_id, type, payload)
            VALUES (@Provider, @EventId, @ProviderPaymentId, @Type, @Payload::jsonb)
            ON CONFLICT DO NOTHING
            """, new { Provider = provider, EventId = eventId, ProviderPaymentId = providerPaymentId, Type = type, Payload = payload }, tx) == 1;

    public static Task UpdateStatusAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string provider, string providerPaymentId, string status, string? failureReason) =>
        conn.ExecuteAsync("""
            UPDATE payments SET status = @Status, failure_reason = @FailureReason, updated_at = now()
            WHERE provider = @Provider AND provider_payment_id = @ProviderPaymentId
            """, new { Provider = provider, ProviderPaymentId = providerPaymentId, Status = status, FailureReason = failureReason }, tx);
}
