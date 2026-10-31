using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Npgsql;

namespace ReapSeek.Simulator;

public sealed record LiveOptions(string ApiUrl, string SandboxUrl, int Shoppers, TimeSpan Pause, int Seed);

public sealed class Live(NpgsqlDataSource db, Ingest ingest, LiveOptions o)
{
    public sealed record ShopperRecord(long Id, string Email);
    public sealed record SellerTaskRecord(long SellerId, string Email);

    const int LivePool = 60;
    readonly System.Collections.Concurrent.ConcurrentDictionary<long, Session> _sessions = new();
    int _orders, _carts, _views, _reviews, _returns, _shipped;

    sealed class Session(string apiUrl)
    {
        public HttpClient Http { get; } = new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var shoppers = (await conn.QueryAsync<ShopperRecord>("SELECT id, email FROM users WHERE email LIKE '%@shoppers.example.com' ORDER BY random() LIMIT @Limit",
            new { Limit = LivePool })).AsList();
        if (shoppers.Count == 0) throw new InvalidOperationException("No simulated shoppers yet. Run the backfill first.");
        Console.WriteLine($"Live mode: {o.Shoppers} shoppers in parallel, a pause of {o.Pause.TotalSeconds:0} s between steps. Stop with Ctrl+C.");
        var workers = Enumerable.Range(0, o.Shoppers).Select(i => ShopperLoopAsync(shoppers, new Random(o.Seed + i), ct)).ToList();
        workers.Add(SellerLoopAsync(ct));
        workers.Add(ReportAsync(ct));
        await Task.WhenAll(workers);
    }

