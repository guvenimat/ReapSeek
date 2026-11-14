using ReapSeek.Modules.Identity;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Modules.Catalog;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Engagement.Wishlist;

public sealed class PriceDropJob(NpgsqlDataSource db, IConfiguration cfg, ILogger<PriceDropJob> log) : BackgroundService
{
    public const decimal MinimumDrop = 0.05m;
    public sealed record DropRecord(long CustomerId, long ProductId, decimal OldPrice, decimal NewPrice);
    public sealed record CandidateRecord(long CustomerId, long ProductId, decimal Base);
    public sealed record DueRecord(long[] CustomerIds, long[] ProductIds, decimal[] Bases, decimal[] Prices);

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
        var candidates = (await conn.QueryAsync<CandidateRecord>("""
            SELECT customer_id, product_id, coalesce(alert_price, added_price) AS base FROM engagement.wishlist_items
            WHERE coalesce(alert_price, added_price) IS NOT NULL
            """, transaction: tx)).AsList();
        var prices = await CatalogLookups.LowestPricesAsync(conn, candidates.Select(c => c.ProductId), tx);
        var due = candidates
            .Where(c => prices.TryGetValue(c.ProductId, out var price) && price <= Math.Round(c.Base * (1 - MinimumDrop), 2, MidpointRounding.AwayFromZero))
            .Take(500).ToList();
        var drops = due.Count == 0 ? [] : (await conn.QueryAsync<DropRecord>("""
            UPDATE engagement.wishlist_items w SET alert_price = d.price
            FROM unnest(@CustomerIds, @ProductIds, @Bases, @Prices) AS d(customer_id, product_id, base, price)
            WHERE w.customer_id = d.customer_id AND w.product_id = d.product_id AND coalesce(w.alert_price, w.added_price) = d.base
            RETURNING w.customer_id, w.product_id, d.base AS old_price, d.price AS new_price
            """, new DueRecord(due.Select(d => d.CustomerId).ToArray(), due.Select(d => d.ProductId).ToArray(), due.Select(d => d.Base).ToArray(),
            due.Select(d => prices[d.ProductId]).ToArray()), tx)).AsList();
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
        var customer = (await People.ReachableCustomersAsync(conn, [payload.CustomerId])).Count == 1 ? await People.FindAsync(conn, payload.CustomerId) : null;
        var product = await CatalogLookups.ProductAsync(conn, payload.ProductId);
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
