using System.Globalization;
using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Outbox;
using ReapSeek.Shared.Providers;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed class OrderRefundHandler(NpgsqlDataSource db, IPaymentGateway payments, EmailSender email, ReapDeepIngestClient reapDeep, IConfiguration cfg,
    TimeProvider clock, ILogger<OrderRefundHandler> log)
{
    public sealed record RefundDoneRecord(Guid Id, string Reference);
    public sealed record IdRecord(Guid Id);
    public sealed record PersonRecord(string Email, string Name);
    public sealed record CustomerRecord(long Id);
    public sealed record SellerRecord(long SellerId);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var m = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.OrderRefundMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        if (await conn.ExecuteScalarAsync<DateTime?>("SELECT refunded_at FROM orders WHERE id = @Id", new IdRecord(m.OrderId)) is not null) return;

        var reference = await payments.RefundAsync(m.PaymentReference, m.Amount, $"order-{m.OrderId}", ct);
        var updated = await conn.ExecuteAsync("UPDATE orders SET refunded_at = now(), refund_reference = @Reference WHERE id = @Id AND refunded_at IS NULL",
            new RefundDoneRecord(m.OrderId, reference));
        if (updated == 0) return;
        log.LogInformation("Refunded {Amount} for cancelled order {Order} ({Refund})", m.Amount, m.Number, reference);

        var order = await OrderStore.LoadByNumberAsync(conn, null, m.Number) ?? throw new InvalidOperationException($"Order {m.Number} not found");
        var customer = await conn.QuerySingleAsync<PersonRecord>("SELECT email, name FROM users WHERE id = @Id", new CustomerRecord(order.CustomerId));
        var amount = $"{m.Amount.ToString("N2", CultureInfo.InvariantCulture)} {order.Total.Currency}";
        var orderUrl = $"{_storefrontUrl}/account/orders/{order.Number}";
        var items = string.Join(", ", order.Lines.Select(l => $"{l.Quantity} x {l.ProductTitle}"));
        await email.SendAsync(new EmailMessage(customer.Email, $"Order {order.Number} is cancelled and refunded",
            $"Hi {customer.Name},\n\nWe cancelled order {order.Number} as you asked and refunded {amount} to the card you paid with; it can take a few days to appear.\nItems: {items}\n\nOrder: {orderUrl}\n\nThe ReapSeek team\n",
            $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">Your order is cancelled</h1>
              <p>Hi {WebUtility.HtmlEncode(customer.Name)}, we cancelled order {WebUtility.HtmlEncode(order.Number)} as you asked and refunded <strong>{WebUtility.HtmlEncode(amount)}</strong> to the card you paid with. It can take a few days to appear.</p>
              <p style="font-size:13px;color:#6b7080">{WebUtility.HtmlEncode(items)}</p>
              <p><a href="{WebUtility.HtmlEncode(orderUrl)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">View your order</a></p>
            </div>
            """), ct);

        foreach (var package in order.Packages)
            foreach (var seller in await conn.QueryAsync<PersonRecord>("SELECT email, name FROM users WHERE seller_id = @SellerId", new SellerRecord(package.SellerId)))
                await email.SendAsync(new EmailMessage(seller.Email, $"Do not ship: order {order.Number} was cancelled",
                    $"Hi {seller.Name},\n\nThe customer cancelled order {order.Number} before it shipped. Do not ship this package: {string.Join(", ", package.Lines.Select(l => $"{l.Quantity} x {l.ProductTitle}"))}.\nThe stock is back on your offers.\n\nPackages: {_backofficeUrl}/#/packages\n",
                    $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#c8324b;font-size:20px">Do not ship order {WebUtility.HtmlEncode(order.Number)}</h1><p>The customer cancelled it before it shipped. The stock is back on your offers.</p><p><a href="{WebUtility.HtmlEncode($"{_backofficeUrl}/#/packages")}">Your packages</a></p></div>"""), ct);

        if (reapDeep.IsConfigured)
            await reapDeep.CaptureAsync(order.Packages.SelectMany(p => p.Lines.Select(l => new CaptureEvent("refund", ReapDeepIdentity.DistinctId(order.CustomerId),
                $"order-refund-line-{l.Id}", clock.GetUtcNow().UtcDateTime,
                new PurchaseProperties(l.ProductId.ToString(CultureInfo.InvariantCulture), order.Number, l.UnitPrice.Amount, l.Quantity, l.Total.Amount,
                    l.UnitPrice.Currency, p.SellerId, l.Sku, "server", $"cancel-{order.Number}")))).ToList(), ct);
    }
}
