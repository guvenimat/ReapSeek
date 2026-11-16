using ReapSeek.Catalog.Contracts;

namespace ReapSeek.Catalog.Api;

internal sealed record RecommenderItem(string ItemId, double Score, string Source);
internal sealed record RecommenderResponse(string Model, List<RecommenderItem> Items);
internal sealed record CachedRecommendations(DateTime FetchedAt, List<RecommenderItem> Items);

internal sealed record RecommendedProduct(ProductCard Product, string Source);
internal sealed record RecommendationsResponse(string Placement, string Model, string ServedFrom, List<RecommendedProduct> Items);
