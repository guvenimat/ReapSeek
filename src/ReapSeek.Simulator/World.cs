namespace ReapSeek.Simulator;

public sealed record SimProduct(long Id, string Title, string Brand, string CategoryPath, string CategoryName, string Department);

public enum Persona { Champion, Loyal, Newcomer, AtRisk, Lapsed, Browser }

public sealed record SimCustomer(int Index, string FirstName, string LastName, Persona Persona, DateTime SignedUpAt, IReadOnlyList<string> Departments)
{
    public string Email => $"{FirstName.ToLowerInvariant()}.{LastName.ToLowerInvariant()}.{Index}@shoppers.example.com";
    public string Name => $"{FirstName} {LastName}";
}

public enum SimEventKind { Search, View, AddToCart, CheckoutStarted }

public sealed record SimEvent(SimEventKind Kind, DateTime At, long? ProductId, string? Query, int Quantity = 1);
public sealed record SimLine(long ProductId, int Quantity);
public sealed record SimReview(int Rating, DateTime At);

public sealed record SimOrder(DateTime PlacedAt, IReadOnlyList<SimLine> Lines, bool ReturnFirstLine, IReadOnlyDictionary<long, SimReview> Reviews);
public sealed record SimSession(IReadOnlyList<SimEvent> Events, SimOrder? Order);
public sealed record SimQuestion(long ProductId, int CustomerIndex, int Template, DateTime At, bool Answered);

public sealed record WorldOptions(int Customers, int Days, int Seed, DateTime Now);

public sealed record World(IReadOnlyList<SimCustomer> Customers, IReadOnlyList<IReadOnlyList<SimSession>> Sessions, IReadOnlyList<SimQuestion> Questions)
{
    public IEnumerable<SimOrder> Orders => Sessions.SelectMany(s => s).Select(s => s.Order).OfType<SimOrder>();
}

public static class WorldGenerator
{
    static readonly (Persona Persona, double Share)[] Mix =
    [
        (Persona.Champion, 0.10), (Persona.Loyal, 0.20), (Persona.Newcomer, 0.14), (Persona.AtRisk, 0.16), (Persona.Lapsed, 0.18), (Persona.Browser, 0.22),
    ];

