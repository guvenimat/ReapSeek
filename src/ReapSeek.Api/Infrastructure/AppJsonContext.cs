using ReapSeek.Finance.Contracts;
using ReapSeek.Api.Ordering.Shipping;
using System.Text.Json.Serialization;
using ReapSeek.Api.Ordering.Carts;
using ReapSeek.Api.Ordering.Checkout;
using ReapSeek.Api.Health;
using ReapSeek.Api.Operations;
using ReapSeek.Api.Ordering.Orders;
using ReapSeek.Api.Ordering.Returns;
using ReapSeek.Api.Sellers.SellerPanel;
using ReapSeek.Api.Sellers;
using ReapSeek.Api.Sellers.Stores;
using ReapSeek.Catalog.Contracts;

namespace ReapSeek.Api.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(CategoriesResponse))]
[JsonSerializable(typeof(BrandsResponse))]
[JsonSerializable(typeof(ProductsResponse))]
[JsonSerializable(typeof(ProductDetail))]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(OfferRequest))]
[JsonSerializable(typeof(SellerRow))]
[JsonSerializable(typeof(SellerAccount))]
[JsonSerializable(typeof(CouponPreview))]
[JsonSerializable(typeof(OutboxResponse))]
[JsonSerializable(typeof(OutboxRetryRequest))]
[JsonSerializable(typeof(OutboxRetryResult))]
[JsonSerializable(typeof(CreateReturnRequest))]
[JsonSerializable(typeof(CreatedReturn))]
[JsonSerializable(typeof(RejectReturnRequest))]
[JsonSerializable(typeof(SellerReturnRow))]
[JsonSerializable(typeof(SellerReturnsResponse))]
[JsonSerializable(typeof(SellerOffersResponse))]
[JsonSerializable(typeof(SellerProductView))]
[JsonSerializable(typeof(ProposalsResponse))]
[JsonSerializable(typeof(SellerPackagesResponse))]
[JsonSerializable(typeof(SellerPackageRow))]
[JsonSerializable(typeof(SellerBalanceResponse))]
[JsonSerializable(typeof(PayoutsResponse))]
[JsonSerializable(typeof(PayoutRunResult))]
[JsonSerializable(typeof(ReviewRequest))]
[JsonSerializable(typeof(CreateSellerUserRequest))]
[JsonSerializable(typeof(SellerUserRow))]
[JsonSerializable(typeof(SellerUsersResponse))]
[JsonSerializable(typeof(SellersResponse))]
[JsonSerializable(typeof(CreateSellerRequest))]
[JsonSerializable(typeof(UpdateSellerRequest))]
[JsonSerializable(typeof(StoreSellerResponse))]
[JsonSerializable(typeof(AddToCartRequest))]
[JsonSerializable(typeof(UpdateCartLineRequest))]
[JsonSerializable(typeof(CartView))]
[JsonSerializable(typeof(CartSummary))]
[JsonSerializable(typeof(PlaceOrderRequest))]
[JsonSerializable(typeof(PlaceOrderResponse))]
[JsonSerializable(typeof(AddressesResponse))]
[JsonSerializable(typeof(OrdersPage))]
[JsonSerializable(typeof(OrderDetail))]
public sealed partial class AppJsonContext : JsonSerializerContext;
