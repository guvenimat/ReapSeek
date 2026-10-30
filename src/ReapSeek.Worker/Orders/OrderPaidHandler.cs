using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed class OrderPaidHandler(NpgsqlDataSource db, ReapDeepIngestClient reapDeep, EmailSender email, IConfiguration cfg, ILogger<OrderPaidHandler> log)
{
    public sealed record CustomerRecord(string Email, string Name);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.OrderEventMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var order = await OrderStore.LoadByNumberAsync(conn, null, payload.Number)
            ?? throw new InvalidOperationException($"Order {payload.Number} was not found");
        var customer = await conn.QuerySingleAsync<CustomerRecord>("SELECT email, name FROM users WHERE id = @Id", new { Id = order.CustomerId });

        if (reapDeep.IsConfigured)
        {
            var events = PurchaseEvents.For(order);
            await reapDeep.CaptureAsync(events, ct);
            log.LogInformation("Sent {Count} purchase events for {Order} to ReapDeep", events.Count, order.Number);
        }
        await email.SendAsync(EmailTemplates.OrderConfirmation(order, customer.Email, customer.Name, _storefrontUrl), ct);
    }
}
