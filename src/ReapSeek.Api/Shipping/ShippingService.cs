using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Payments;
using ReapSeek.Domain.Ledger;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Providers;
using ReapSeek.Shared.Returns;

namespace ReapSeek.Api.Shipping;

public enum ShippingWebhookOutcome { Applied, Duplicate, Rejected, Malformed }

public sealed class ShippingService(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<ShippingService> log)
{
    public sealed record PackageRefRecord(Guid Id, Guid OrderId, string Status, string? ShipmentStatus);
    public sealed record ShipmentEventRecord(string EventId, string TrackingNumber, string Kind, string Status, DateTime OccurredAt);
    public sealed record ShipmentStatusRecord(string TrackingNumber, string Status);
    public sealed record TrackingRecord(string Tracking);

    public static readonly string[] Steps = ["label_created", "in_transit", "out_for_delivery", "delivered"];
    readonly string _secret = cfg["Payments:Sandbox:WebhookSecret"] ?? "";
    readonly TimeSpan _hold = TimeSpan.FromDays(cfg.GetValue("Ledger:HoldDays", Settlement.DefaultHold.TotalDays));

    public async Task<ShippingWebhookOutcome> HandleSandboxWebhookAsync(string body, string? signature, CancellationToken ct)
    {
        if (!WebhookVerifier.IsValid(signature, body, _secret, clock.GetUtcNow())) return ShippingWebhookOutcome.Rejected;
        SandboxShipmentEvent? e;
        try
        {
            e = JsonSerializer.Deserialize(body, SandboxJsonContext.Default.SandboxShipmentEvent);
        }
        catch (JsonException)
        {
            return ShippingWebhookOutcome.Malformed;
        }
        if (e?.Data is not { } d || string.IsNullOrEmpty(e.Id) || Array.IndexOf(Steps, d.Status) < 0 || d.Kind is not (ShipmentKinds.Outbound or ShipmentKinds.Return))
            return ShippingWebhookOutcome.Malformed;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var inserted = await conn.ExecuteAsync("""
            INSERT INTO shipment_events (event_id, tracking_number, kind, status, occurred_at) VALUES (@EventId, @TrackingNumber, @Kind, @Status, @OccurredAt)
            ON CONFLICT (event_id) DO NOTHING
            """, new ShipmentEventRecord(e.Id, d.TrackingNumber, d.Kind, d.Status, d.OccurredAt), tx);
        if (inserted == 0) return ShippingWebhookOutcome.Duplicate;

        if (d.Kind == ShipmentKinds.Outbound)
        {
            var package = await conn.QuerySingleOrDefaultAsync<PackageRefRecord>(
                "SELECT id, order_id, status, shipment_status FROM order_packages WHERE tracking_number = @Tracking FOR UPDATE", new TrackingRecord(d.TrackingNumber), tx);
            if (package is null)
            {
                log.LogWarning("Shipment event {Event} for unknown tracking number {Tracking}", e.Id, d.TrackingNumber);
                await tx.CommitAsync(ct);
                return ShippingWebhookOutcome.Applied;
            }
            if (Array.IndexOf(Steps, d.Status) > Array.IndexOf(Steps, package.ShipmentStatus ?? ""))
                await conn.ExecuteAsync("UPDATE order_packages SET shipment_status = @Status WHERE tracking_number = @TrackingNumber",
                    new ShipmentStatusRecord(d.TrackingNumber, d.Status), tx);
            if (d.Status == "delivered" && package.Status == OrderStatusRules.Name(PackageStatus.Shipped))
                await PackageDelivery.DeliverAsync(conn, tx, package.OrderId, package.Id, d.OccurredAt, _hold);
        }
        else if (d.Status == "delivered" && await ReturnStore.LoadByTrackingAsync(conn, tx, d.TrackingNumber) is { } request)
        {
            if (request.MarkReceived(d.OccurredAt)) await ReturnStore.SaveAsync(conn, tx, request);
        }
        await tx.CommitAsync(ct);
        return ShippingWebhookOutcome.Applied;
    }
}

public static class ShippingEndpoints
{
    public static void MapShipping(this RouteGroupBuilder api) =>
        api.MapPost("/shipping/sandbox/webhook", async (HttpRequest request, ShippingService shipping, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            return await shipping.HandleSandboxWebhookAsync(body, request.Headers["Sandbox-Signature"], ct) switch
            {
                ShippingWebhookOutcome.Rejected => ApiResults.Unauthorized("Invalid signature"),
                ShippingWebhookOutcome.Malformed => ApiResults.BadRequest("Malformed event"),
                _ => Results.Ok(),
            };
        });
}
