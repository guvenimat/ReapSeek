using System.Text.Json.Serialization;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Products;
using ReapSeek.Api.Recommendations;
using ReapSeek.Api.Search;

namespace ReapSeek.Api.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(StoreProduct))]
[JsonSerializable(typeof(CachedRecommendations))]
[JsonSerializable(typeof(List<CategoryCache.CategoryRecord>))]
public partial class CacheJsonContext : JsonSerializerContext;
