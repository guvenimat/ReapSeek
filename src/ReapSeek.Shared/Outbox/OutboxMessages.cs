using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dapper;
using Npgsql;

namespace ReapSeek.Shared.Outbox;

public static class OutboxTypes
{
    public const string UserRegistered = "user_registered";
    public const string ProductsChanged = "products_changed";
    public const string CatalogReindex = "catalog_reindex";
    public const string OrderPlaced = "order_placed";
    public const string OrderPaid = "order_paid";
    public const string OrderPaymentFailed = "order_payment_failed";
    public const string OrderCancelled = "order_cancelled";
    public const string PackageShipped = "package_shipped";
    public const string PackageDelivered = "package_delivered";
}

public sealed record UserRegistered(long UserId, string Email, string Name);
public sealed record ProductsChanged(List<long> ProductIds);
public sealed record CatalogReindex(string Reason);
public sealed record OrderEventMessage(Guid OrderId, string Number, long CustomerId, string? Reason, DateTime OccurredAt);
public sealed record PackageEventMessage(Guid OrderId, string Number, long CustomerId, Guid PackageId, string? TrackingNumber, DateTime OccurredAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UserRegistered))]
[JsonSerializable(typeof(ProductsChanged))]
[JsonSerializable(typeof(CatalogReindex))]
[JsonSerializable(typeof(OrderEventMessage))]
[JsonSerializable(typeof(PackageEventMessage))]
public sealed partial class OutboxJsonContext : JsonSerializerContext;

public static class OutboxWriter
{
    public static Task EnqueueAsync<T>(NpgsqlConnection conn, NpgsqlTransaction tx, string type, T payload, JsonTypeInfo<T> typeInfo) =>
        conn.ExecuteAsync("INSERT INTO outbox (type, payload) VALUES (@Type, @Payload::jsonb)",
            new { Type = type, Payload = JsonSerializer.Serialize(payload, typeInfo) }, tx);
}
