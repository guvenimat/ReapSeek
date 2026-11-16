using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Modules.Ordering;
using ReapSeek.Shared.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReapSeek.Ordering.Jobs;

internal sealed class ShippingDeadlineJob(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<ShippingDeadlineJob> log) : BackgroundService
{
    public sealed record CutoffRecord(DateTime Cutoff, int Limit);
    public sealed record ReminderRecord(Guid Id);

    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Shipping:DeadlineCheckSeconds", 300));
    readonly TimeSpan _remindAfter = TimeSpan.FromSeconds(cfg.GetValue("Shipping:ReminderAfterSeconds", 2 * 24 * 3600));
    readonly TimeSpan _cancelAfter = TimeSpan.FromSeconds(cfg.GetValue("Shipping:CancelAfterSeconds", 5 * 24 * 3600));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (reminded, cancelled) = await RunOnceAsync(ct);
                if (reminded + cancelled > 0)
                    log.LogInformation("Shipping deadlines: reminded {Reminded} packages, cancelled {Cancelled} late packages", reminded, cancelled);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Shipping deadline check failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<(int Reminded, int Cancelled)> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        int reminded;
        await using (var conn = await db.OpenConnectionAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var due = (await conn.QueryAsync<ReminderRecord>("""
                WITH due AS (
                    SELECT p.id FROM order_packages p JOIN orders o ON o.id = p.order_id
                    WHERE p.status = 'preparing' AND p.ship_reminded_at IS NULL AND o.paid_at < @Cutoff
                    ORDER BY o.paid_at LIMIT @Limit FOR UPDATE OF p SKIP LOCKED)
                UPDATE order_packages p SET ship_reminded_at = now() FROM due WHERE p.id = due.id RETURNING p.id
                """, new CutoffRecord(now - _remindAfter, 200), tx)).AsList();
            foreach (var r in due)
                await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.PackageShipReminder, new PackageReminderMessage(r.Id), OutboxJsonContext.Default.PackageReminderMessage);
            await tx.CommitAsync(ct);
            reminded = due.Count;
        }

        List<Guid> late;
        await using (var conn = await db.OpenConnectionAsync(ct))
            late = (await conn.QueryAsync<Guid>("""
                SELECT DISTINCT o.id FROM orders o JOIN order_packages p ON p.order_id = o.id
                WHERE o.status = 'paid' AND p.status = 'preparing' AND o.paid_at < @Cutoff LIMIT @Limit
                """, new CutoffRecord(now - _cancelAfter, 100))).AsList();

        var cancelled = 0;
        foreach (var id in late)
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var order = await OrderStore.LoadAsync(conn, tx, id);
            if (order is null || !order.IsShippingOverdue(now, _cancelAfter)) continue;
            if (order.CanCancelPaid)
            {
                order.CancelPaid(CancelReasons.SellerDidNotShip, now);
                cancelled += order.Packages.Count;
            }
            else
            {
                foreach (var package in order.Packages.Where(p => p.Status == PackageStatus.Preparing).ToList())
                {
                    order.CancelPackage(package.Id, CancelReasons.SellerDidNotShip, now);
                    cancelled++;
                }
            }
            await OrderStore.SaveAsync(conn, tx, order);
            await tx.CommitAsync(ct);
        }
        return (reminded, cancelled);
    }
}
