using System.Text.Json.Serialization;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Finance.Contracts;
using ReapSeek.Ordering.Contracts;

namespace ReapSeek.Sellers.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CategoriesResponse))]
[JsonSerializable(typeof(BrandsResponse))]
[JsonSerializable(typeof(ProductsResponse))]
[JsonSerializable(typeof(ProductDetail))]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(OfferRequest))]
[JsonSerializable(typeof(SellerRow))]
[JsonSerializable(typeof(SellerAccount))]
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
internal sealed partial class SellersJsonContext : JsonSerializerContext;