    async Task ReportAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith(_ => { });
            await ingest.FlushAsync();
            Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} views {_views}, carts {_carts}, orders {_orders}, reviews {_reviews}, returns {_returns}, packages shipped {_shipped}");
        }
    }

    async Task ShopperLoopAsync(List<ShopperRecord> shoppers, Random rng, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var shopper = shoppers[rng.Next(shoppers.Count)];
            try
            {
                await VisitAsync(shopper, rng, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"  shopper {shopper.Id}: {ex.Message}");
            }
            await Pause(rng, ct);
        }
    }

    Task Pause(Random rng, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromMilliseconds(o.Pause.TotalMilliseconds * (0.5 + rng.NextDouble())), ct).ContinueWith(_ => { });

    async Task VisitAsync(ShopperRecord shopper, Random rng, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(shopper.Id, out var s))
        {
            s = new Session(o.ApiUrl);
            if (await PostAsync(s, "api/v1/auth/login", new JsonObject { ["email"] = shopper.Email, ["password"] = SimAccounts.ShopperPassword }, ct) is null) return;
            _sessions[shopper.Id] = s;
        }
        var distinctId = $"customer-{shopper.Id}";

        if (rng.NextDouble() < 0.15 && await AfterSalesAsync(s, distinctId, rng, ct)) return;

        var categories = (await GetAsync(s, "api/v1/catalog/categories", ct))?["departments"]?.AsArray()
            .SelectMany(d => d!["children"]!.AsArray()).Select(c => c!["path"]!.GetValue<string>()).ToList();
        if (categories is null || categories.Count == 0) return;
        var category = categories[rng.Next(categories.Count)];
        var results = (await GetAsync(s, $"api/v1/search?category={Uri.EscapeDataString(category)}&in_stock=true&page_size=24", ct))?["products"]?.AsArray();
        if (results is null || results.Count == 0) return;
        await ingest.AddAsync("search_performed", distinctId, Guid.NewGuid().ToString("N"), DateTime.UtcNow,
            new Dictionary<string, object?> { ["query"] = category, ["results"] = results.Count, ["source"] = "simulator" });

        JsonNode? chosen = null;
        for (var i = rng.Next(1, 4); i > 0; i--)
        {
            var card = results[Math.Min((int)(Math.Pow(rng.NextDouble(), 2) * results.Count), results.Count - 1)]!;
            var product = await GetAsync(s, $"api/v1/catalog/products/{card["slug"]!.GetValue<string>()}", ct);
            if (product is null) continue;
            Interlocked.Increment(ref _views);
            await ingest.AddAsync("product_viewed", distinctId, Guid.NewGuid().ToString("N"), DateTime.UtcNow, new Dictionary<string, object?>
            {
                ["item_id"] = card["id"]!.GetValue<long>().ToString(), ["title"] = card["title"]!.GetValue<string>(), ["brand"] = card["brand"]!.GetValue<string>(),
                ["price"] = card["price"]?.GetValue<decimal>(), ["source"] = "simulator",
            });
            chosen = product;
            await Pause(rng, ct);
        }
        if (chosen is null || rng.NextDouble() > 0.35) return;

        var offer = chosen["variants"]?.AsArray().SelectMany(v => v!["offers"]!.AsArray()).FirstOrDefault();
        if (offer is null) return;
        var offerId = offer["id"]!.GetValue<long>();
        if (await PostAsync(s, "api/v1/cart/items", new JsonObject { ["offer_id"] = offerId, ["quantity"] = 1 }, ct) is null) return;
        Interlocked.Increment(ref _carts);
        await ingest.AddAsync("add_to_cart", distinctId, Guid.NewGuid().ToString("N"), DateTime.UtcNow, new Dictionary<string, object?>
        {
            ["item_id"] = chosen["id"]!.GetValue<long>().ToString(), ["quantity"] = 1, ["price"] = offer["price"]!.GetValue<decimal>(), ["source"] = "simulator",
        });
        if (rng.NextDouble() < 0.4) return;
        await CheckoutAsync(s, distinctId, rng, ct);
    }

    async Task CheckoutAsync(Session s, string distinctId, Random rng, CancellationToken ct)
    {
        var cart = await GetAsync(s, "api/v1/cart", ct);
        if (cart is null || cart["can_checkout"]?.GetValue<bool>() != true) return;
        var total = cart["total"]!.GetValue<decimal>();
        var body = new JsonObject
        {
            ["address"] = new JsonObject
            {
                ["full_name"] = "Simulated Shopper", ["phone"] = "0100 000 00 00", ["city"] = "Izmir", ["district"] = "Konak", ["line"] = "Sample Street 1", ["postal_code"] = "35000",
            },
            ["expected_total"] = total,
        };
        var code = (await GetAsync(s, "api/v1/account/coupons", ct))?["coupons"]?.AsArray().FirstOrDefault()?["code"]?.GetValue<string>();
        if (code is not null && await GetAsync(s, $"api/v1/checkout/coupon?code={code}", ct) is { } preview)
        {
            body["coupon_code"] = code;
            body["expected_total"] = preview["total"]!.GetValue<decimal>();
        }
        await ingest.AddAsync("checkout_started", distinctId, Guid.NewGuid().ToString("N"), DateTime.UtcNow, new Dictionary<string, object?> { ["value"] = total, ["source"] = "simulator" });
        var placed = await PostAsync(s, "api/v1/checkout", body, ct);
        if (placed is null) return;
        var payment = new Uri(placed["payment_url"]!.GetValue<string>());
        var sandbox = new Uri(new Uri(o.SandboxUrl), payment.PathAndQuery);
        using var pay = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var card = rng.NextDouble() < 0.08 ? "4000000000000002" : "4242424242424242";
        var first = await pay.PostAsync(sandbox, Form(("holder", "Simulated Shopper"), ("card_number", card), ("expiry", "12/30"), ("cvc", "123")), ct);
        if (first.StatusCode == HttpStatusCode.Redirect)
            await pay.PostAsync(new Uri(sandbox + "/3ds"), Form(("code", "123456"), ("action", "confirm")), ct);
        Interlocked.Increment(ref _orders);
    }

    async Task<bool> AfterSalesAsync(Session s, string distinctId, Random rng, CancellationToken ct)
    {
        var number = (await GetAsync(s, "api/v1/orders", ct))?["orders"]?.AsArray().Select(x => x!["number"]!.GetValue<string>()).FirstOrDefault();
        if (number is null) return false;
        var order = await GetAsync(s, $"api/v1/orders/{number}", ct);
        var package = order?["packages"]?.AsArray().FirstOrDefault(p => p!["returnable"]?.GetValue<bool>() == true);
        if (package is null) return false;
        var line = package["lines"]!.AsArray()[0]!;
        var productId = line["product_id"]!.GetValue<long>();
        if (rng.NextDouble() < 0.2)
        {
            var created = await PostAsync(s, $"api/v1/orders/{number}/returns", new JsonObject
            {
                ["package_id"] = package["id"]!.GetValue<string>(), ["reason"] = "changed_mind",
                ["items"] = new JsonArray(new JsonObject { ["order_line_id"] = line["id"]!.GetValue<string>(), ["quantity"] = 1 }),
            }, ct);
            if (created is not null) Interlocked.Increment(ref _returns);
            return true;
        }
        var rating = rng.Next(3, 6);
        var (title, text) = Texts.Review(rating, rng.Next(10));
        if (await SendAsync(s, HttpMethod.Put, $"api/v1/catalog/products/{productId}/review", new JsonObject { ["rating"] = rating, ["title"] = title, ["body"] = text }, ct) is not null)
        {
            Interlocked.Increment(ref _reviews);
            await ingest.AddAsync("review_submitted", distinctId, Guid.NewGuid().ToString("N"), DateTime.UtcNow,
                new Dictionary<string, object?> { ["item_id"] = productId.ToString(), ["rating"] = rating, ["edited"] = false });
        }
        return true;
    }

    async Task SellerLoopAsync(CancellationToken ct)
    {
        var sessions = new Dictionary<long, Session>();
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct).ContinueWith(_ => { });
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                var tasks = (await conn.QueryAsync<SellerTaskRecord>("""
                    SELECT DISTINCT u.seller_id, u.email FROM users u
                    WHERE u.email LIKE '%@sellers.example.com' AND (
                        EXISTS (SELECT 1 FROM order_packages p WHERE p.seller_id = u.seller_id AND p.status = 'preparing')
                        OR EXISTS (SELECT 1 FROM returns r WHERE r.seller_id = u.seller_id AND r.status = 'requested'))
                    LIMIT 10
                    """)).AsList();
                foreach (var t in tasks)
                {
                    if (!sessions.TryGetValue(t.SellerId, out var s))
                    {
                        s = new Session(o.ApiUrl);
                        if (await PostAsync(s, "api/v1/backoffice/auth/login", new JsonObject { ["email"] = t.Email, ["password"] = SimAccounts.SellerPassword }, ct) is null) continue;
                        sessions[t.SellerId] = s;
                    }
                    foreach (var p in (await GetAsync(s, "api/v1/seller/packages?status=preparing", ct))?["packages"]?.AsArray() ?? [])
                        if (await PostAsync(s, $"api/v1/seller/packages/{p!["id"]!.GetValue<string>()}/ship", new JsonObject(), ct) is not null) Interlocked.Increment(ref _shipped);
                    foreach (var r in (await GetAsync(s, "api/v1/seller/returns?status=requested", ct))?["returns"]?.AsArray() ?? [])
                        await PostAsync(s, $"api/v1/seller/returns/{r!["id"]!.GetValue<string>()}/approve", new JsonObject(), ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"  sellers: {ex.Message}");
            }
        }
    }

    static FormUrlEncodedContent Form(params (string, string)[] fields) => new(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));

    static async Task<JsonNode?> GetAsync(Session s, string path, CancellationToken ct)
    {
        using var res = await s.Http.GetAsync(path, ct);
        return res.IsSuccessStatusCode ? JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) : null;
    }

    static Task<JsonNode?> PostAsync(Session s, string path, JsonNode body, CancellationToken ct) => SendAsync(s, HttpMethod.Post, path, body, ct);

    static async Task<JsonNode?> SendAsync(Session s, HttpMethod method, string path, JsonNode body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var res = await s.Http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        return res.IsSuccessStatusCode ? (text.Length > 0 ? JsonNode.Parse(text) : new JsonObject()) : null;
    }
}
