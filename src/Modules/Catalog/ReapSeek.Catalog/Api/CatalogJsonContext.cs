using System.Text.Json.Serialization;
using ReapSeek.Catalog.Contracts;

namespace ReapSeek.Catalog.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
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
[JsonSerializable(typeof(ProductImagesResponse))]
[JsonSerializable(typeof(CategoryTreeResponse))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(ProductCardsResponse))]
[JsonSerializable(typeof(SuggestResponse))]
[JsonSerializable(typeof(StoreProduct))]
[JsonSerializable(typeof(RecommenderResponse))]
[JsonSerializable(typeof(RecommendationsResponse))]
[JsonSerializable(typeof(SitemapResponse))]
internal sealed partial class CatalogJsonContext : JsonSerializerContext;
