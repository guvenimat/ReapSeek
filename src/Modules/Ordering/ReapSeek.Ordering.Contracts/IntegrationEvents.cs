using System.Text.Json.Serialization;

namespace ReapSeek.Ordering.Contracts;

public sealed record PackageDeliveredV1(Guid PackageId, string OrderNumber, long SellerId, decimal Subtotal, decimal ShippingFee, decimal CommissionRate,
    DateTime DeliveredAt)
{
    public const string EventName = "ordering.package_delivered.v1";
}

public sealed record ReturnRefundedV1(Guid ReturnId, Guid PackageId, long SellerId, string OrderNumber, decimal GrossAmount, decimal CommissionRate,
    DateTime RefundedAt)
{
    public const string EventName = "ordering.return_refunded.v1";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PackageDeliveredV1))]
[JsonSerializable(typeof(ReturnRefundedV1))]
public sealed partial class OrderingEventsJsonContext : JsonSerializerContext;
