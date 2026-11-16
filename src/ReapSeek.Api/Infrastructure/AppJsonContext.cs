using ReapSeek.Finance.Contracts;
using System.Text.Json.Serialization;
using ReapSeek.Api.Health;
using ReapSeek.Api.Operations;
using ReapSeek.Api.Sellers.SellerPanel;
using ReapSeek.Api.Sellers;
using ReapSeek.Api.Sellers.Stores;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Ordering.Contracts;

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
[JsonSerializable(typeof(OutboxResponse))]
[JsonSerializable(typeof(OutboxRetryRequest))]
[JsonSerializable(typeof(OutboxRetryResult))]
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
public sealed partial class AppJsonContext : JsonSerializerContext;
