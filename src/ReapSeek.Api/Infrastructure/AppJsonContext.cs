using System.Text.Json.Serialization;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Health;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Products;
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
[JsonSerializable(typeof(SitemapResponse))]
public sealed partial class AppJsonContext : JsonSerializerContext;
