using System.Text.Json.Serialization;

namespace ReapSeek.Ordering.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(AddToCartRequest))]
[JsonSerializable(typeof(UpdateCartLineRequest))]
[JsonSerializable(typeof(CartView))]
[JsonSerializable(typeof(CartSummary))]
[JsonSerializable(typeof(PlaceOrderRequest))]
[JsonSerializable(typeof(PlaceOrderResponse))]
[JsonSerializable(typeof(CouponPreview))]
[JsonSerializable(typeof(AddressesResponse))]
[JsonSerializable(typeof(OrdersPage))]
[JsonSerializable(typeof(OrderDetail))]
[JsonSerializable(typeof(CreateReturnRequest))]
[JsonSerializable(typeof(CreatedReturn))]
[JsonSerializable(typeof(RejectReturnRequest))]
[JsonSerializable(typeof(SellerReturnRow))]
[JsonSerializable(typeof(SellerReturnsResponse))]
internal sealed partial class OrderingJsonContext : JsonSerializerContext;
