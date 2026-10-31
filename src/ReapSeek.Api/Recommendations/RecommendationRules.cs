using System.Globalization;

namespace ReapSeek.Api.Recommendations;

public enum PlacementKind { Global, Product, Cart, Personal }

public enum FillPolicy { Anything, SameCategory, None }

public sealed record Placement(string Name, string Model, PlacementKind Kind, FillPolicy Fill);

public readonly record struct Candidate(long ProductId, double Score, string Source);

public static class RecommendationRules
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 24;
    public const int MaxCartSources = 3;
    public const string FallbackSource = "fallback";
    public const int RankFusionK = 60;
    public const int MinPersonalItems = 4;

    public static readonly Placement[] Placements =
    [
        new("home_popular", "popular", PlacementKind.Global, FillPolicy.Anything),
        new("home_trending", "trending", PlacementKind.Global, FillPolicy.Anything),
        new("product_similar", "co_viewed", PlacementKind.Product, FillPolicy.SameCategory),
        new("product_bought_together", "bought_together", PlacementKind.Product, FillPolicy.None),
        new("cart", "bought_together", PlacementKind.Cart, FillPolicy.None),
        new("home_for_you", "for_you", PlacementKind.Personal, FillPolicy.None),
    ];

    public static Placement? Find(string name) => Placements.FirstOrDefault(p => p.Name == name);

    public static int FetchSize(int limit) => Math.Min(limit * 2, 50);

    public static List<Candidate> FromRecommender(IEnumerable<RecommenderItem> items) =>
        items.Select(i => long.TryParse(i.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? new Candidate(id, i.Score, i.Source) : (Candidate?)null)
            .OfType<Candidate>().ToList();

    public static List<Candidate> Merge(IReadOnlyList<List<Candidate>> lists, IReadOnlySet<long> exclude, int limit)
    {
        var merged = new Dictionary<long, (double Score, string Source, int FirstSeen)>();
        var order = 0;
        foreach (var list in lists)
            for (var rank = 0; rank < list.Count; rank++)
            {
                var c = list[rank];
                if (exclude.Contains(c.ProductId)) continue;
                var score = 1.0 / (RankFusionK + rank + 1);
                merged[c.ProductId] = merged.TryGetValue(c.ProductId, out var seen)
                    ? (seen.Score + score, seen.Source, seen.FirstSeen)
                    : (score, c.Source, order++);
            }
        return merged.OrderByDescending(kv => kv.Value.Score).ThenBy(kv => kv.Value.FirstSeen)
            .Take(limit).Select(kv => new Candidate(kv.Key, kv.Value.Score, kv.Value.Source)).ToList();
    }

    public static List<T> Shown<T>(Placement placement, List<T> items) =>
        placement.Kind == PlacementKind.Personal && items.Count < MinPersonalItems ? [] : items;

    public static bool Keeps(Placement placement, string source, string itemCategory, string? sourceCategory) => placement.Fill switch
    {
        FillPolicy.Anything => true,
        FillPolicy.SameCategory => source == placement.Model || (sourceCategory is not null && itemCategory == sourceCategory),
        _ => source == placement.Model,
    };

    public static string ServedFrom(IReadOnlyCollection<string> origins) =>
        origins.Count == 0 || origins.All(o => o == ListOrigin.None) ? ListOrigin.None
        : origins.Contains(ListOrigin.Stale) ? ListOrigin.Stale
        : origins.Contains(ListOrigin.ReapDeep) ? ListOrigin.ReapDeep
        : ListOrigin.Cache;
}

public static class ListOrigin
{
    public const string ReapDeep = "reapdeep";
    public const string Cache = "cache";
    public const string Stale = "stale";
    public const string None = "none";
}
