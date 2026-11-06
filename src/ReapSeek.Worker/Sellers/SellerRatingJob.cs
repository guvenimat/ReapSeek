using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Worker.Sellers;

public sealed class SellerRatingJob(NpgsqlDataSource db, IConfiguration cfg, ILogger<SellerRatingJob> log) : BackgroundService
{
    public const decimal FallbackPrior = 4.0m;
    public const int PriorWeight = 10;
    public const int WindowDays = 180;

    public sealed record RatingParamsRecord(decimal Prior, int Weight, int WindowDays);
    public sealed record ChangeRecord(long SellerId, long? ProductId);
    public sealed record Result(int Sellers, int Products);

    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Sellers:RatingRefreshSeconds", 3600));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await RunOnceAsync(ct);
                if (result.Sellers > 0)
                    log.LogInformation("Updated the rating of {Sellers} sellers, {Products} products queued for reindexing", result.Sellers, result.Products);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Seller rating refresh failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<Result> RunOnceAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var changes = (await conn.QueryAsync<ChangeRecord>("""
            WITH attributed AS (
                SELECT DISTINCT ON (r.id) r.id, r.rating, pk.seller_id
                FROM reviews r
                JOIN orders o ON o.customer_id = r.customer_id
                JOIN order_lines l ON l.order_id = o.id AND l.product_id = r.product_id
                JOIN order_packages pk ON pk.id = l.package_id AND pk.status = 'delivered'
                WHERE r.status = 'visible' AND r.updated_at >= now() - make_interval(days => @WindowDays)
                ORDER BY r.id, pk.delivered_at DESC NULLS LAST, pk.id),
            prior AS (SELECT coalesce(avg(rating), @Prior) AS mean FROM attributed),
            scores AS (
                SELECT s.id, round((@Weight * prior.mean + coalesce(sum(a.rating), 0)) / (@Weight + count(a.id)), 1) AS rating
                FROM sellers s CROSS JOIN prior LEFT JOIN attributed a ON a.seller_id = s.id
                GROUP BY s.id, prior.mean),
            changed AS (
                UPDATE sellers s SET rating = scores.rating FROM scores
                WHERE s.id = scores.id AND s.rating IS DISTINCT FROM scores.rating
                RETURNING s.id)
            SELECT DISTINCT c.id AS seller_id, v.product_id
            FROM changed c
            LEFT JOIN offers o ON o.seller_id = c.id
            LEFT JOIN product_variants v ON v.id = o.variant_id
            """, new RatingParamsRecord(FallbackPrior, PriorWeight, WindowDays), tx)).AsList();

        var products = changes.Where(c => c.ProductId is not null).Select(c => c.ProductId!.Value).Distinct().ToList();
        if (products.Count > 0)
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged(products), OutboxJsonContext.Default.ProductsChanged);
        await tx.CommitAsync(ct);
        return new Result(changes.Select(c => c.SellerId).Distinct().Count(), products.Count);
    }
}
