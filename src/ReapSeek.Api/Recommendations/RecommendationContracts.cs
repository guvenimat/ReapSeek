using ReapSeek.Api.Search;

namespace ReapSeek.Api.Recommendations;

public sealed record RecommenderItem(string ItemId, double Score, string Source);
public sealed record RecommenderResponse(string Model, List<RecommenderItem> Items);
public sealed record CachedRecommendations(DateTime FetchedAt, List<RecommenderItem> Items);

public sealed record RecommendedProduct(ProductCard Product, string Source);
public sealed record RecommendationsResponse(string Placement, string Model, string ServedFrom, List<RecommendedProduct> Items);
