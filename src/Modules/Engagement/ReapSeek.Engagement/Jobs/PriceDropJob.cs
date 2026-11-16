using Dapper;
using Npgsql;
using ReapSeek.Modules.Catalog;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Engagement.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReapSeek.Engagement.Jobs;

internal sealed class PriceDropJob(NpgsqlDataSource db, IConfiguration cfg, ILogger<PriceDropJob> log) : BackgroundService
{
    static readonly ModuleOutbox Outbox = new(MessagingModules.Engagement);

    public const decimal MinimumDrop = 0.05m;
    public sealed record DropRecord(long CustomerId, long ProductId, decimal OldPrice, decimal NewPrice);
    public sealed record CandidateRecord(long CustomerId, long ProductId, decimal Base);
    public sealed record DueRecord(long[] CustomerIds, long[] ProductIds, decimal[] Bases, decimal[] Prices);

    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Wishlist:PriceCheckSeconds", 300));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var count = await RunOnceAsync(ct);
                if (count > 0) log.LogInformation("Queued {Count} wishlist price drop notices", count);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Wishlist price check failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var candidates = (await conn.QueryAsync<CandidateRecord>("""
            SELECT customer_id, product_id, coalesce(alert_price, added_price) AS base FROM engagement.wishlist_items
            WHERE coalesce(alert_price, added_price) IS NOT NULL
            """, transaction: tx)).AsList();
        var prices = await CatalogLookups.LowestPricesAsync(conn, candidates.Select(c => c.ProductId), tx);
        var due = candidates
            .Where(c => prices.TryGetValue(c.ProductId, out var price) && price <= Math.Round(c.Base * (1 - MinimumDrop), 2, MidpointRounding.AwayFromZero))
            .Take(500).ToList();
        var drops = due.Count == 0 ? [] : (await conn.QueryAsync<DropRecord>("""
            UPDATE engagement.wishlist_items w SET alert_price = d.price
            FROM unnest(@CustomerIds, @ProductIds, @Bases, @Prices) AS d(customer_id, product_id, base, price)
            WHERE w.customer_id = d.customer_id AND w.product_id = d.product_id AND coalesce(w.alert_price, w.added_price) = d.base
            RETURNING w.customer_id, w.product_id, d.base AS old_price, d.price AS new_price
            """, new DueRecord(due.Select(d => d.CustomerId).ToArray(), due.Select(d => d.ProductId).ToArray(), due.Select(d => d.Base).ToArray(),
            due.Select(d => prices[d.ProductId]).ToArray()), tx)).AsList();
        foreach (var d in drops)
            await Outbox.PublishAsync(conn, tx, PriceDroppedV1.EventName, new PriceDroppedV1(d.CustomerId, d.ProductId, d.OldPrice, d.NewPrice),
                EngagementEventsJsonContext.Default.PriceDroppedV1);
        await tx.CommitAsync(ct);
        return drops.Count;
    }
}
