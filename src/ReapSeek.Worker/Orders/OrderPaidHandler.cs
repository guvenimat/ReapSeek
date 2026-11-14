using ReapSeek.Modules.Identity;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Modules.Ordering;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed class OrderPaidHandler(NpgsqlDataSource db, ReapDeepIngestClient reapDeep, EmailSender email, IConfiguration cfg)
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
        var customer = await People.GetAsync(conn, order.CustomerId);

        await reapDeep.EnqueueAsync(conn, PurchaseEvents.For(order));
        await email.SendAsync(EmailTemplates.OrderConfirmation(order, customer.Email, customer.Name, _storefrontUrl), ct);
    }

    public async Task HandleShippedAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.PackageEventMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var order = await OrderStore.LoadByNumberAsync(conn, null, payload.Number)
            ?? throw new InvalidOperationException($"Order {payload.Number} was not found");
        var package = order.Packages.Single(p => p.Id == payload.PackageId);
        var customer = await People.GetAsync(conn, order.CustomerId);
        await email.SendAsync(EmailTemplates.PackageShipped(order, package, customer.Email, customer.Name, _storefrontUrl), ct);
    }
}