    public static World Generate(IReadOnlyList<SimProduct> products, WorldOptions o)
    {
        if (products.Count == 0) throw new InvalidOperationException("The catalog has no products to shop");
        var rng = new Random(o.Seed);
        var byCategory = products.GroupBy(p => p.CategoryPath).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Id).ToArray());
        var categories = byCategory.Keys.Order(StringComparer.Ordinal).ToArray();
        var departments = products.Select(p => p.Department).Distinct().Order(StringComparer.Ordinal).ToArray();
        var companion = Companions(categories, products);
        var quality = products.ToDictionary(p => p.Id, p => Quality(p.Id));

        var customers = new List<SimCustomer>(o.Customers);
        var sessions = new List<IReadOnlyList<SimSession>>(o.Customers);
        for (var i = 0; i < o.Customers; i++)
        {
            var persona = PickPersona(rng);
            var start = o.Now.AddDays(-o.Days);
            var signedUp = persona == Persona.Newcomer ? o.Now.AddDays(-rng.Next(1, 21)) : start.AddDays(rng.NextDouble() * o.Days * 0.4);
            var count = rng.NextDouble() < 0.35 ? 2 : 1;
            var depts = departments.OrderBy(_ => rng.Next()).Take(count).ToArray();
            var customer = new SimCustomer(i + 1, Names.First[rng.Next(Names.First.Length)], Names.Last[rng.Next(Names.Last.Length)], persona, signedUp, depts);
            customers.Add(customer);
            sessions.Add(SessionsFor(customer, o, rng, byCategory, categories, companion, quality));
        }
        return new World(customers, sessions, Questions(products, customers, o, rng));
    }

    public static double Quality(long productId)
    {
        var x = (ulong)productId * 0x9E3779B97F4A7C15UL;
        x ^= x >> 29;
        return (x % 1000) / 1000.0;
    }

    static Persona PickPersona(Random rng)
    {
        var roll = rng.NextDouble();
        foreach (var (persona, share) in Mix)
        {
            if (roll < share) return persona;
            roll -= share;
        }
        return Persona.Browser;
    }

    static Dictionary<string, string> Companions(string[] categories, IReadOnlyList<SimProduct> products)
    {
        var dept = products.GroupBy(p => p.CategoryPath).ToDictionary(g => g.Key, g => g.First().Department);
        var result = new Dictionary<string, string>();
        foreach (var group in categories.GroupBy(c => dept[c]))
        {
            var list = group.ToArray();
            for (var i = 0; i < list.Length; i++) result[list[i]] = list[(i + 1) % list.Length];
        }
        return result;
    }

    static (double Every, DateTime? Until, int Orders) Rhythm(Persona persona, DateTime now, Random rng) => persona switch
    {
        Persona.Champion => (rng.Next(3, 7), null, int.MaxValue),
        Persona.Loyal => (rng.Next(9, 18), null, int.MaxValue),
        Persona.Newcomer => (rng.Next(3, 8), null, 1),
        Persona.AtRisk => (rng.Next(6, 12), now.AddDays(-rng.Next(60, 95)), int.MaxValue),
        Persona.Lapsed => (rng.Next(12, 25), now.AddDays(-rng.Next(100, 160)), 3),
        _ => (rng.Next(6, 15), null, 0),
    };

    static List<SimSession> SessionsFor(SimCustomer c, WorldOptions o, Random rng, Dictionary<string, SimProduct[]> byCategory, string[] categories,
        Dictionary<string, string> companion, Dictionary<long, double> quality)
    {
        var (every, until, maxOrders) = Rhythm(c.Persona, o.Now, rng);
        var favorites = categories.Where(cat => c.Departments.Any(d => cat.StartsWith(d + "/", StringComparison.Ordinal))).ToArray();
        if (favorites.Length == 0) favorites = categories;
        var end = until ?? o.Now.AddHours(-1);
        var result = new List<SimSession>();
        var orders = 0;
        for (var t = c.SignedUpAt.AddHours(rng.Next(1, 48)); t < end; t = t.AddDays(every * (0.6 + rng.NextDouble() * 0.8)))
        {
            var buys = orders < maxOrders && rng.NextDouble() < (c.Persona == Persona.Browser ? 0 : 0.65);
            var session = Session(t, favorites[rng.Next(favorites.Length)], buys, rng, byCategory, companion, quality, o.Now);
            if (session.Order is not null) orders++;
            result.Add(session);
        }
        return result;
    }

    static SimProduct Popular(SimProduct[] list, Random rng)
    {
        var rank = (int)Math.Floor(Math.Pow(rng.NextDouble(), 2.2) * list.Length);
        return list[Math.Min(rank, list.Length - 1)];
    }

    static SimSession Session(DateTime start, string category, bool buys, Random rng, Dictionary<string, SimProduct[]> byCategory,
        Dictionary<string, string> companion, Dictionary<long, double> quality, DateTime now)
    {
        var at = start;
        DateTime Next() => at = at.AddSeconds(rng.Next(20, 140));
        var events = new List<SimEvent>();
        var pool = byCategory[category];
        if (rng.NextDouble() < 0.45) events.Add(new SimEvent(SimEventKind.Search, Next(), null, pool[0].CategoryName.ToLowerInvariant()));
        var viewed = new List<SimProduct>();
        var views = rng.Next(2, 8);
        for (var i = 0; i < views; i++)
        {
            var source = rng.NextDouble() < 0.8 ? pool : byCategory[companion[category]];
            var p = Popular(source, rng);
            viewed.Add(p);
            events.Add(new SimEvent(SimEventKind.View, Next(), p.Id, null));
        }
        var carted = viewed.Where(p => p.CategoryPath == category).DistinctBy(p => p.Id)
            .OrderByDescending(p => quality[p.Id] + rng.NextDouble()).Take(buys ? rng.Next(1, 3) : rng.NextDouble() < 0.25 ? 1 : 0).ToList();
        if (buys && rng.NextDouble() < 0.35)
        {
            var extra = Popular(byCategory[companion[category]], rng);
            events.Add(new SimEvent(SimEventKind.View, Next(), extra.Id, null));
            carted.Add(extra);
        }
        var lines = carted.DistinctBy(p => p.Id).Select(p => new SimLine(p.Id, rng.NextDouble() < 0.85 ? 1 : 2)).ToList();
        foreach (var l in lines) events.Add(new SimEvent(SimEventKind.AddToCart, Next(), l.ProductId, null, l.Quantity));
        if (!buys || lines.Count == 0) return new SimSession(events, null);

        events.Add(new SimEvent(SimEventKind.CheckoutStarted, Next(), null, null));
        var placed = Next();
        var delivered = placed.AddDays(3);
        var returns = delivered < now.AddDays(-2) && rng.NextDouble() < 0.04 + (1 - quality[lines[0].ProductId]) * 0.08;
        var reviews = new Dictionary<long, SimReview>();
        foreach (var l in lines)
        {
            var reviewAt = delivered.AddDays(rng.Next(1, 9));
            if (reviewAt >= now || rng.NextDouble() > 0.3) continue;
            var q = quality[l.ProductId];
            var rating = Math.Clamp((int)Math.Round(1.6 + q * 3.6 + (rng.NextDouble() - 0.5) * 1.6), 1, 5);
            reviews[l.ProductId] = new SimReview(returns && l == lines[0] ? Math.Min(rating, 2) : rating, reviewAt);
        }
        return new SimSession(events, new SimOrder(placed, lines, returns, reviews));
    }

    static List<SimQuestion> Questions(IReadOnlyList<SimProduct> products, List<SimCustomer> customers, WorldOptions o, Random rng)
    {
        var list = new List<SimQuestion>();
        foreach (var p in products.OrderBy(p => p.Id))
        {
            if (rng.NextDouble() > 0.12) continue;
            var count = rng.Next(1, 3);
            for (var i = 0; i < count; i++)
                list.Add(new SimQuestion(p.Id, customers[rng.Next(customers.Count)].Index, rng.Next(Texts.Questions.Length),
                    o.Now.AddDays(-rng.Next(1, o.Days)).AddMinutes(rng.Next(0, 1440)), rng.NextDouble() < 0.8));
        }
        return list;
    }
}
