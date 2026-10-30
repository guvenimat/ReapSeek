using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Ordering;

namespace ReapSeek.Worker.Orders;

public sealed class PaymentExpiryJob(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<PaymentExpiryJob> log) : BackgroundService
{
    const int BatchSize = 100;
    public const string TimeoutReason = "payment_timeout";
    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Orders:ExpiryCheckSeconds", 60));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cancelled = await RunOnceAsync(ct);
                if (cancelled > 0) log.LogInformation("Cancelled {Count} orders that were not paid in time", cancelled);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Payment expiry check failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        List<Guid> due;
        await using (var conn = await db.OpenConnectionAsync(ct))
        {
            due = (await conn.QueryAsync<Guid>("""
                SELECT id FROM orders WHERE status = 'pending_payment' AND placed_at < @Cutoff ORDER BY placed_at LIMIT @Limit
                """, new { Cutoff = now - Order.PaymentWindow, Limit = BatchSize })).AsList();
        }

        var cancelled = 0;
        foreach (var id in due)
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var order = await OrderStore.LoadAsync(conn, tx, id);
            if (order is not null && order.IsPaymentOverdue(now) && order.Cancel(TimeoutReason, now))
            {
                await OrderStore.SaveAsync(conn, tx, order);
                cancelled++;
            }
            await tx.CommitAsync(ct);
        }
        return cancelled;
    }
}
