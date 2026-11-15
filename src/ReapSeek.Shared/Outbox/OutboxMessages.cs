using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Observability;

namespace ReapSeek.Shared.Outbox;

public static class OutboxTypes
{
    public const string ProductsChanged = "products_changed";
    public const string CatalogReindex = "catalog_reindex";
    public const string OrderPlaced = "order_placed";
    public const string OrderPaid = "order_paid";
    public const string OrderPaymentFailed = "order_payment_failed";
    public const string OrderCancelled = "order_cancelled";
    public const string PackageShipped = "package_shipped";
    public const string PackageDelivered = "package_delivered";
    public const string ReturnRequested = "return_requested";
    public const string ReturnApproved = "return_approved";
    public const string ReturnRejected = "return_rejected";
    public const string ReturnReceived = "return_received";
    public const string ReturnRefunded = "return_refunded";
    public const string CartReminder = "cart_reminder";
    public const string OutboxFailed = "outbox_failed";
    public const string OrderRefundDue = "order_refund_due";
    public const string PackageShipReminder = "package_ship_reminder";
    public const string ReapDeepCatalog = "reapdeep_catalog";
    public const string ReapDeepEvents = "reapdeep_events";
}

public sealed record ProductsChanged(List<long> ProductIds);
public sealed record CatalogReindex(string Reason);
public sealed record ReapDeepCatalogMessage(List<long> ProductIds, bool All);
public sealed record OrderRefundMessage(Guid OrderId, string Number, long CustomerId, string PaymentReference, decimal Amount, DateTime OccurredAt, Guid? PackageId = null);
public sealed record PackageReminderMessage(Guid PackageId);
public sealed record OrderEventMessage(Guid OrderId, string Number, long CustomerId, string? Reason, DateTime OccurredAt);
public sealed record CartReminderMessage(Guid CartId, long CustomerId);
public sealed record OutboxFailedMessage(long MessageId, string Type);
public sealed record ReturnEventMessage(Guid ReturnId, string Number, DateTime OccurredAt);
public sealed record PackageEventMessage(Guid OrderId, string Number, long CustomerId, Guid PackageId, string? TrackingNumber, DateTime OccurredAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ProductsChanged))]
[JsonSerializable(typeof(CatalogReindex))]
[JsonSerializable(typeof(ReapDeepCatalogMessage))]
[JsonSerializable(typeof(OrderEventMessage))]
[JsonSerializable(typeof(OrderRefundMessage))]
[JsonSerializable(typeof(PackageReminderMessage))]
[JsonSerializable(typeof(PackageEventMessage))]
[JsonSerializable(typeof(ReturnEventMessage))]
[JsonSerializable(typeof(CartReminderMessage))]
[JsonSerializable(typeof(OutboxFailedMessage))]
public sealed partial class OutboxJsonContext : JsonSerializerContext;

public sealed record OutboxInsertRecord(string Type, string Payload, string? TraceParent);

public static class OutboxWriter
{
    public static Task EnqueueAsync<T>(NpgsqlConnection conn, NpgsqlTransaction? tx, string type, T payload, JsonTypeInfo<T> typeInfo) =>
        conn.ExecuteAsync("INSERT INTO platform.outbox (type, payload, trace_parent) VALUES (@Type, @Payload::jsonb, @TraceParent)",
            new OutboxInsertRecord(type, JsonSerializer.Serialize(payload, typeInfo), Telemetry.CurrentTraceParent()), tx);
}
