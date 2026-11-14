using ReapSeek.Modules.Identity;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Modules.Catalog;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Ordering;

public sealed class CartReminderJob(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<CartReminderJob> log) : BackgroundService
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

public sealed class CartReminderHandler(NpgsqlDataSource db, EmailSender email, ReapDeepIngestClient reapDeep, IConfiguration cfg, TimeProvider clock)
{
    public sealed record CartItemRecord(long OfferId, int Quantity);
    public sealed record CartLineRecord(string Title, string Slug, int Quantity, decimal? Price);
    public sealed record PersonRecord(string Email, string Name);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        await RemindAsync(message, ct);
    }

    async Task RemindAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.CartReminderMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var cartLines = (await conn.QueryAsync<CartItemRecord>(
            "SELECT offer_id, quantity FROM ordering.cart_lines WHERE cart_id = @CartId ORDER BY added_at", new { payload.CartId })).AsList();
        var offers = (await Offers.LoadAsync(conn, null, cartLines.Select(l => l.OfferId).ToArray(), false)).ToDictionary(o => o.OfferId);
        var lines = cartLines.Where(l => offers.ContainsKey(l.OfferId)).Select(l => offers[l.OfferId] is var o
            ? new CartLineRecord(o.ProductTitle, o.ProductSlug, l.Quantity, o.OfferStatus == "active" && o.Stock > 0 ? o.Price : null) : null!).ToList();
        if (lines.Count == 0) return;
        var customer = await People.FindAsync(conn, payload.CustomerId);
        if (customer is null) return;

        static string M(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
        var cartUrl = $"{_storefrontUrl}/cart";
        var text = new StringBuilder($"Hi {customer.Name},\n\nYou left these in your cart:\n");
        var rows = new StringBuilder();
        foreach (var l in lines)
        {
            var price = l.Price is { } p ? $"{M(p * l.Quantity)} TRY" : "no longer available";
            text.AppendLine($"  {l.Quantity} x {l.Title} - {price}");
            rows.Append($"<tr><td style=\"padding:4px 0\">{l.Quantity} &times; <a href=\"{WebUtility.HtmlEncode($"{_storefrontUrl}/p/{l.Slug}")}\">{WebUtility.HtmlEncode(l.Title)}</a></td><td style=\"text-align:right\">{WebUtility.HtmlEncode(price)}</td></tr>");
        }
        text.AppendLine().AppendLine($"Finish your order: {cartUrl}").AppendLine().AppendLine("Prices and stock can change; we keep your cart for you.");
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">Still thinking it over?</h1>
              <p>Hi {WebUtility.HtmlEncode(customer.Name)}, you left these in your cart:</p>
              <table style="width:100%;border-collapse:collapse;font-size:14px">{rows}</table>
              <p><a href="{WebUtility.HtmlEncode(cartUrl)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Finish your order</a></p>
              <p style="color:#6b7080;font-size:12px">Prices and stock can change; we keep your cart for you.</p>
            </div>
            """;
        await email.SendAsync(new EmailMessage(customer.Email, "You left something in your cart", text.ToString(), html), ct);
        if (reapDeep.IsConfigured)
            await reapDeep.CaptureAsync([new CaptureEvent("cart_reminder_sent", ReapDeepIdentity.DistinctId(payload.CustomerId), $"cart-reminder-{message.Id}",
                clock.GetUtcNow().UtcDateTime, new PurchaseProperties("", "", 0, lines.Sum(l => l.Quantity), lines.Sum(l => (l.Price ?? 0) * l.Quantity), "TRY", 0, "", "server"))], ct);
    }
}
