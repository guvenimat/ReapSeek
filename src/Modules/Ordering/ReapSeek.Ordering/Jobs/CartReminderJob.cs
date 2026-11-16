using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReapSeek.Ordering.Jobs;

internal sealed class CartReminderJob(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<CartReminderJob> log) : BackgroundService
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    public sealed record DueRecord(Guid Id, long CustomerId);
    public sealed record WindowRecord(DateTime Cutoff, DateTime Oldest);

    readonly TimeSpan _after = TimeSpan.FromSeconds(cfg.GetValue("Carts:ReminderAfterSeconds", 3600));
    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Carts:ReminderCheckSeconds", 60));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var count = await RunOnceAsync(ct);
                if (count > 0) log.LogInformation("Queued {Count} abandoned cart reminders", count);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Cart reminder check failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var due = (await conn.QueryAsync<DueRecord>("""
            WITH due AS (
                SELECT c.id FROM carts c
                WHERE c.customer_id IS NOT NULL AND c.updated_at < @Cutoff AND c.updated_at > @Oldest
                  AND (c.reminded_at IS NULL OR c.reminded_at < c.updated_at)
                  AND EXISTS (SELECT 1 FROM cart_lines l WHERE l.cart_id = c.id)
                ORDER BY c.updated_at LIMIT 100 FOR UPDATE SKIP LOCKED
            )
            UPDATE carts c SET reminded_at = now() FROM due WHERE c.id = due.id RETURNING c.id, c.customer_id
            """, new WindowRecord(now - _after, now - MaxAge), tx)).AsList();
        foreach (var cart in due)
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CartReminder, new CartReminderMessage(cart.Id, cart.CustomerId),
                OutboxJsonContext.Default.CartReminderMessage);
        await tx.CommitAsync(ct);
        return due.Count;
    }
}
