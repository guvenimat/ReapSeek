using System.Text.Json.Serialization;
using ReapSeek.Api.Carts;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Checkout;
using ReapSeek.Api.Health;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Orders;
using ReapSeek.Api.Products;
using ReapSeek.Api.Recommendations;
using ReapSeek.Api.Search;
using ReapSeek.Api.Sellers;

namespace ReapSeek.Api.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(PasswordChangeRequest))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(CategoryRow))]
[JsonSerializable(typeof(CategoriesResponse))]
[JsonSerializable(typeof(CreateCategoryRequest))]
[JsonSerializable(typeof(BrandRow))]
[JsonSerializable(typeof(BrandsResponse))]
[JsonSerializable(typeof(CreateBrandRequest))]
[JsonSerializable(typeof(ProductsResponse))]
[JsonSerializable(typeof(ProductDetail))]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(UpdateProductRequest))]
[JsonSerializable(typeof(OfferRequest))]
[JsonSerializable(typeof(CatalogStats))]
[JsonSerializable(typeof(SellerRow))]
[JsonSerializable(typeof(SellersResponse))]
[JsonSerializable(typeof(CreateSellerRequest))]
[JsonSerializable(typeof(UpdateSellerRequest))]
[JsonSerializable(typeof(CategoryTreeResponse))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(SuggestResponse))]
[JsonSerializable(typeof(StoreProduct))]
[JsonSerializable(typeof(RecommenderResponse))]
[JsonSerializable(typeof(RecommendationsResponse))]
[JsonSerializable(typeof(SitemapResponse))]
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
