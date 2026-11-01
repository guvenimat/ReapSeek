using System.Diagnostics;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ledger;
using ReapSeek.Domain.Ordering;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Identity;
using ReapSeek.Shared.Ledger;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Outbox;
using ReapSeek.Shared.Payouts;
using ReapSeek.Shared.Returns;

namespace ReapSeek.Simulator;

public static class SimAccounts
{
    public const string ShopperPassword = "shopper-pass-1";
    public const string SellerPassword = "seller-pass-1";
    public const string ShopperDomain = "@shoppers.example.com";
    public const string SellerDomain = "@sellers.example.com";
    public static string SellerEmail(long sellerId) => $"seller-{sellerId}{SellerDomain}";
}

public sealed class Backfill(NpgsqlDataSource db, Ingest ingest, PayoutRunner payouts)
{
    public sealed record CustomerInsertRecord(string[] Emails, string[] Names, string Hash, DateTime[] CreatedAt);
    public sealed record CustomerIdRecord(long Id, string Email);
    public sealed record SellerRecord(long Id, string Name);

    static readonly string[] SafeReasons = ["wrong_size", "changed_mind", "not_as_described"];

    public async Task<bool> HasShoppersAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM users WHERE email LIKE '%' || @Domain)", new { Domain = SimAccounts.ShopperDomain });
    }

    public async Task RunAsync(WorldOptions options)
    {
        var clock = Stopwatch.StartNew();
        await using var conn = await db.OpenConnectionAsync();
        if (await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM users WHERE email LIKE '%' || @Domain)", new { Domain = SimAccounts.ShopperDomain }))
            throw new InvalidOperationException("Simulated shoppers already exist. Start from a fresh database (docker compose down -v) to backfill again.");

        var catalog = await Catalog.LoadAsync(conn);
        var world = WorldGenerator.Generate(catalog.Products, options);
        Console.WriteLine($"Generated {world.Customers.Count} shoppers, {world.Sessions.Sum(s => s.Count)} sessions and {world.Orders.Count()} orders over {options.Days} days");

        var shopperHash = PasswordHasher.Hash(SimAccounts.ShopperPassword);
        var ids = await InsertCustomersAsync(conn, world, shopperHash);
        await CreateSellerLoginsAsync(conn);

        var rng = new Random(options.Seed + 1);
        var orders = 0;
        foreach (var customer in world.Customers)
        {
            var id = ids[customer.Email];
            var distinctId = ReapDeepIdentity.DistinctId(id);
            await using var tx = await conn.BeginTransactionAsync();
            var s = 0;
            foreach (var session in world.Sessions[customer.Index - 1])
            {
                s++;
                await SendSessionAsync(distinctId, $"sim-{id}-{s}", session, catalog);
                if (session.Order is { } order && order.PlacedAt < options.Now.AddHours(-1))
                {
                    await PlaceAsync(conn, tx, id, distinctId, order, catalog, options.Now, rng);
                    orders++;
                }
            }
            await tx.CommitAsync();
            if (customer.Index % 250 == 0) Console.WriteLine($"  {customer.Index} shoppers, {orders} orders, {ingest.Sent} events sent ({clock.Elapsed.TotalSeconds:0} s)");
        }
        await ingest.FlushAsync();

        await InsertQuestionsAsync(conn, world, ids, catalog);
        var weeks = 0;
        for (var monday = PayoutPeriod.CurrentPeriodEnd(options.Now.AddDays(-options.Days + 7), PayoutPeriod.Turkey); monday <= options.Now; monday = monday.AddDays(7))
        {
            await payouts.RunAsync(monday.AddHours(1), CancellationToken.None);
            weeks++;
        }
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CatalogReindex, new CatalogReindex("simulator backfill"), OutboxJsonContext.Default.CatalogReindex);
            await tx.CommitAsync();
        }
        await using (var analyze = new NpgsqlCommand("ANALYZE", conn)) await analyze.ExecuteNonQueryAsync();
        Console.WriteLine($"Backfill done in {clock.Elapsed.TotalSeconds:0} s: {ids.Count} shoppers, {orders} orders, {ingest.Sent} ReapDeep events, {weeks} payout weeks");
    }

    static async Task<Dictionary<string, long>> InsertCustomersAsync(NpgsqlConnection conn, World world, string hash)
    {
        var rows = await conn.QueryAsync<CustomerIdRecord>("""
            INSERT INTO users (email, name, password_hash, role, created_at, email_verified_at)
            SELECT e, n, @Hash, 'customer', c, c FROM unnest(@Emails, @Names, @CreatedAt) AS u(e, n, c)
            RETURNING id, email
            """, new CustomerInsertRecord(world.Customers.Select(c => c.Email).ToArray(), world.Customers.Select(c => c.Name).ToArray(), hash,
            world.Customers.Select(c => c.SignedUpAt).ToArray()));
        return rows.ToDictionary(r => r.Email, r => r.Id);
    }

    static async Task CreateSellerLoginsAsync(NpgsqlConnection conn)
    {
        var hash = PasswordHasher.Hash(SimAccounts.SellerPassword);
        foreach (var s in await conn.QueryAsync<SellerRecord>("SELECT id, name FROM sellers s WHERE status = 'active' AND NOT EXISTS (SELECT 1 FROM users u WHERE u.seller_id = s.id)"))
            await conn.ExecuteAsync("""
                INSERT INTO users (email, name, password_hash, role, seller_id, email_verified_at) VALUES (@Email, @Name, @Hash, 'seller', @SellerId, now()) ON CONFLICT (email) DO NOTHING
                """, new { Email = SimAccounts.SellerEmail(s.Id), Name = s.Name, Hash = hash, SellerId = s.Id });
    }

    async Task SendSessionAsync(string distinctId, string sessionId, SimSession session, Catalog catalog)
    {
        var n = 0;
        foreach (var e in session.Events)
        {
            n++;
            var eventId = $"{sessionId}-{n}";
            var offer = e.ProductId is { } pid ? catalog.Offers[pid] : null;
            var product = e.ProductId is { } p ? catalog.ById[p] : null;
            switch (e.Kind)
            {
                case SimEventKind.Search:
                    await ingest.AddAsync("search_performed", distinctId, eventId, e.At, new Dictionary<string, object?> { ["query"] = e.Query, ["results"] = 24, ["source"] = "simulator" });
                    break;
                case SimEventKind.View:
                    await ingest.AddAsync("product_viewed", distinctId, eventId, e.At, new Dictionary<string, object?>
                    {
                        ["item_id"] = e.ProductId!.Value.ToString(), ["title"] = product!.Title, ["brand"] = product.Brand, ["category"] = product.CategoryPath.Split('/')[^1],
                        ["department"] = product.Department, ["price"] = offer!.Price, ["seller_id"] = offer.SellerId, ["in_stock"] = true,
                    });
                    break;
                case SimEventKind.AddToCart:
                    await ingest.AddAsync("add_to_cart", distinctId, eventId, e.At, new Dictionary<string, object?>
                    {
                        ["item_id"] = e.ProductId!.Value.ToString(), ["quantity"] = e.Quantity, ["price"] = offer!.Price, ["seller_id"] = offer.SellerId,
                    });
                    break;
                case SimEventKind.CheckoutStarted:
                    await ingest.AddAsync("checkout_started", distinctId, eventId, e.At, new Dictionary<string, object?> { ["source"] = "simulator" });
                    break;
            }
        }
    }

    async Task PlaceAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long customerId, string distinctId, SimOrder sim, Catalog catalog, DateTime now, Random rng)
    {
        var items = sim.Lines.Select(l =>
        {
            var o = catalog.Offers[l.ProductId];
            var p = catalog.ById[l.ProductId];
            return new OrderItem(o.OfferId, o.ProductId, o.VariantId, o.SellerId, o.SellerName, p.Title, o.VariantLabel, o.Sku, o.ImageUrl, Money.Of(o.Price), l.Quantity);
        }).ToList();
        var address = ShippingAddress.Create(Names.First[rng.Next(Names.First.Length)] + " " + Names.Last[rng.Next(Names.Last.Length)], "0100 000 00 00",
            Cities[rng.Next(Cities.Length)], "Center", $"Sample Street {rng.Next(1, 200)}", "35000");
        var order = Order.Place(Guid.CreateVersion7(), await OrderStore.NextNumberAsync(conn, tx, sim.PlacedAt), customerId, address, items,
            ShippingPolicy.Standard, Guid.CreateVersion7, sim.PlacedAt);
        var paidAt = sim.PlacedAt.AddMinutes(2);
        order.AttachPayment($"pay_sim_{order.Id:N}");
        order.MarkPaid(order.PaymentReference!, order.Total, paidAt);
        foreach (var package in order.Packages)
        {
            var shippedAt = paidAt.AddHours(rng.Next(6, 30));
            if (shippedAt < now) order.ShipPackage(package.Id, package.SellerId, $"SC{rng.NextInt64(1_000_000_000, 9_999_999_999)}", shippedAt);
            var deliveredAt = shippedAt.AddHours(rng.Next(30, 70));
            if (deliveredAt < now) order.DeliverPackage(package.Id, deliveredAt);
        }
        order.ClearDomainEvents();
        await OrderStore.InsertAsync(conn, tx, order);
        await OrderStore.SaveAsync(conn, tx, order);
        foreach (var package in order.Packages)
        {
            var status = package.Status switch { PackageStatus.Delivered => "delivered", PackageStatus.Shipped => "in_transit", _ => null };
            if (status is not null)
                await conn.ExecuteAsync("UPDATE order_packages SET shipment_status = @Status WHERE id = @Id", new { Id = package.Id, Status = status }, tx);
            if (package.Status != PackageStatus.Delivered) continue;
            var rate = await conn.ExecuteScalarAsync<decimal>("SELECT commission_rate FROM order_packages WHERE id = @Id", new { Id = package.Id }, tx);
            var entries = Settlement.ForPackage(package, rate);
            await LedgerStore.InsertAsync(conn, tx, new LedgerInsertRecord(package.SellerId, package.Id, null, order.Number,
                entries.Select(e => e.Kind).ToArray(), entries.Select(e => e.Amount).ToArray(), Settlement.AvailableAt(package.DeliveredAt!.Value, Settlement.DefaultHold)));
        }

        foreach (var package in order.Packages)
            foreach (var l in package.Lines)
                await ingest.AddAsync("purchase", distinctId, $"order-line-{l.Id}", paidAt, new Dictionary<string, object?>
                {
                    ["item_id"] = l.ProductId.ToString(), ["order_id"] = order.Number, ["price"] = l.UnitPrice.Amount, ["quantity"] = l.Quantity, ["amount"] = l.Total.Amount,
                    ["currency"] = "TRY", ["seller_id"] = package.SellerId, ["sku"] = l.Sku, ["source"] = "server",
                });

        foreach (var (productId, review) in sim.Reviews)
        {
            var package = order.Packages.First(p => p.Lines.Any(l => l.ProductId == productId));
            if (package.Status != PackageStatus.Delivered) continue;
            var (title, body) = Texts.Review(review.Rating, rng.Next(10));
            await conn.ExecuteAsync("""
                INSERT INTO reviews (product_id, customer_id, rating, title, body, variant_label, created_at, updated_at)
                VALUES (@ProductId, @CustomerId, @Rating, @Title, @Body, @Variant, @At, @At) ON CONFLICT (product_id, customer_id) DO NOTHING
                """, new { ProductId = productId, CustomerId = customerId, review.Rating, Title = title, Body = body,
                Variant = package.Lines.First(l => l.ProductId == productId).VariantLabel, review.At }, tx);
        }

        if (sim.ReturnFirstLine) await ReturnAsync(conn, tx, order, customerId, distinctId, now, rng);
    }

    async Task ReturnAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Order order, long customerId, string distinctId, DateTime now, Random rng)
    {
        var package = order.Packages[0];
        if (package.DeliveredAt is not { } delivered) return;
        var requested = delivered.AddDays(rng.Next(1, 5));
        if (requested >= now) return;
        var rate = await conn.ExecuteScalarAsync<decimal>("SELECT commission_rate FROM order_packages WHERE id = @Id", new { Id = package.Id }, tx);
        var line = package.Lines[0];
        var request = ReturnRequest.Request(Guid.CreateVersion7(), await ReturnStore.NextNumberAsync(conn, tx, requested), order, package.Id, customerId,
            [new ReturnItem(line.Id, 1)], SafeReasons[rng.Next(SafeReasons.Length)], null, rate, new Dictionary<Guid, int>(), requested);
        IReadOnlyList<LedgerEntry>? entries = null;
        if (requested.AddDays(1) < now) request.Approve(package.SellerId, $"SC{rng.NextInt64(1_000_000_000, 9_999_999_999)}", requested.AddDays(1));
        if (requested.AddDays(3) < now) request.MarkReceived(requested.AddDays(3));
        if (requested.AddDays(4) < now) entries = request.MarkRefunded($"re_sim_{request.Id:N}", requested.AddDays(4));
        request.ClearDomainEvents();
        await ReturnStore.InsertAsync(conn, tx, request);
        await ReturnStore.SaveAsync(conn, tx, request);
        if (entries is null) return;
        await LedgerStore.InsertAsync(conn, tx, new LedgerInsertRecord(request.SellerId, request.PackageId, request.Id, request.OrderNumber,
            entries.Select(e => e.Kind).ToArray(), entries.Select(e => e.Amount).ToArray(), request.RefundedAt!.Value));
        await ingest.AddAsync("refund", distinctId, $"return-line-{request.Id}-{line.Id}", request.RefundedAt!.Value, new Dictionary<string, object?>
        {
            ["item_id"] = line.ProductId.ToString(), ["order_id"] = order.Number, ["return_id"] = request.Number, ["price"] = line.UnitPrice.Amount, ["quantity"] = 1,
            ["amount"] = request.RefundAmount.Amount, ["currency"] = "TRY", ["seller_id"] = request.SellerId, ["sku"] = line.Sku, ["source"] = "server",
        });
    }

    static async Task InsertQuestionsAsync(NpgsqlConnection conn, World world, Dictionary<string, long> ids, Catalog catalog)
    {
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var q in world.Questions)
        {
            var (question, answer) = Texts.Questions[q.Template];
            var customerId = ids[world.Customers[q.CustomerIndex - 1].Email];
            var id = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO questions (product_id, customer_id, body, created_at) VALUES (@ProductId, @CustomerId, @Body, @At) RETURNING id",
                new { q.ProductId, CustomerId = customerId, Body = question, q.At }, tx);
            if (q.Answered)
                await conn.ExecuteAsync("INSERT INTO answers (question_id, seller_id, body, created_at, updated_at) VALUES (@Id, @SellerId, @Body, @At, @At)",
                    new { Id = id, catalog.Offers[q.ProductId].SellerId, Body = answer, At = q.At.AddHours(5) }, tx);
        }
        await tx.CommitAsync();
    }

    static readonly string[] Cities = ["Istanbul", "Ankara", "Izmir", "Bursa", "Antalya", "Konya", "Eskisehir", "Mersin", "Kayseri", "Trabzon"];
}
