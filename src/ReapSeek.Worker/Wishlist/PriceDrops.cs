using System.Globalization;
using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Orders;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Wishlist;

public sealed class PriceDropJob(NpgsqlDataSource db, IConfiguration cfg, ILogger<PriceDropJob> log) : BackgroundService
{
    public const decimal MinimumDrop = 0.05m;
    public sealed record DropRecord(long CustomerId, long ProductId, decimal OldPrice, decimal NewPrice);
    public sealed record ThresholdRecord(decimal Factor, int Limit);

    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Wishlist:PriceCheckSeconds", 300));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var count = await RunOnceAsync(ct);
                if (count > 0) log.LogInformation("Queued {Count} wishlist price drop notices", count);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Wishlist price check failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var drops = (await conn.QueryAsync<DropRecord>("""
            WITH current AS (
                SELECT w.customer_id, w.product_id, coalesce(w.alert_price, w.added_price) AS base, m.price
                FROM wishlist_items w
                JOIN products p ON p.id = w.product_id AND p.status = 'active'
                JOIN users u ON u.id = w.customer_id AND u.suspended_at IS NULL AND u.email_verified_at IS NOT NULL
                CROSS JOIN LATERAL (
                    SELECT min(o.price) AS price FROM offers o JOIN product_variants v ON v.id = o.variant_id JOIN sellers s ON s.id = o.seller_id
                    WHERE v.product_id = w.product_id AND o.status = 'active' AND o.stock > 0 AND s.status = 'active') m
                WHERE coalesce(w.alert_price, w.added_price) IS NOT NULL),
            due AS (SELECT * FROM current WHERE price IS NOT NULL AND price <= round(base * @Factor, 2) LIMIT @Limit)
            UPDATE wishlist_items w SET alert_price = due.price FROM due
            WHERE w.customer_id = due.customer_id AND w.product_id = due.product_id AND coalesce(w.alert_price, w.added_price) = due.base
            RETURNING w.customer_id, w.product_id, due.base AS old_price, due.price AS new_price
            """, new ThresholdRecord(1 - MinimumDrop, 500), tx)).AsList();
        foreach (var d in drops)
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.PriceDropped, new PriceDropMessage(d.CustomerId, d.ProductId, d.OldPrice, d.NewPrice),
                OutboxJsonContext.Default.PriceDropMessage);
        await tx.CommitAsync(ct);
        return drops.Count;
    }
}

public sealed class PriceDropHandler(NpgsqlDataSource db, EmailSender email, ReapDeepIngestClient reapDeep, IConfiguration cfg, TimeProvider clock)
{
    public sealed record IdRecord(long Id);
    public sealed record PersonRecord(string Email, string Name);
    public sealed record ProductRecord(string Title, string Slug);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.PriceDropMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var customer = await conn.QuerySingleOrDefaultAsync<PersonRecord>(
            "SELECT email, name FROM users WHERE id = @Id AND suspended_at IS NULL", new IdRecord(payload.CustomerId));
        var product = await conn.QuerySingleOrDefaultAsync<ProductRecord>("SELECT title, slug FROM products WHERE id = @Id", new IdRecord(payload.ProductId));
        if (customer is null || product is null) return;

        static string M(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture) + " TRY";
        var url = $"{_storefrontUrl}/p/{product.Slug}";
        var saved = payload.OldPrice - payload.NewPrice;
        var subject = $"Price drop: {product.Title}";
        var text = $"Hi {customer.Name},\n\n{product.Title} from your wishlist is now {M(payload.NewPrice)} (was {M(payload.OldPrice)}, you save {M(saved)}).\n\n{url}\n\nManage your wishlist: {_storefrontUrl}/wishlist\n";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">A price dropped on your wishlist</h1>
              <p>Hi {WebUtility.HtmlEncode(customer.Name)},</p>
              <p><a href="{WebUtility.HtmlEncode(url)}">{WebUtility.HtmlEncode(product.Title)}</a> is now <strong>{M(payload.NewPrice)}</strong>
                <span style="color:#6b7080;text-decoration:line-through">{M(payload.OldPrice)}</span>. You save {M(saved)}.</p>
              <p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">See the product</a></p>
              <p style="color:#6b7080;font-size:12px">You get this because the product is on your wishlist. <a href="{WebUtility.HtmlEncode($"{_storefrontUrl}/wishlist")}">Manage your wishlist</a></p>
            </div>
            """;
        await email.SendAsync(new EmailMessage(customer.Email, subject, text, html), ct);
        if (reapDeep.IsConfigured)
            await reapDeep.CaptureAsync([new CaptureEvent("price_drop_notified", ReapDeepIdentity.DistinctId(payload.CustomerId), $"price-drop-{message.Id}",
                clock.GetUtcNow().UtcDateTime, new PurchaseProperties(payload.ProductId.ToString(CultureInfo.InvariantCulture), "", payload.NewPrice, 0, saved, "TRY", 0, "", "server"))], ct);
    }
}
