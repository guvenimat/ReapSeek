using System.Text.Json.Serialization;

namespace ReapSeek.Sellers.Contracts;

public sealed record SellerChangedV1(long SellerId, string Name, string Slug, string City, string Status, decimal Rating, long Version)
{
    public const string EventName = "sellers.seller_changed.v1";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SellerChangedV1))]
public sealed partial class SellersEventsJsonContext : JsonSerializerContext;
