using ReapSeek.Api.Recommendations;

namespace ReapSeek.UnitTests;

public class RecommendationTests
{
    [Fact]
    public void Placements_map_to_reapdeep_models()
    {
        Assert.Equal(("popular", PlacementKind.Global), (RecommendationRules.Find("home_popular")!.Model, RecommendationRules.Find("home_popular")!.Kind));
        Assert.Equal("co_viewed", RecommendationRules.Find("product_similar")!.Model);
        Assert.Equal(PlacementKind.Cart, RecommendationRules.Find("cart")!.Kind);
        Assert.Null(RecommendationRules.Find("sidebar"));
    }

    [Fact]
    public void Item_ids_that_are_not_product_ids_are_ignored()
    {
        var candidates = RecommendationRules.FromRecommender(
        [
            new RecommenderItem("12", 0.9, "co_viewed"),
            new RecommenderItem("sku-1", 0.8, "co_viewed"),
            new RecommenderItem("-3", 0.7, "co_viewed"),
            new RecommenderItem("0", 0.6, "co_viewed"),
            new RecommenderItem("40", 0.5, "popular"),
        ]);
        Assert.Equal([12L, 40L], candidates.Select(c => c.ProductId));
        Assert.Equal("popular", candidates[1].Source);
    }

    [Fact]
    public void A_single_list_keeps_the_recommender_order_whatever_the_scores()
    {
        List<Candidate> list = [new(48, 0.2, "co_viewed"), new(80, 31, "popular"), new(62, 12, "popular")];
        Assert.Equal([48L, 80L, 62L], RecommendationRules.Merge([list], new HashSet<long>(), 10).Select(c => c.ProductId));
    }

    [Fact]
    public void Cart_lists_are_merged_by_rank_without_the_cart_items()
    {
        List<Candidate> forA = [new(2, 0.9, "bought_together"), new(10, 0.5, "bought_together"), new(11, 40, "popular")];
        List<Candidate> forB = [new(1, 0.9, "bought_together"), new(11, 0.4, "bought_together"), new(12, 0.3, "bought_together")];
        var merged = RecommendationRules.Merge([forA, forB], new HashSet<long> { 1, 2 }, 10);
        Assert.Equal([11L, 10L, 12L], merged.Select(c => c.ProductId));
        Assert.Equal("popular", merged[0].Source);
        Assert.Equal(2, RecommendationRules.Merge([forA, forB], new HashSet<long>(), 2).Count);
    }

    [Fact]
    public void Equal_ranks_keep_the_first_list_first()
    {
        List<Candidate> a = [new(5, 1, "bought_together")];
        List<Candidate> b = [new(3, 9, "bought_together")];
        Assert.Equal([5L, 3L], RecommendationRules.Merge([a, b], new HashSet<long>(), 10).Select(c => c.ProductId));
    }

    [Fact]
    public void Fill_policy_depends_on_the_placement()
    {
        var home = RecommendationRules.Find("home_popular")!;
        var similar = RecommendationRules.Find("product_similar")!;
        var together = RecommendationRules.Find("product_bought_together")!;
        Assert.True(RecommendationRules.Keeps(home, "popular", "Lamps", null));
        Assert.True(RecommendationRules.Keeps(similar, "co_viewed", "Lamps", "Dresses"));
        Assert.True(RecommendationRules.Keeps(similar, "popular", "Dresses", "Dresses"));
        Assert.False(RecommendationRules.Keeps(similar, "popular", "Lamps", "Dresses"));
        Assert.True(RecommendationRules.Keeps(together, "bought_together", "Lamps", "Dresses"));
        Assert.False(RecommendationRules.Keeps(together, "popular", "Dresses", "Dresses"));
        Assert.False(RecommendationRules.Keeps(RecommendationRules.Find("cart")!, "popular", "Dresses", null));
    }

    [Theory]
    [InlineData(new[] { "cache", "reapdeep" }, "reapdeep")]
    [InlineData(new[] { "cache", "stale" }, "stale")]
    [InlineData(new[] { "none", "none" }, "none")]
    [InlineData(new[] { "none", "cache" }, "cache")]
    [InlineData(new string[0], "none")]
    public void Served_from_reports_the_weakest_list(string[] origins, string expected) =>
        Assert.Equal(expected, RecommendationRules.ServedFrom(origins));

    [Fact]
    public void Fetch_size_leaves_room_for_filtered_items() =>
        Assert.Equal((16, 48, 50), (RecommendationRules.FetchSize(8), RecommendationRules.FetchSize(24), RecommendationRules.FetchSize(30)));
}
