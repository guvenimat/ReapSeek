using ReapSeek.Simulator;

namespace ReapSeek.UnitTests;

public class SimulatorTests
{
    static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    static readonly List<SimProduct> Products =
    [
        .. Enumerable.Range(1, 40).Select(i => new SimProduct(i, $"Dress {i}", "Velora", "women/dresses", "Dresses", "women")),
        .. Enumerable.Range(41, 30).Select(i => new SimProduct(i, $"Shoe {i}", "Pace", "women/shoes", "Shoes", "women")),
        .. Enumerable.Range(71, 30).Select(i => new SimProduct(i, $"Phone {i}", "Lumo", "electronics/phones", "Phones", "electronics")),
        .. Enumerable.Range(101, 20).Select(i => new SimProduct(i, $"Headset {i}", "Lumo", "electronics/audio", "Audio", "electronics")),
    ];

    static World Make(int seed = 42) => WorldGenerator.Generate(Products, new WorldOptions(2000, 120, seed, Now));

    [Fact]
    public void The_same_seed_builds_the_same_world()
    {
        var a = Make();
        var b = Make();
        Assert.Equal(a.Customers.Select(c => c.Email), b.Customers.Select(c => c.Email));
        Assert.Equal(a.Orders.Select(o => (o.PlacedAt, o.Lines.Count)), b.Orders.Select(o => (o.PlacedAt, o.Lines.Count)));
        Assert.NotEqual(a.Orders.Count(), Make(43).Orders.Count());
    }

    [Fact]
    public void The_volume_matches_a_medium_store()
    {
        var world = Make();
        var orders = world.Orders.Count();
        Assert.True(orders is >= 4500 and <= 8000, $"{orders} orders");
        Assert.All(world.Customers, c => Assert.EndsWith("@shoppers.example.com", c.Email));
        Assert.All(world.Orders, o => Assert.True(o.PlacedAt < Now));
    }

    [Fact]
    public void Personas_shape_recency_and_frequency()
    {
        var world = Make();
        var byPersona = world.Customers.Select((c, i) => (c.Persona, Orders: world.Sessions[i].Select(s => s.Order).OfType<SimOrder>().ToList())).ToList();
        Assert.All(byPersona.Where(x => x.Persona == Persona.Browser), x => Assert.Empty(x.Orders));
        Assert.All(byPersona.Where(x => x.Persona == Persona.Newcomer), x => Assert.InRange(x.Orders.Count, 0, 1));
        Assert.All(byPersona.Where(x => x.Persona == Persona.AtRisk), x => Assert.All(x.Orders, o => Assert.True(o.PlacedAt < Now.AddDays(-59))));
        var champions = byPersona.Where(x => x.Persona == Persona.Champion).Select(x => x.Orders.Count).Average();
        var loyal = byPersona.Where(x => x.Persona == Persona.Loyal).Select(x => x.Orders.Count).Average();
        Assert.True(champions > loyal * 1.5, $"champions {champions:0.0} vs loyal {loyal:0.0}");
    }

    [Fact]
    public void Sessions_stay_mostly_in_one_category_so_co_views_mean_something()
    {
        var world = Make();
        var category = Products.ToDictionary(p => p.Id, p => p.CategoryPath);
        var views = world.Sessions.SelectMany(s => s).Select(s => s.Events.Where(e => e.Kind == SimEventKind.View).Select(e => category[e.ProductId!.Value]).ToList())
            .Where(v => v.Count > 1).ToList();
        var sameAsFirst = views.Average(v => v.Count(c => c == v[0]) / (double)v.Count);
        Assert.InRange(sameAsFirst, 0.6, 0.95);
    }

    [Fact]
    public void Reviews_follow_product_quality_and_only_come_after_delivery()
    {
        var world = Make();
        var reviews = world.Orders.SelectMany(o => o.Reviews.Select(r => (Product: r.Key, r.Value.Rating, o.PlacedAt, r.Value.At))).ToList();
        Assert.NotEmpty(reviews);
        Assert.All(reviews, r => Assert.True(r.At > r.PlacedAt.AddDays(3) && r.At < Now));
        var good = reviews.Where(r => WorldGenerator.Quality(r.Product) > 0.8).Average(r => r.Rating);
        var poor = reviews.Where(r => WorldGenerator.Quality(r.Product) < 0.2).Average(r => r.Rating);
        Assert.True(good > poor + 1.5, $"good {good:0.0} vs poor {poor:0.0}");
    }
}
