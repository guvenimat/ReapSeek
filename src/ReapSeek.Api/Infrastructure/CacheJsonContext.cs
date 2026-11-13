using System.Text.Json.Serialization;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Catalog.Products;
using ReapSeek.Api.Catalog.Recommendations;
using ReapSeek.Api.Catalog.Search;

namespace ReapSeek.Api.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(StoreProduct))]
[JsonSerializable(typeof(CachedRecommendations))]
[JsonSerializable(typeof(List<CategoryCache.CategoryRecord>))]
public partial class CacheJsonContext : JsonSerializerContext;
