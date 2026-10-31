using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

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

public sealed class EngagementHandler(NpgsqlDataSource db, EmailSender email, ReapDeepIngestClient reapDeep, IConfiguration cfg, TimeProvider clock)
{
    public sealed record CartLineRecord(string Title, string Slug, int Quantity, decimal? Price);
    public sealed record PersonRecord(string Email, string Name);
    public sealed record AlertRecord(string AlertName, string State, string Message, string InsightName, double Value, DateTime CheckedAt);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        if (message.Type == OutboxTypes.CartReminder) await RemindAsync(message, ct);
        else await AlertAsync(message, ct);
    }

    async Task RemindAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.CartReminderMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var lines = (await conn.QueryAsync<CartLineRecord>("""
            SELECT p.title, p.slug, l.quantity, CASE WHEN o.status = 'active' AND o.stock > 0 THEN o.price END AS price
            FROM cart_lines l JOIN offers o ON o.id = l.offer_id JOIN product_variants v ON v.id = o.variant_id JOIN products p ON p.id = v.product_id
            WHERE l.cart_id = @CartId ORDER BY l.added_at
            """, new { payload.CartId })).AsList();
        if (lines.Count == 0) return;
        var customer = await conn.QuerySingleOrDefaultAsync<PersonRecord>("SELECT email, name FROM users WHERE id = @Id", new { Id = payload.CustomerId });
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

    async Task AlertAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.AlertMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var a = await conn.QuerySingleOrDefaultAsync<AlertRecord>(
            "SELECT alert_name, state, message, insight_name, value, checked_at FROM analytics_alerts WHERE id = @Id", new { Id = payload.EventId });
        if (a is null) return;
        var url = $"{_backofficeUrl}/#/alerts";
        var headline = a.State == "firing" ? $"Alert: {a.AlertName}" : $"{a.AlertName} is back to normal";
        foreach (var admin in await conn.QueryAsync<PersonRecord>("SELECT email, name FROM users WHERE role = 'admin'"))
            await email.SendAsync(new EmailMessage(admin.Email, headline,
                $"{headline}\n\n{a.Message}\nSource: {a.InsightName} (ReapDeep)\n\nAll alerts: {url}\n",
                $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:{(a.State == "firing" ? "#c8324b" : "#12805c")};font-size:20px">{WebUtility.HtmlEncode(headline)}</h1><p>{WebUtility.HtmlEncode(a.Message)}</p><p style="color:#6b7080;font-size:13px">Source: {WebUtility.HtmlEncode(a.InsightName)} in ReapDeep</p><p><a href="{WebUtility.HtmlEncode(url)}">All alerts in the back office</a></p></div>"""), ct);
    }
}
