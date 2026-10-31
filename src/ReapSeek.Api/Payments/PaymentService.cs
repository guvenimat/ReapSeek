using System.Text.Json;
using Npgsql;
using ReapSeek.Api.Carts;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Observability;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Providers;

namespace ReapSeek.Api.Payments;

public enum WebhookOutcome { Rejected, Malformed, Duplicate, Ignored, Applied }

public sealed class PaymentService(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<PaymentService> log)
{
    readonly string _secret = cfg["Payments:Sandbox:WebhookSecret"] ?? "";

    public async Task<WebhookOutcome> HandleSandboxWebhookAsync(string body, string? signature, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!WebhookVerifier.IsValid(signature, body, _secret, now)) return WebhookOutcome.Rejected;
        SandboxWebhookEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize(body, SandboxJsonContext.Default.SandboxWebhookEvent);
        }
        catch (JsonException)
        {
            return WebhookOutcome.Malformed;
        }
        if (evt?.Data is null || string.IsNullOrEmpty(evt.Id) || string.IsNullOrEmpty(evt.Data.PaymentId)) return WebhookOutcome.Malformed;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (!await PaymentRepository.RecordEventAsync(conn, tx, SandboxProvider.ProviderName, evt.Id, evt.Data.PaymentId, evt.Type, body))
        {
            await tx.CommitAsync(ct);
            return WebhookOutcome.Duplicate;
        }

        var order = await OrderStore.LoadByPaymentReferenceAsync(conn, tx, evt.Data.PaymentId);
        if (order is null)
        {
            log.LogWarning("Webhook {Event} refers to unknown payment {Payment}", evt.Id, evt.Data.PaymentId);
            await tx.CommitAsync(ct);
            return WebhookOutcome.Ignored;
        }

        var at = now.UtcDateTime;
        switch (evt.Type)
        {
            case "payment.succeeded":
                await PaymentRepository.UpdateStatusAsync(conn, tx, SandboxProvider.ProviderName, evt.Data.PaymentId, "succeeded", null);
                try
                {
                    if (order.MarkPaid(evt.Data.PaymentId, Money.Of(evt.Data.Amount, evt.Data.Currency), at))
                        await RemovePurchasedFromCartAsync(conn, tx, order);
                }
                catch (DomainException ex)
                {
                    log.LogError("Payment {Payment} succeeded but order {Order} could not be marked paid: {Message}", evt.Data.PaymentId, order.Number, ex.Message);
                }
                break;
            case "payment.failed":
                await PaymentRepository.UpdateStatusAsync(conn, tx, SandboxProvider.ProviderName, evt.Data.PaymentId, "failed", evt.Data.FailureReason);
                if (order.Status == OrderStatus.PendingPayment) order.MarkPaymentFailed(evt.Data.FailureReason ?? "payment_failed", at);
                break;
            default:
                await tx.CommitAsync(ct);
                return WebhookOutcome.Ignored;
        }
        var becamePaid = order.DomainEvents.OfType<OrderPaid>().Any();
        await OrderStore.SaveAsync(conn, tx, order);
        await tx.CommitAsync(ct);
        if (becamePaid)
        {
            Telemetry.OrdersPaid.Add(1);
            Telemetry.Revenue.Add((double)order.Total.Amount);
        }
        if (evt.Type == "payment.failed") Telemetry.PaymentsFailed.Add(1, new KeyValuePair<string, object?>("reason", evt.Data.FailureReason ?? "payment_failed"));
        log.LogInformation("Applied {Type} to order {Order}, now {Status}", evt.Type, order.Number, OrderStatusRules.Name(order.Status));
        return WebhookOutcome.Applied;
    }

    static async Task RemovePurchasedFromCartAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Order order)
    {
        var cart = await CartRepository.FindAsync(conn, tx, new CartOwner(order.CustomerId, null));
        if (cart is null) return;
        cart.RemovePurchased(order.Lines.Select(l => (l.OfferId, l.Quantity)));
        await CartRepository.SaveAsync(conn, tx, cart);
    }
}
