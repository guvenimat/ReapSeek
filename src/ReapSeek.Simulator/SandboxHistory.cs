using Dapper;
using Npgsql;

namespace ReapSeek.Simulator;

public sealed record SimPaymentRecord(string Id, decimal Amount, string Number, DateTime CreatedAt);
public sealed record SimRefundRecord(string Reference, string Id, string PaymentId, decimal Amount, DateTime CreatedAt);

public static class SandboxHistory
{
    public static async Task SyncAsync(NpgsqlDataSource db, string? sandboxConnectionString)
    {
        if (string.IsNullOrEmpty(sandboxConnectionString))
        {
            Console.WriteLine("ConnectionStrings:Sandbox is not set; simulated payments are not registered with the payment sandbox.");
            return;
        }
        await using var source = await db.OpenConnectionAsync();
        var payments = (await source.QueryAsync<SimPaymentRecord>("""
            SELECT payment_reference AS id, total AS amount, number, coalesce(paid_at, placed_at) AS created_at
            FROM orders WHERE status = 'paid' AND payment_reference LIKE 'pay_sim_%'
            """)).AsList();
        var refunds = (await source.QueryAsync<SimRefundRecord>("""
            SELECT 'return-' || r.id AS reference, r.refund_reference AS id, o.payment_reference AS payment_id, r.refund_amount AS amount, r.refunded_at AS created_at
            FROM returns r JOIN orders o ON o.id = r.order_id
            WHERE r.status = 'refunded' AND r.refund_reference LIKE 're_sim_%' AND o.payment_reference LIKE 'pay_sim_%'
            """)).AsList();

        await using var sandboxDb = new NpgsqlSlimDataSourceBuilder(sandboxConnectionString).EnableArrays().Build();
        await using var sandbox = await sandboxDb.OpenConnectionAsync();
        await using var tx = await sandbox.BeginTransactionAsync();
        var added = await sandbox.ExecuteAsync("""
            INSERT INTO payments (id, amount, currency, reference, description, return_url, webhook_url, created_at, status, card_brand, card_last4, attempts)
            SELECT p.id, p.amount, 'TRY', p.number, 'Order ' || p.number, 'http://localhost:3100/checkout/complete', 'http://api:8080/api/v1/payments/sandbox/webhook',
                p.created_at, 'succeeded', 'Visa', '4242', 1
            FROM unnest(@Ids, @Amounts, @Numbers, @Times) AS p(id, amount, number, created_at)
            ON CONFLICT (id) DO NOTHING
            """, new { Ids = payments.Select(p => p.Id).ToArray(), Amounts = payments.Select(p => p.Amount).ToArray(),
                Numbers = payments.Select(p => p.Number).ToArray(), Times = payments.Select(p => p.CreatedAt).ToArray() }, tx);
        var refunded = await sandbox.ExecuteAsync("""
            INSERT INTO refunds (reference, id, payment_id, amount, created_at)
            SELECT * FROM unnest(@References, @Ids, @PaymentIds, @Amounts, @Times)
            ON CONFLICT (reference) DO NOTHING
            """, new { References = refunds.Select(r => r.Reference).ToArray(), Ids = refunds.Select(r => r.Id).ToArray(),
                PaymentIds = refunds.Select(r => r.PaymentId).ToArray(), Amounts = refunds.Select(r => r.Amount).ToArray(), Times = refunds.Select(r => r.CreatedAt).ToArray() }, tx);
        await tx.CommitAsync();
        Console.WriteLine($"Payment sandbox: {added} simulated payments and {refunded} refunds registered.");
    }
}
