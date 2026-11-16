using System.Text.Json.Serialization;
using ReapSeek.Catalog.Api;

namespace ReapSeek.Catalog.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(StoreProduct))]
[JsonSerializable(typeof(CachedRecommendations))]
[JsonSerializable(typeof(List<CategoryCache.CategoryRecord>))]
internal sealed partial class CatalogCacheJsonContext : JsonSerializerContext;
