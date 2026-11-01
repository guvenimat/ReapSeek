using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed class ShippingDeadlineJob(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<ShippingDeadlineJob> log) : BackgroundService
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

public sealed class PackageReminderHandler(NpgsqlDataSource db, EmailSender email, IConfiguration cfg)
{
    public sealed record PackageRecord(string OrderNumber, string Status, long SellerId, DateTime PaidAt);
    public sealed record IdRecord(Guid Id);
    public sealed record SellerRecord(long SellerId);
    public sealed record PersonRecord(string Email, string Name);

    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');
    readonly int _cancelDays = (int)Math.Round(cfg.GetValue("Shipping:CancelAfterSeconds", 5 * 24 * 3600) / 86400.0);

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var m = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.PackageReminderMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var p = await conn.QuerySingleOrDefaultAsync<PackageRecord>("""
            SELECT o.number AS order_number, p.status, p.seller_id, o.paid_at FROM order_packages p JOIN orders o ON o.id = p.order_id WHERE p.id = @Id
            """, new IdRecord(m.PackageId));
        if (p is null || p.Status != "preparing") return;
        var url = $"{_backofficeUrl}/#/packages";
        var deadline = p.PaidAt.AddDays(Math.Max(1, _cancelDays)).ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var seller in await conn.QueryAsync<PersonRecord>("SELECT email, name FROM users WHERE seller_id = @SellerId", new SellerRecord(p.SellerId)))
            await email.SendAsync(new EmailMessage(seller.Email, $"Ship order {p.OrderNumber} soon",
                $"Hi {seller.Name},\n\nOrder {p.OrderNumber} was paid on {p.PaidAt:d MMM yyyy} and has not shipped yet. If it does not ship by {deadline} UTC we cancel your package and refund the customer.\n\nYour packages: {url}\n",
                $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#ec6416;font-size:20px">Order {WebUtility.HtmlEncode(p.OrderNumber)} is waiting to ship</h1><p>It was paid on {p.PaidAt:d MMM yyyy}. If it does not ship by <strong>{deadline} UTC</strong>, we cancel your package and refund the customer.</p><p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Ship it now</a></p></div>"""), ct);
    }
}
