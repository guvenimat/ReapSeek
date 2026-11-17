using Dapper;
using Npgsql;
using ReapSeek.Modules.Engagement;
using ReapSeek.Modules.Ordering;
using ReapSeek.Sellers.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReapSeek.Sellers.Jobs;

internal sealed class SellerRatingJob(NpgsqlDataSource db, IConfiguration cfg, ILogger<SellerRatingJob> log) : BackgroundService
{
    public const decimal FallbackPrior = 4.0m;
    public const int PriorWeight = 10;
    public const int WindowDays = 180;

    public sealed record ScoresRecord(long[] Ids, decimal[] Ratings);

    public static Dictionary<long, decimal> Scores(IEnumerable<long> sellerIds, IReadOnlyCollection<(long SellerId, int Rating)> attributed)
    {
        var mean = attributed.Count == 0 ? FallbackPrior : (decimal)attributed.Average(a => a.Rating);
        var bySeller = attributed.ToLookup(a => a.SellerId, a => a.Rating);
        return sellerIds.ToDictionary(id => id, id =>
            Math.Round((PriorWeight * mean + bySeller[id].Sum()) / (PriorWeight + bySeller[id].Count()), 1, MidpointRounding.AwayFromZero));
    }

    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Sellers:RatingRefreshSeconds", 3600));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var sellers = await RunOnceAsync(ct);
                if (sellers > 0)
                    log.LogInformation("Updated the rating of {Sellers} sellers", sellers);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Seller rating refresh failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var reviews = await ReviewStats.RecentVisibleAsync(conn, tx, WindowDays);
        var sellerOf = (await OrderLookups.DeliveredSellersAsync(conn, tx, reviews.Select(r => (r.CustomerId, r.ProductId)).Distinct().ToList()))
            .ToDictionary(d => (d.CustomerId, d.ProductId), d => d.SellerId);
        var attributed = reviews.Where(r => sellerOf.ContainsKey((r.CustomerId, r.ProductId)))
            .Select(r => (SellerId: sellerOf[(r.CustomerId, r.ProductId)], r.Rating)).ToList();
        var sellerIds = (await conn.QueryAsync<long>("SELECT id FROM sellers.sellers", transaction: tx)).ToArray();
        var scores = Scores(sellerIds, attributed);
        var changes = (await conn.QueryAsync<SellerVersionRecord>("""
            UPDATE sellers.sellers s SET rating = n.rating, version = s.version + 1
            FROM unnest(@Ids, @Ratings) AS n(id, rating)
            WHERE s.id = n.id AND s.rating IS DISTINCT FROM n.rating
            RETURNING s.id, s.name, s.slug, s.city, s.status, s.rating, s.version
            """, new ScoresRecord(scores.Keys.ToArray(), scores.Values.ToArray()), tx)).AsList();

        foreach (var change in changes) await SellerChanges.PublishAsync(conn, tx, change);
        await tx.CommitAsync(ct);
        return changes.Count;
    }
}
