using Dapper;
using Npgsql;
using ReapSeek.Catalog.Application;
using ReapSeek.Modules.Catalog;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Catalog.Infrastructure;

internal sealed class SellerSnapshotRepository(NpgsqlDataSource db)
{
    internal sealed record SnapshotIdRecord(long Id);

    public async Task<bool> ApplyAsync(SellerSnapshotStateRecord next, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var current = await conn.QuerySingleOrDefaultAsync<SellerSnapshotStateRecord>("""
            SELECT id, name, slug, city, status, rating, version FROM catalog.seller_snapshots WHERE id = @Id FOR UPDATE
            """, new SnapshotIdRecord(next.Id), tx);
        if (!SellerSnapshotRules.Supersedes(current, next)) return false;
        var applied = await conn.ExecuteAsync("""
            INSERT INTO catalog.seller_snapshots (id, name, slug, city, status, rating, version)
            VALUES (@Id, @Name, @Slug, @City, @Status, @Rating, @Version)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, slug = excluded.slug, city = excluded.city, status = excluded.status,
                rating = excluded.rating, version = excluded.version
            WHERE catalog.seller_snapshots.version < excluded.version
            """, next, tx);
        if (applied == 0) return false;
        if (SellerSnapshotRules.ChangesProducts(current, next))
        {
            var productIds = await SellerSnapshots.ProductIdsOfSellerAsync(conn, tx, next.Id);
            if (productIds.Count > 0)
                await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged(productIds), OutboxJsonContext.Default.ProductsChanged);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
