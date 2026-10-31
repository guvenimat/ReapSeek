using System.Text.Json;
using ReapSeek.Api.Carts;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Search;
using ReapSeek.Shared;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Caching;

namespace ReapSeek.Api.Recommendations;

public sealed class RecommendationService(
    RecommenderClient recommender, RedisCache cache, SearchService search, CartService carts, SessionService sessions, IConfiguration cfg,
    TimeProvider clock, ILogger<RecommendationService> log)
{
    static readonly TimeSpan StaleTtl = TimeSpan.FromHours(24);
    readonly TimeSpan _freshFor = TimeSpan.FromSeconds(cfg.GetValue("Recommendations:CacheSeconds", 300));

    public async Task<RecommendationsResponse> GetAsync(string placementName, long? productId, int? limit, HttpContext ctx, CancellationToken ct)
    {
        var placement = RecommendationRules.Find(placementName)
            ?? throw new ValidationException($"placement: {string.Join(" | ", RecommendationRules.Placements.Select(p => p.Name))}");
        var size = Ensure.InRange(limit ?? RecommendationRules.DefaultLimit, 1, RecommendationRules.MaxLimit, "limit");
        Ensure.That(placement.Kind != PlacementKind.Product || productId is > 0, "product_id is required for this placement");

        long[] sources = placement.Kind switch
        {
            PlacementKind.Product => [productId!.Value],
            PlacementKind.Cart => (await carts.ViewAsync(ctx, ct)).Packages.SelectMany(p => p.Lines).Select(l => l.ProductId)
                .Distinct().Take(RecommendationRules.MaxCartSources).ToArray(),
            _ => [],
        };
        if (placement.Kind == PlacementKind.Cart && sources.Length == 0)
            return new RecommendationsResponse(placement.Name, placement.Model, ListOrigin.None, []);

        string? person = null;
        if (placement.Kind == PlacementKind.Personal)
        {
            if (!recommender.CanPersonalize || await sessions.GetCurrentAsync(ctx, SessionAudience.Storefront) is not { } user)
                return new RecommendationsResponse(placement.Name, placement.Model, ListOrigin.None, []);
            person = ReapDeepIdentity.DistinctId(user.Id);
        }

        var fetch = RecommendationRules.FetchSize(size);
        var loaded = placement.Kind switch
        {
            PlacementKind.Global => [await LoadListAsync(placement.Model, null, fetch, ct)],
            PlacementKind.Personal => [await LoadListAsync(placement.Model, null, fetch, ct, person)],
            _ => await Task.WhenAll(sources.Select(id => LoadListAsync(placement.Model, id.ToString(), fetch, ct))),
        };

        var exclude = sources.ToHashSet();
        var category = sources.Length > 0 && placement.Fill != FillPolicy.None ? await search.CategoryOfAsync(sources[0], ct) : null;
        var candidates = RecommendationRules.Merge(loaded.Select(l => l.Candidates).ToList(), exclude, fetch);
        var sourceById = candidates.ToDictionary(c => c.ProductId, c => c.Source);
        var items = (await search.InStockCardsAsync(candidates.Select(c => c.ProductId).ToList(), ct))
            .Where(card => RecommendationRules.Keeps(placement, sourceById[card.Id], card.CategoryName, category?.Name))
            .Take(size).Select(card => new RecommendedProduct(card, sourceById[card.Id])).ToList();

        if (items.Count < size && placement.Fill != FillPolicy.None)
        {
            var taken = exclude.Concat(items.Select(i => i.Product.Id)).ToHashSet();
            var fill = await search.FallbackCardsAsync(category?.Path, taken, size - items.Count, ct);
            items.AddRange(fill.Select(card => new RecommendedProduct(card, RecommendationRules.FallbackSource)));
        }

        return new RecommendationsResponse(placement.Name, placement.Model, RecommendationRules.ServedFrom(loaded.Select(l => l.Origin).ToList()),
            RecommendationRules.Shown(placement, items));
    }

    async Task<(List<Candidate> Candidates, string Origin)> LoadListAsync(string model, string? itemId, int fetch, CancellationToken ct,
        string? person = null)
    {
        var key = CacheKeys.Recommendations(model, itemId ?? person);
        var cached = await cache.GetAsync(key, CacheJsonContext.Default.CachedRecommendations);
        var now = clock.GetUtcNow().UtcDateTime;
        if (cached is not null && now - cached.FetchedAt < _freshFor)
            return (RecommendationRules.FromRecommender(cached.Items), ListOrigin.Cache);
        if (!recommender.IsConfigured)
            return ([], ListOrigin.None);

        try
        {
            var items = await recommender.GetAsync(model, itemId, fetch, ct, person);
            await cache.SetAsync(key, new CachedRecommendations(now, items), CacheJsonContext.Default.CachedRecommendations, StaleTtl);
            return (RecommendationRules.FromRecommender(items), ListOrigin.ReapDeep);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning("ReapDeep recommender failed for {Model}/{Item}, {Fallback}: {Message}", model, itemId ?? person ?? "-",
                cached is null ? "using the fallback list" : "serving the last list", ex.Message);
            return cached is null ? ([], ListOrigin.None) : (RecommendationRules.FromRecommender(cached.Items), ListOrigin.Stale);
        }
    }
}
